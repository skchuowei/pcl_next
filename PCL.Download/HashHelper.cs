using System.Security.Cryptography;

namespace PCL.Download;

/// <summary>
/// 哈希计算工具：按十六进制串长度自动识别 MD5/SHA1/SHA256/SHA512。
/// </summary>
public static class HashHelper
{
    public static string NameOf(int hexLength) => hexLength switch
    {
        32 => "MD5",
        40 => "SHA1",
        64 => "SHA256",
        128 => "SHA512",
        _ => "未知算法",
    };

    public static string ComputeFileHash(string path, int hexLength)
    {
        using HashAlgorithm algorithm = hexLength switch
        {
            32 => MD5.Create(),
            40 => SHA1.Create(),
            64 => SHA256.Create(),
            128 => SHA512.Create(),
            _ => throw new ArgumentException($"不支持的哈希长度：{hexLength}"),
        };
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
    }

    public static string ComputeHash(byte[] data, int hexLength)
    {
        using HashAlgorithm algorithm = hexLength switch
        {
            32 => MD5.Create(),
            40 => SHA1.Create(),
            64 => SHA256.Create(),
            128 => SHA512.Create(),
            _ => throw new ArgumentException($"不支持的哈希长度：{hexLength}"),
        };
        return Convert.ToHexString(algorithm.ComputeHash(data)).ToLowerInvariant();
    }
}
