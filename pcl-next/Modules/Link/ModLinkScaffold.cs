using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeloongCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCL
{

    /// <summary>
    /// PCL2-CE 的 Scaffolding 数据交换协议（压缩包内部的私有协议，格式按对方实现对齐）。
    /// <para>
    /// 帧格式：
    /// 请求 = [1 字节类型长度][类型 ASCII][4 字节大端正文长度][正文]；
    /// 应答 = [1 字节状态][4 字节大端正文长度][正文]，状态 0 为成功、32 为参数错误。
    /// </para>
    /// <para>
    /// 房主跑 <see cref="Server"/>（监听 127.0.0.1，由 EasyTier 端口白名单把对方流量转进来），
    /// 加入者跑 <see cref="Client"/>，靠 <c>c:server_port</c> 拿到房主真实的 MC 端口。
    /// 这里只对齐字符串与字节格式，不复制对方源码，也不接触对方账号体系。
    /// </para>
    /// </summary>
    public static class ModLinkScaffold
    {

        #region 常量

        /// <summary>房主 EasyTier 主机名前缀（对方靠它识别房主，后缀是 Scaffolding 端口）。</summary>
        public const string HostnamePrefix = ModLinkInterop.ScaffoldHostnamePrefix;

        public const string TypePing = "c:ping";
        public const string TypeProtocols = "c:protocols";
        public const string TypeServerPort = "c:server_port";
        public const string TypePlayerPing = "c:player_ping";
        public const string TypePlayerList = "c:player_profiles_list";

        private const byte StatusOk = 0;
        private const byte StatusBadRequest = 32;
        private const int MaxTypeLength = 128;
        private const int MaxBodyLength = 65536;

        private static readonly string[] SupportedProtocols =
        {
            TypePing, TypeProtocols, TypeServerPort, TypePlayerPing, TypePlayerList
        };

        #endregion

        #region 帧读写

        /// <summary>写一帧请求。</summary>
        private static void WriteRequest(Stream stream, string type, byte[] body)
        {
            body ??= Array.Empty<byte>();
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            if (typeBytes.Length == 0 || typeBytes.Length > byte.MaxValue)
                throw new ArgumentException("请求类型长度不合法");
            byte[] header = new byte[1 + typeBytes.Length + 4];
            header[0] = (byte)typeBytes.Length;
            Buffer.BlockCopy(typeBytes, 0, header, 1, typeBytes.Length);
            WriteBigEndian(header, 1 + typeBytes.Length, body.Length);
            stream.Write(header, 0, header.Length);
            if (body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        /// <summary>读一帧请求（服务端用）。返回 null 表示连接已正常结束。</summary>
        private static (string Type, byte[] Body)? ReadRequest(Stream stream)
        {
            byte[] typeLengthBuffer = new byte[1];
            if (!ReadExact(stream, typeLengthBuffer))
                return null;
            int typeLength = typeLengthBuffer[0];
            if (typeLength == 0 || typeLength > MaxTypeLength)
                throw new InvalidDataException("帧类型长度不合法：" + typeLength);
            byte[] typeBuffer = new byte[typeLength];
            if (!ReadExact(stream, typeBuffer))
                return null;
            byte[] lengthBuffer = new byte[4];
            if (!ReadExact(stream, lengthBuffer))
                return null;
            int bodyLength = ReadBigEndian(lengthBuffer, 0);
            if (bodyLength < 0 || bodyLength > MaxBodyLength)
                throw new InvalidDataException("帧正文长度不合法：" + bodyLength);
            byte[] body = new byte[bodyLength];
            if (bodyLength > 0 && !ReadExact(stream, body))
                return null;
            return (Encoding.ASCII.GetString(typeBuffer), body);
        }

        /// <summary>写一帧应答。</summary>
        private static void WriteResponse(Stream stream, byte status, byte[] body)
        {
            body ??= Array.Empty<byte>();
            byte[] header = new byte[5];
            header[0] = status;
            WriteBigEndian(header, 1, body.Length);
            stream.Write(header, 0, header.Length);
            if (body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        /// <summary>读一帧应答（返回 (状态, 正文)）。</summary>
        private static (byte Status, byte[] Body) ReadResponse(Stream stream)
        {
            byte[] header = new byte[5];
            if (!ReadExact(stream, header))
                throw new IOException("连接已被关闭");
            int bodyLength = ReadBigEndian(header, 1);
            if (bodyLength < 0 || bodyLength > MaxBodyLength)
                throw new InvalidDataException("应答正文长度不合法：" + bodyLength);
            byte[] body = new byte[bodyLength];
            if (bodyLength > 0 && !ReadExact(stream, body))
                throw new IOException("连接已被关闭");
            return (header[0], body);
        }

        private static bool ReadExact(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                    return false;
                offset += read;
            }
            return true;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static int ReadBigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
        }

        #endregion

        #region 玩家档案

        /// <summary>构造一份玩家档案（字段名与对方一致：name / machine_id / vendor / kind）。</summary>
        public static JObject BuildProfile(string name, string machineId, string vendor, string kind)
        {
            JObject profile = new JObject
            {
                ["name"] = name ?? "",
                ["machine_id"] = machineId ?? "",
                ["vendor"] = vendor ?? ""
            };
            if (!string.IsNullOrEmpty(kind))
                profile["kind"] = kind;
            return profile;
        }

        /// <summary>本启动器在联机大厅里的身份：名字 + 识别码 + 署名。</summary>
        public static string DefaultVendor => "pcl-next " + ModBase.VersionBaseName + ", EasyTier " + ModLinkEasyTier.Version;

        private static string GetMachineId()
        {
            try
            {
                string id = ModBase.Identify;
                return string.IsNullOrWhiteSpace(id) ? "pcl-next-unknown" : id;
            }
            catch
            {
                return "pcl-next-unknown";
            }
        }

        #endregion

        #region 房主侧服务

        /// <summary>
        /// 房主侧的 Scaffolding 服务：监听回环地址，回应对方的端口查询与心跳。
        /// </summary>
        public sealed class Server : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly Dictionary<string, JObject> _players = new Dictionary<string, JObject>();
            private readonly object _playersLock = new object();
            private readonly ushort _mcPort;
            private Task _acceptTask;
            private bool _disposed;

            /// <summary>本机玩家列表变化时的通知（参数是玩家名）。</summary>
            public event Action<string> PlayerJoined;

            public Server(int scaffoldPort, ushort mcPort, string hostName)
            {
                _mcPort = mcPort;
                string machineId = GetMachineId();
                lock (_playersLock)
                {
                    _players[machineId] = BuildProfile(hostName, machineId, DefaultVendor, "HOST");
                }
                _listener = new TcpListener(IPAddress.Loopback, scaffoldPort);
            }

            /// <summary>启动监听；端口被占用等异常会抛出。</summary>
            public void Start()
            {
                _listener.Start();
                _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
                Logger.Info($"联机：Scaffolding 服务已监听 127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            }

            private async Task AcceptLoopAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested)
                            break;
                        Logger.Warn(ex, "联机：Scaffolding 接收连接失败");
                        continue;
                    }
                    _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
                }
            }

            private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
            {
                using (client)
                {
                    client.NoDelay = true;
                    NetworkStream stream = client.GetStream();
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            (string Type, byte[] Body)? frame = ReadRequest(stream);
                            if (frame is null)
                                break;
                            byte status = StatusOk;
                            byte[] body = Array.Empty<byte>();
                            switch (frame.Value.Type)
                            {
                                case TypePing:
                                    body = frame.Value.Body;
                                    break;
                                case TypeProtocols:
                                    body = Encoding.ASCII.GetBytes(string.Join('\0', SupportedProtocols));
                                    break;
                                case TypeServerPort:
                                    if (_mcPort == 0)
                                    {
                                        status = StatusBadRequest;
                                    }
                                    else
                                    {
                                        body = new byte[2];
                                        body[0] = (byte)(_mcPort >> 8);
                                        body[1] = (byte)_mcPort;
                                    }
                                    break;
                                case TypePlayerPing:
                                    status = RegisterPlayer(frame.Value.Body);
                                    break;
                                case TypePlayerList:
                                    body = Encoding.UTF8.GetBytes(SnapshotPlayers().ToString(Formatting.None));
                                    break;
                                default:
                                    status = StatusBadRequest;
                                    break;
                            }
                            WriteResponse(stream, status, body);
                        }
                    }
                    catch (IOException)
                    {
                        // 对端关闭，正常结束
                    }
                    catch (InvalidDataException ex)
                    {
                        Logger.Warn(ex, "联机：Scaffolding 收到畸形帧，已断开该连接");
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Logger.Warn(ex, "联机：Scaffolding 处理连接出错");
                    }
                }
            }

            private byte RegisterPlayer(byte[] body)
            {
                JObject profile;
                try
                {
                    profile = JObject.Parse(Encoding.UTF8.GetString(body));
                }
                catch (JsonException ex)
                {
                    Logger.Warn(ex, "联机：Scaffolding 心跳 JSON 解析失败");
                    return StatusBadRequest;
                }
                string machineId = profile["machine_id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(machineId))
                    return StatusBadRequest;
                profile["kind"] = "GUEST";
                bool isNew;
                string name = profile["name"]?.ToString() ?? "";
                lock (_playersLock)
                {
                    isNew = !_players.ContainsKey(machineId);
                    _players[machineId] = profile;
                }
                if (isNew)
                {
                    Logger.Info("联机：Scaffolding 有玩家加入（" + (string.IsNullOrEmpty(name) ? machineId : name) + "）");
                    try { PlayerJoined?.Invoke(name); } catch { /* 忽略回调异常 */ }
                }
                return StatusOk;
            }

            private JArray SnapshotPlayers()
            {
                JArray array = new JArray();
                lock (_playersLock)
                {
                    foreach (KeyValuePair<string, JObject> pair in _players)
                        array.Add(pair.Value.DeepClone());
                }
                return array;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                try { _cts.Cancel(); } catch { /* 忽略 */ }
                try { _listener.Stop(); } catch { /* 忽略 */ }
                _cts.Dispose();
            }
        }

        #endregion

        #region 加入侧客户端

        /// <summary>
        /// 加入侧的 Scaffolding 客户端：握手、请求房主 MC 端口、维持心跳。
        /// </summary>
        public sealed class Client : IDisposable
        {
            private readonly TcpClient _tcp = new TcpClient();
            private readonly object _sync = new object();
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private NetworkStream _stream;
            private Task _heartbeatTask;
            private JObject _profile;
            private bool _disposed;

            /// <summary>最近一次拿到的玩家列表（可为空）。</summary>
            public JArray LastPlayers { get; private set; }

            /// <summary>连接并完成首次握手（对方要求先发一次 <c>c:player_ping</c>）。</summary>
            public void Connect(string host, int port, string playerName)
            {
                _profile = BuildProfile(playerName, GetMachineId(), DefaultVendor, null);
                _tcp.NoDelay = true;
                _tcp.Connect(host, port);
                _stream = _tcp.GetStream();
                SendRequest(TypePlayerPing, Encoding.UTF8.GetBytes(_profile.ToString(Formatting.None)));
                _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
                Logger.Info($"联机：Scaffolding 已连上房主服务 {host}:{port}");
            }

            private async Task HeartbeatLoopAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(5000, ct).ConfigureAwait(false);
                        SendRequest(TypePlayerPing, Encoding.UTF8.GetBytes(_profile.ToString(Formatting.None)));
                        byte[] body = SendRequest(TypePlayerList, Array.Empty<byte>());
                        try
                        {
                            LastPlayers = JArray.Parse(Encoding.UTF8.GetString(body));
                        }
                        catch (JsonException)
                        {
                            // 列表拿不到不影响联机
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "联机：Scaffolding 心跳失败，房主可能已关闭");
                        break;
                    }
                }
            }

            /// <summary>向房主索取真实的 Minecraft 服务器端口。</summary>
            public ushort GetServerPort()
            {
                byte[] body = SendRequest(TypeServerPort, Array.Empty<byte>());
                if (body.Length != 2)
                    throw new InvalidDataException("房主返回的端口长度不正确");
                return (ushort)((body[0] << 8) | body[1]);
            }

            private byte[] SendRequest(string type, byte[] body)
            {
                lock (_sync)
                {
                    if (_stream is null)
                        throw new IOException("Scaffolding 连接尚未建立");
                    WriteRequest(_stream, type, body);
                    (byte status, byte[] responseBody) = ReadResponse(_stream);
                    if (status != StatusOk)
                    {
                        throw new IOException("房主拒绝了请求（" + type + "），状态码 " + status);
                    }
                    return responseBody;
                }
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                try { _cts.Cancel(); } catch { /* 忽略 */ }
                try { _stream?.Dispose(); } catch { /* 忽略 */ }
                try { _tcp.Close(); } catch { /* 忽略 */ }
                _cts.Dispose();
            }
        }

        #endregion

    }
}
