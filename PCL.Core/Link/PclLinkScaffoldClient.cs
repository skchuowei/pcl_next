using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PCL.Core.Link
{
    /// <summary>
    /// 大厅协议的最小客户端（与 GUI 的 ModLinkScaffold 同一帧格式）。
    /// <para>
    /// 帧：<c>1 字节类型长度 + ASCII 类型 + 4 字节大端正文长度 + 正文</c>；
    /// 应答：<c>1 字节状态 + 4 字节大端长度 + 正文</c>。状态 0 为成功、32 为请求不合法、255 为失败（正文是说明）。
    /// </para>
    /// <para>
    /// 只做 CLI 需要的部分：注册（心跳）、取成员、取 MC 端口、取网关凭据。服务端仍在 GUI 工程里。
    /// </para>
    /// </summary>
    public sealed class PclLinkScaffoldClient : IDisposable
    {
        public const string TypePing = "c:ping";
        public const string TypeServerPort = "c:server_port";
        public const string TypePlayerPing = "c:player_ping";
        public const string TypePlayerList = "c:player_profiles_list";
        public const string TypeGate = "c:gate";
        public const string KickedMarker = "KICKED";

        public const byte StatusOk = 0;
        public const byte StatusError = 255;

        private const int MaxBodyLength = 65536;
        private const int TimeoutMs = 5000;

        private readonly string _host;
        private readonly int _port;
        private readonly string _machineId;
        private readonly string _name;
        private readonly string _vendor;
        private TcpClient? _client;
        private NetworkStream? _stream;

        public PclLinkScaffoldClient(string host, int port, string machineId, string name, string vendor)
        {
            _host = host;
            _port = port;
            _machineId = machineId;
            _name = name;
            _vendor = vendor;
        }

        /// <summary>一条成员档案。</summary>
        public sealed class Member
        {
            public string Name { get; init; } = "";
            public string MachineId { get; init; } = "";
            public string Vendor { get; init; } = "";
            public string Kind { get; init; } = "";
            public int PingMs { get; init; } = -1;
            public bool IsHost => Kind.Equals("HOST", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>房主下发的网关凭据。</summary>
        public sealed class GateInfo
        {
            public int Port { get; init; }
            public string Ticket { get; init; } = "";
            public DateTime ExpiresAt { get; init; }
        }

        public void Connect()
        {
            _client = new TcpClient { NoDelay = true };
            if (!_client.ConnectAsync(_host, _port).Wait(TimeoutMs))
                throw new IOException($"连接大厅协议端口 {_host}:{_port} 超时");
            _stream = _client.GetStream();
            _stream.ReadTimeout = TimeoutMs;
            _stream.WriteTimeout = TimeoutMs;
        }

        /// <summary>注册为成员（每次调用即一次心跳）。</summary>
        public void Register()
        {
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                name = _name,
                machine_id = _machineId,
                vendor = _vendor,
                kind = "GUEST"
            });
            (byte status, byte[] reply) = Request(TypePlayerPing, body);
            if (status == StatusError)
            {
                string text = Encoding.UTF8.GetString(reply);
                throw new InvalidOperationException(text.Contains(KickedMarker, StringComparison.Ordinal)
                    ? "你已被房主移出该房间"
                    : "大厅协议拒绝了注册：" + text);
            }
            if (status != StatusOk)
                throw new InvalidOperationException("大厅协议拒绝了注册（状态 " + status + "）");
        }

        public bool Ping()
        {
            (byte status, _) = Request(TypePing, Array.Empty<byte>());
            return status == StatusOk;
        }

        /// <summary>房主的 MC 端口；拿不到时返回 0。</summary>
        public int GetServerPort()
        {
            (byte status, byte[] body) = Request(TypeServerPort, Array.Empty<byte>());
            if (status != StatusOk || body.Length < 2)
                return 0;
            return (body[0] << 8) | body[1];
        }

        /// <summary>请求网关信息；房主没启用网关时返回 null。</summary>
        public GateInfo? GetGate()
        {
            byte[] body = Encoding.UTF8.GetBytes(_machineId);
            (byte status, byte[] reply) = Request(TypeGate, body);
            if (status != StatusOk)
                return null;
            using JsonDocument doc = JsonDocument.Parse(reply);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            int port = root.TryGetProperty("port", out JsonElement p) && p.TryGetInt32(out int pv) ? pv : 0;
            string ticket = root.TryGetProperty("ticket", out JsonElement t) ? (t.GetString() ?? "") : "";
            DateTime expires = root.TryGetProperty("expiresAt", out JsonElement e) &&
                               DateTime.TryParse(e.GetString(), out DateTime ex)
                ? ex
                : DateTime.Now.AddHours(12);
            if (port is < 1 or > 65535 || string.IsNullOrEmpty(ticket))
                return null;
            return new GateInfo { Port = port, Ticket = ticket, ExpiresAt = expires };
        }

        public List<Member> GetMembers()
        {
            List<Member> list = new();
            (byte status, byte[] body) = Request(TypePlayerList, Array.Empty<byte>());
            if (status != StatusOk || body.Length == 0)
                return list;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return list;
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    list.Add(new Member
                    {
                        Name = item.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "",
                        MachineId = item.TryGetProperty("machine_id", out JsonElement m) ? (m.GetString() ?? "") : "",
                        Vendor = item.TryGetProperty("vendor", out JsonElement v) ? (v.GetString() ?? "") : "",
                        Kind = item.TryGetProperty("kind", out JsonElement k) ? (k.GetString() ?? "") : "",
                        PingMs = item.TryGetProperty("ping", out JsonElement g) && g.TryGetInt32(out int ping) ? ping : -1
                    });
                }
            }
            catch (JsonException)
            {
                // 列表坏了不影响联机
            }
            return list;
        }

        private (byte Status, byte[] Body) Request(string type, byte[] body)
        {
            if (_stream is null)
                throw new InvalidOperationException("大厅协议连接尚未建立");
            WriteRequest(_stream, type, body);
            return ReadResponse(_stream);
        }

        #region 帧读写（与 GUI 完全一致）

        private static void WriteRequest(Stream stream, string type, byte[] body)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            byte[] header = new byte[1 + typeBytes.Length + 4];
            header[0] = (byte)typeBytes.Length;
            Buffer.BlockCopy(typeBytes, 0, header, 1, typeBytes.Length);
            WriteBigEndian(header, 1 + typeBytes.Length, body.Length);
            stream.Write(header, 0, header.Length);
            if (body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static (byte Status, byte[] Body) ReadResponse(Stream stream)
        {
            byte[] header = new byte[5];
            if (!ReadExact(stream, header))
                throw new IOException("大厅协议连接已被关闭");
            int length = ReadBigEndian(header, 1);
            if (length < 0 || length > MaxBodyLength)
                throw new InvalidDataException("应答正文长度不合法：" + length);
            byte[] body = new byte[length];
            if (length > 0 && !ReadExact(stream, body))
                throw new IOException("大厅协议连接已被关闭");
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
            => (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];

        #endregion

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { /* 忽略 */ }
            try { _client?.Dispose(); } catch { /* 忽略 */ }
            _stream = null;
            _client = null;
        }
    }
}
