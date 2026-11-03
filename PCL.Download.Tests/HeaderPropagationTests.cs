using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace PCL.Download.Tests;

/// <summary>
/// 校验引擎在规范化下载请求时是否保留 <see cref="DownloadFileRequest.HeaderProvider"/>。
/// 回归背景：NetInterop.vb 通过 HeaderProvider 注入 SecretHeadersSign 生成的
/// User-Agent / pcl2 Referer / CurseForge Key；引擎原先重建请求对象时漏拷该字段，
/// 导致这些自定义请求头在 C# 引擎内全部丢失，需要鉴权/防盗链的源会收到无头请求。
/// </summary>
public class HeaderPropagationTests
{
    private static byte[] RandomBytes(int size, int seed = 42)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static FileCheckRule Sha1Rule(byte[] data) =>
        new() { Hash = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant() };

    private static string TempPath(params string[] relative) =>
        Path.Combine(Path.GetTempPath(), "pcl-hdr-tests", Guid.NewGuid().ToString("N"), Path.Combine(relative));

    [Fact]
    public async Task CustomHeaders_AreSentToServer()
    {
        var data = RandomBytes(4096);
        using var server = new HeaderCollectingServer(data);
        var target = TempPath("headers.bin");
        var engine = new DownloadEngine();

        var report = await engine.DownloadAsync(
        [
            new DownloadFileRequest
            {
                Sources = [server.Url],
                LocalPath = target,
                Check = Sha1Rule(data),
                HeaderProvider = _ =>
                [
                    new KeyValuePair<string, string>("X-Api-Key", "secret-cf-key-123"),
                    new KeyValuePair<string, string>("X-Custom-Referer", "https://example.invalid/"),
                ],
            },
        ], new DownloadOptions { MaxConcurrentChunks = 4 });

        Assert.Equal(DownloadFileState.Finished, report.Files.Single().State);
        Assert.Equal(data, await File.ReadAllBytesAsync(target));
        // 自定义请求头必须真正发到对端服务器
        Assert.Contains(server.ReceivedHeaders,
            h => h.Contains("X-Api-Key: secret-cf-key-123", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(server.ReceivedHeaders,
            h => h.Contains("X-Custom-Referer: https://example.invalid/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>最小 HTTP 服务器：记录请求头原始行，返回全量 200。</summary>
    private sealed class HeaderCollectingServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[] _data;

        public ConcurrentBag<string> ReceivedHeaders { get; } = new();
        public int Port { get; }
        public string Url => $"http://127.0.0.1:{Port}/file.bin";

        public HeaderCollectingServer(byte[] data)
        {
            _data = data;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }
                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using var _ = client;
                var stream = client.GetStream();
                var sb = new StringBuilder();
                var buffer = new byte[8192];
                while (!sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var n = await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                    if (n == 0) break;
                    sb.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }
                foreach (var line in sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    ReceivedHeaders.Add(line);

                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {_data.Length}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, _cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(_data, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // 测试服务器吞掉处理错误
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
