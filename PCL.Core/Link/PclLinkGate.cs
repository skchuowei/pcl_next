using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link
{
    /// <summary>
    /// 联机准入网关：房主不再把 MC 端口直接暴露在 overlay 上，只开放一个 gate 端口。
    /// <para>
    /// 只有先走大厅协议登记、拿到房主下发的一次性 ticket 的加入者，才能通过 gate 的
    /// 挑战-应答（nonce + HMAC）进入，随后才被转发到本机 MC 端口。没有协议凭证的连接
    /// （例如直接扫端口、裸 MC 客户端）会在握手阶段被拒绝并记录。
    /// </para>
    /// </summary>
    public static class PclLinkGate
    {
        private const int HandshakeTimeoutMs = 5000;
        private const int MaxLineLength = 256;
        private const int TicketHours = 12;
        private const int MaxPendingHandshakes = 2;
        private const int MaxSessions = 64;
        private const int MaxSessionsPerMachine = 2;

        private static byte[] Key(string networkSecret)
            => SHA256.HashData(Encoding.UTF8.GetBytes("pclnext-gate|" + networkSecret));

        private static string Proof(string networkSecret, string nonce, string ticket)
        {
            byte[] mac = HMACSHA256.HashData(Key(networkSecret),
                Encoding.UTF8.GetBytes(nonce + "|" + ticket));
            return Convert.ToHexString(mac).ToLowerInvariant();
        }

        private static string NewNonce()
            => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        /// <summary>房主侧网关：监听、发凭据、校验握手、把会话转给本机 MC 端口。</summary>
        public sealed class Host : IDisposable
        {
            private sealed class Ticket
            {
                public string MachineId { get; init; } = "";
                public DateTime ExpiresAt { get; init; }
            }

            private sealed class Session
            {
                public string MachineId { get; init; } = "";
                public TcpClient? Client { get; set; }
            }

            private readonly int _listenPort;
            private readonly int _targetPort;
            private readonly string _secret;
            private readonly Action<string>? _log;
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cts = new();
            private readonly Dictionary<string, Ticket> _tickets = new();
            private readonly List<Session> _sessions = new();
            private readonly SemaphoreSlim _pending = new(MaxPendingHandshakes, MaxPendingHandshakes);
            private readonly object _lock = new();
            private int _failureBurst;
            private long _failureWindowStart;
            private long _blockedUntil;

            public Host(int listenPort, int targetPort, string networkSecret, Action<string>? log = null)
            {
                _listenPort = listenPort;
                _targetPort = targetPort;
                _secret = networkSecret;
                _log = log;
                _listener = new TcpListener(IPAddress.Loopback, listenPort);
            }

            public int Port => _listenPort;

            /// <summary>给已登记成员发一张票；同一成员重复请求会换新票（旧票继续有效到过期）。</summary>
            public (string Ticket, DateTime ExpiresAt) IssueTicket(string machineId)
            {
                if (string.IsNullOrWhiteSpace(machineId))
                    throw new ArgumentException("machineId 不能为空");
                string ticket = NewNonce();
                DateTime expires = DateTime.Now.AddHours(TicketHours);
                lock (_lock)
                {
                    _tickets[ticket] = new Ticket { MachineId = machineId, ExpiresAt = expires };
                }
                return (ticket, expires);
            }

            /// <summary>撤销某成员的全部票，并断开其已建立的隧道（踢人时调用）。</summary>
            public void Revoke(string machineId)
            {
                List<Session> victims = new();
                lock (_lock)
                {
                    foreach (string key in _tickets.Where(p => p.Value.MachineId == machineId).Select(p => p.Key).ToList())
                        _tickets.Remove(key);
                    foreach (Session session in _sessions.Where(s => s.MachineId == machineId))
                        victims.Add(session);
                    foreach (Session session in victims)
                        _sessions.Remove(session);
                }
                foreach (Session session in victims)
                {
                    try { session.Client?.Close(); } catch { /* 忽略 */ }
                }
                if (victims.Count > 0)
                    _log?.Invoke($"[gate] 已断开 {machineId} 的 {victims.Count} 条隧道");
            }

            public void Start()
            {
                _listener.Start();
                _log?.Invoke($"[gate] 监听 127.0.0.1:{_listenPort}，转发到 127.0.0.1:{_targetPort}");
                _ = Task.Run(AcceptLoopAsync);
            }

            private async Task AcceptLoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (_cts.IsCancellationRequested)
                            break;
                        _log?.Invoke("[gate] 接受连接失败：" + ex.Message);
                        continue;
                    }
                    _ = Task.Run(() => HandleAsync(client));
                }
            }

            private async Task HandleAsync(TcpClient client)
            {
                bool acquired = false;
                Session? opened = null;
                try
                {
                    if (Environment.TickCount64 < _blockedUntil)
                    {
                        await RejectAsync(client, "too_many_failures").ConfigureAwait(false);
                        return;
                    }
                    if (!await _pending.WaitAsync(0).ConfigureAwait(false))
                    {
                        await RejectAsync(client, "too_many_pending").ConfigureAwait(false);
                        return;
                    }
                    acquired = true;
                    client.NoDelay = true;
                    using NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = HandshakeTimeoutMs;
                    stream.WriteTimeout = HandshakeTimeoutMs;

                    string nonce = NewNonce();
                    WriteLine(stream, "PCLGATE1 " + nonce);
                    string line = ReadLine(stream);
                    string[] parts = line.Split(' ', 2);
                    if (parts.Length != 2)
                    {
                        Fail("bad_format", "", client, stream);
                        return;
                    }
                    string ticket = parts[0];
                    string proof = parts[1];

                    Ticket? found;
                    lock (_lock)
                    {
                        found = _tickets.TryGetValue(ticket, out Ticket? t) ? t : null;
                    }
                    if (found is null || found.ExpiresAt < DateTime.Now)
                    {
                        Fail("unknown_ticket", "", client, stream);
                        return;
                    }
                    string expect = Proof(_secret, nonce, ticket);
                    if (!CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(expect), Encoding.UTF8.GetBytes(proof)))
                    {
                        Fail("bad_proof", found.MachineId, client, stream);
                        return;
                    }

                    Session session = new() { MachineId = found.MachineId, Client = client };
                    lock (_lock)
                    {
                        int mine = _sessions.Count(s => s.MachineId == found.MachineId);
                        if (_sessions.Count >= MaxSessions || mine >= MaxSessionsPerMachine)
                        {
                            Fail("too_many_sessions", found.MachineId, client, stream);
                            return;
                        }
                        _sessions.Add(session);
                        _failureBurst = 0;
                    }
                    opened = session;
                    _log?.Invoke($"[gate] 通过 {found.MachineId}");

                    WriteLine(stream, "OK");
                    // 0 在 Linux 上是非法超时值（.NET 只接受正数或 Timeout.Infinite）
                    stream.ReadTimeout = Timeout.Infinite;
                    stream.WriteTimeout = Timeout.Infinite;
                    await PipeAsync(client, stream).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log?.Invoke("[gate] 握手异常：" + ex.Message);
                    try { client.Close(); } catch { /* 忽略 */ }
                }
                finally
                {
                    if (opened is not null)
                    {
                        lock (_lock)
                        {
                            _sessions.Remove(opened);
                        }
                    }
                    if (acquired)
                        _pending.Release();
                }
            }

            private void Fail(string reason, string machineId, TcpClient client, NetworkStream stream)
            {
                long now = Environment.TickCount64;
                lock (_lock)
                {
                    if (now - _failureWindowStart > 60_000)
                    {
                        _failureWindowStart = now;
                        _failureBurst = 0;
                    }
                    _failureBurst++;
                    if (_failureBurst >= 6)
                        _blockedUntil = now + 60_000;
                }
                _log?.Invoke($"[gate] 拒绝（{reason}）{machineId}");
                try { WriteLine(stream, "DENY " + reason); } catch { /* 忽略 */ }
                try { client.Close(); } catch { /* 忽略 */ }
            }

            private static async Task RejectAsync(TcpClient client, string reason)
            {
                try
                {
                    using NetworkStream stream = client.GetStream();
                    WriteLine(stream, "DENY " + reason);
                }
                catch
                {
                    // 忽略
                }
                finally
                {
                    try { client.Close(); } catch { /* 忽略 */ }
                }
                await Task.CompletedTask.ConfigureAwait(false);
            }

            private async Task PipeAsync(TcpClient client, NetworkStream inbound)
            {
                using TcpClient target = new();
                try
                {
                    await target.ConnectAsync(IPAddress.Loopback, _targetPort).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log?.Invoke("[gate] 连不上本机 MC 端口：" + ex.Message);
                    try { client.Close(); } catch { /* 忽略 */ }
                    return;
                }
                using NetworkStream outbound = target.GetStream();
                Task up = PumpAsync(inbound, outbound);
                Task down = PumpAsync(outbound, inbound);
                await Task.WhenAny(up, down).ConfigureAwait(false);
                try { client.Close(); } catch { /* 忽略 */ }
                try { target.Close(); } catch { /* 忽略 */ }
            }

            private static async Task PumpAsync(Stream from, Stream to)
            {
                byte[] buffer = new byte[16 * 1024];
                try
                {
                    while (true)
                    {
                        int read = await from.ReadAsync(buffer).ConfigureAwait(false);
                        if (read <= 0)
                            return;
                        await to.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // 任一侧断开即结束
                }
            }

            private static string ReadLine(Stream stream)
            {
                StringBuilder sb = new();
                while (sb.Length <= MaxLineLength)
                {
                    int b = stream.ReadByte();
                    if (b < 0)
                        throw new IOException("连接已关闭");
                    if (b == (byte)'\n')
                        return sb.ToString().TrimEnd('\r');
                    sb.Append((char)b);
                }
                throw new InvalidDataException("握手行过长");
            }

            private static void WriteLine(Stream stream, string text)
            {
                stream.Write(Encoding.UTF8.GetBytes(text + "\n"));
                stream.Flush();
            }

            public void Dispose()
            {
                _cts.Cancel();
                try { _listener.Stop(); } catch { /* 忽略 */ }
                lock (_lock)
                {
                    foreach (Session session in _sessions)
                    {
                        try { session.Client?.Close(); } catch { /* 忽略 */ }
                    }
                    _sessions.Clear();
                    _tickets.Clear();
                }
            }
        }

        /// <summary>加入侧隧道：本机监听 → overlay gate 端口（握手成功后原样转发）。</summary>
        public sealed class Client : IDisposable
        {
            private readonly int _localPort;
            private readonly string _hostIp;
            private readonly int _gatePort;
            private readonly string _secret;
            private readonly string _ticket;
            private readonly Action<string>? _log;
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cts = new();

            public Client(int localPort, string hostOverlayIp, int gatePort, string networkSecret, string ticket,
                Action<string>? log = null)
            {
                _localPort = localPort;
                _hostIp = hostOverlayIp;
                _gatePort = gatePort;
                _secret = networkSecret;
                _ticket = ticket;
                _log = log;
                _listener = new TcpListener(IPAddress.Loopback, localPort);
            }

            public void Start()
            {
                _listener.Start();
                _log?.Invoke($"[gate] 隧道就绪：127.0.0.1:{_localPort} → {_hostIp}:{_gatePort}");
                _ = Task.Run(AcceptLoopAsync);
            }

            private async Task AcceptLoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient local;
                    try
                    {
                        local = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (_cts.IsCancellationRequested)
                            break;
                        _log?.Invoke("[gate] 本地接受连接失败：" + ex.Message);
                        continue;
                    }
                    _ = Task.Run(() => HandleAsync(local));
                }
            }

            private async Task HandleAsync(TcpClient local)
            {
                TcpClient remote = new();
                try
                {
                    local.NoDelay = true;
                    await remote.ConnectAsync(_hostIp, _gatePort).ConfigureAwait(false);
                    remote.NoDelay = true;
                    using NetworkStream inbound = remote.GetStream();
                    inbound.ReadTimeout = HandshakeTimeoutMs;
                    inbound.WriteTimeout = HandshakeTimeoutMs;

                    string challenge = ReadLine(inbound);
                    if (!challenge.StartsWith("PCLGATE1 ", StringComparison.Ordinal))
                        throw new InvalidDataException("网关握手协议不匹配");
                    string nonce = challenge[9..].Trim();
                    WriteLine(inbound, _ticket + " " + Proof(_secret, nonce, _ticket));
                    string reply = ReadLine(inbound);
                    if (!reply.Equals("OK", StringComparison.Ordinal))
                        throw new InvalidOperationException("网关拒绝：" + reply);

                    inbound.ReadTimeout = Timeout.Infinite;
                    inbound.WriteTimeout = Timeout.Infinite;
                    using NetworkStream outbound = local.GetStream();
                    Task up = PumpAsync(outbound, inbound);
                    Task down = PumpAsync(inbound, outbound);
                    await Task.WhenAny(up, down).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log?.Invoke("[gate] 隧道失败：" + ex.Message);
                }
                finally
                {
                    try { local.Close(); } catch { /* 忽略 */ }
                    try { remote.Close(); } catch { /* 忽略 */ }
                }
            }

            private static async Task PumpAsync(Stream from, Stream to)
            {
                byte[] buffer = new byte[16 * 1024];
                try
                {
                    while (true)
                    {
                        int read = await from.ReadAsync(buffer).ConfigureAwait(false);
                        if (read <= 0)
                            return;
                        await to.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // 任一侧断开即结束
                }
            }

            private static string ReadLine(Stream stream)
            {
                StringBuilder sb = new();
                while (sb.Length <= MaxLineLength)
                {
                    int b = stream.ReadByte();
                    if (b < 0)
                        throw new IOException("连接已关闭");
                    if (b == (byte)'\n')
                        return sb.ToString().TrimEnd('\r');
                    sb.Append((char)b);
                }
                throw new InvalidDataException("握手行过长");
            }

            private static void WriteLine(Stream stream, string text)
            {
                stream.Write(Encoding.UTF8.GetBytes(text + "\n"));
                stream.Flush();
            }

            public void Dispose()
            {
                _cts.Cancel();
                try { _listener.Stop(); } catch { /* 忽略 */ }
            }
        }
    }
}
