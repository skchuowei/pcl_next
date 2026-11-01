using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PCL.Download.Tests;

/// <summary>
/// 测试用假文件服务器：基于 TcpListener 手写最小 HTTP/1.1 响应，
/// 避免对 HttpListener URLACL / Kestrel 的依赖。
/// </summary>
public sealed class FakeFileServer : IDisposable
{
    private readonly TcpListener _listener;
    private CancellationTokenSource? _cts;

    /// <summary>返回的文件内容。</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>是否支持 Range 请求；false 时无视 Range 头返回全量 200。</summary>
    public bool SupportRange { get; set; } = true;

    /// <summary>Range 违规模式：0=无（正常 206/200），1=无视 Range 返回全量，2=返回错误长度的切片。</summary>
    public int RangeViolationMode { get; set; }

    /// <summary>对响应内容做变换（用于模拟损坏的下载源）。</summary>
    public Func<byte[], byte[]>? Mutate { get; set; }

    /// <summary>每 64KB 分块写入后的延迟（毫秒），模拟慢速源。</summary>
    public int DelayMsPerChunk { get; set; }

    /// <summary>是否直接拒绝连接（模拟无法访问的源）。</summary>
    public bool RefuseConnections { get; set; }

    /// <summary>接受连接但永不响应（模拟连接挂起，触发引擎连接超时）。</summary>
    public bool HangConnections { get; set; }

    public List<string> Requests { get; } = new();

    /// <summary>TCP 连接被接受的次数（含 HangConnections 模式——此时 Requests 不增长）。</summary>
    public int AcceptCount;
    public int Port { get; }
    public string Url => $"http://127.0.0.1:{Port}/file.bin";

    public FakeFileServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            if (RefuseConnections)
            {
                client.Dispose();
                continue;
            }
            if (HangConnections)
            {
                // 吞掉连接，不读不写，让客户端一直等待直到超时
                Interlocked.Increment(ref AcceptCount);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    client.Dispose();
                }, cancellationToken);
                continue;
            }
            Interlocked.Increment(ref AcceptCount);
            _ = Task.Run(() => HandleAsync(client, cancellationToken), cancellationToken);
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        return sb.ToString();
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var _ = client;
            var stream = client.GetStream();
            var raw = await ReadHeadersAsync(stream, ct).ConfigureAwait(false);
            var headerPart = raw.Split("\r\n\r\n")[0];
            var lines = headerPart.Split("\r\n");
            lock (Requests) Requests.Add(lines[0] + " | " +
                lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) ?? "");

            long rangeStart = -1;
            var rangeLine = lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
            if (rangeLine is not null)
            {
                var spec = rangeLine["Range:".Length..].Trim(); // bytes=N-
                if (spec.StartsWith("bytes=") && spec.TrimEnd('-') != "bytes=")
                {
                    var startText = spec["bytes=".Length..].Split('-')[0];
                    if (long.TryParse(startText, out var parsed)) rangeStart = parsed;
                }
            }

            var body = Data;
            if (Mutate is not null) body = Mutate(body);

            string status;
            byte[] payload;
            if (rangeStart > 0 && SupportRange && RangeViolationMode == 0)
            {
                status = "206 Partial Content";
                var startOffset = (int)rangeStart;
                payload = body[startOffset..];
                var headers =
                    $"HTTP/1.1 {status}\r\n" +
                    $"Content-Length: {payload.Length}\r\n" +
                    $"Content-Range: bytes {rangeStart}-{body.Length - 1}/{body.Length}\r\n" +
                    "Accept-Ranges: bytes\r\n" +
                    "Connection: close\r\n\r\n";
                await WriteAllAsync(stream, headers, payload, DelayMsPerChunk, ct).ConfigureAwait(false);
                return;
            }
            if (rangeStart > 0 && RangeViolationMode == 2)
            {
                // 返回错误长度的切片（比预期短）
                status = "200 OK";
                payload = body[..Math.Max(1, body.Length / 2)];
            }
            else
            {
                status = "200 OK";
                payload = body;
            }
            var plainHeaders =
                $"HTTP/1.1 {status}\r\n" +
                $"Content-Length: {payload.Length}\r\n" +
                "Accept-Ranges: bytes\r\n" +
                "Connection: close\r\n\r\n";
            await WriteAllAsync(stream, plainHeaders, payload, DelayMsPerChunk, ct).ConfigureAwait(false);
        }
        catch
        {
            // 测试服务器忽略处理错误
        }
    }

    private static async Task WriteAllAsync(NetworkStream stream, string headers, byte[] payload, int delayMsPerChunk, CancellationToken ct)
    {
        var head = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        const int chunkSize = 64 * 1024;
        for (var offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, payload.Length - offset);
            await stream.WriteAsync(payload.AsMemory(offset, count), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            if (delayMsPerChunk > 0) await Task.Delay(delayMsPerChunk, ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try { _listener.Stop(); } catch { }
    }
}
