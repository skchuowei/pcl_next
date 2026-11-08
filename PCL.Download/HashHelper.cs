using System.Security.Cryptography;

namespace PCL.Download;

/// <summary>
/// 哈希计算工具：
/// 1) 按十六进制串长度自动识别 MD5/SHA1/SHA256/SHA512（传输层校验用，签名稳定）；
/// 2) 统一 <see cref="HashType"/> 模型（资源子系统用，含 CurseForge 指纹 MurmurHash2）。
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
        using HashAlgorithm algorithm = CreateByHexLength(hexLength);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
    }

    public static string ComputeHash(byte[] data, int hexLength)
    {
        using HashAlgorithm algorithm = CreateByHexLength(hexLength);
        return Convert.ToHexString(algorithm.ComputeHash(data)).ToLowerInvariant();
    }

    // ── 统一 HashType 模型 ────────────────────────────────────────────────────

    /// <summary>算法显示名（与 VB <c>CryptographyUtils.HashMethod</c> 的叫法对齐）。</summary>
    public static string NameOf(HashType type) => type switch
    {
        HashType.Md5 => "MD5",
        HashType.Sha1 => "SHA1",
        HashType.Sha256 => "SHA256",
        HashType.Sha512 => "SHA512",
        HashType.Murmur2 => "MurmurHash2",
        _ => "未知算法",
    };

    /// <summary>
    /// 按 <see cref="HashType"/> 计算哈希，返回小写十六进制（<see cref="HashType.Murmur2"/> 为 8 位）。
    /// 注意：<see cref="HashType.Murmur2"/> 采用 CurseForge 指纹语义（含去空白）。
    /// </summary>
    public static string Compute(ReadOnlySpan<byte> data, HashType type)
    {
        if (type == HashType.Murmur2)
            return ComputeCurseForgeFingerprint(data).ToString("x8");
        using HashAlgorithm algorithm = Create(type);
        return Convert.ToHexString(algorithm.ComputeHash(data.ToArray())).ToLowerInvariant();
    }

    /// <inheritdoc cref="Compute(ReadOnlySpan{byte}, HashType)"/>
    public static string ComputeFile(string path, HashType type)
    {
        if (type == HashType.Murmur2)
            return ComputeCurseForgeFingerprintFile(path).ToString("x8");
        using HashAlgorithm algorithm = Create(type);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 原始 MurmurHash2（x86，种子 1，等价于 VB <c>LocalResourceFile.CurseForgeHash</c> 的核心循环）。
    /// 不做去空白；需要 CurseForge 指纹请用 <see cref="ComputeCurseForgeFingerprint"/>。
    /// </summary>
    public static uint ComputeMurmur2(ReadOnlySpan<byte> data)
    {
        const uint m = 0x5bd1e995;
        const int r = 24;
        uint h = 1u ^ (uint)data.Length;
        int i = 0;
        while (data.Length - i >= 4)
        {
            uint k = data[i] | ((uint)data[i + 1] << 8) | ((uint)data[i + 2] << 16) | ((uint)data[i + 3] << 24);
            k *= m;
            k ^= k >> r;
            k *= m;
            h *= m;
            h ^= k;
            i += 4;
        }
        // 尾块与 VB 实现逐项对齐（VB 的 Select Case 无 fall-through，与标准 fall-through 版按 XOR 交换律等价）
        switch (data.Length - i)
        {
            case 3:
                h ^= data[i] | ((uint)data[i + 1] << 8);
                h ^= (uint)data[i + 2] << 16;
                h *= m;
                break;
            case 2:
                h ^= data[i] | ((uint)data[i + 1] << 8);
                h *= m;
                break;
            case 1:
                h ^= data[i];
                h *= m;
                break;
        }
        h ^= h >> 13;
        h *= m;
        h ^= h >> 15;
        return h;
    }

    /// <summary>
    /// CurseForge 指纹：先移除空白字节 0x09/0x0A/0x0D/0x20（仅这四类，与 VB 一致，
    /// 其余如 0x0B/0x0C/0x00 必须保留），再计算 <see cref="ComputeMurmur2"/>。
    /// CurseForge API 使用十进制格式：<c>fp.ToString()</c>。
    /// </summary>
    public static uint ComputeCurseForgeFingerprint(ReadOnlySpan<byte> data)
    {
        Span<byte> stripped = data.Length <= 512 ? stackalloc byte[data.Length] : new byte[data.Length];
        int n = 0;
        foreach (byte b in data)
        {
            if (b is 9 or 10 or 13 or 32) continue;
            stripped[n++] = b;
        }
        return ComputeMurmur2(stripped[..n]);
    }

    /// <inheritdoc cref="ComputeCurseForgeFingerprint(ReadOnlySpan{byte})"/>
    public static uint ComputeCurseForgeFingerprintFile(string path)
    {
        // 与 VB LocalResourceFile.CurseForgeHash（FileUtils.ReadAsBytes 一次读入）行为一致；
        // 若后续扫描超大文件成瓶颈，再改增量式实现。
        return ComputeCurseForgeFingerprint(File.ReadAllBytes(path));
    }

    private static HashAlgorithm CreateByHexLength(int hexLength) => hexLength switch
    {
        32 => MD5.Create(),
        40 => SHA1.Create(),
        64 => SHA256.Create(),
        128 => SHA512.Create(),
        _ => throw new ArgumentException($"不支持的哈希长度：{hexLength}"),
    };

    private static HashAlgorithm Create(HashType type) => type switch
    {
        HashType.Md5 => MD5.Create(),
        HashType.Sha1 => SHA1.Create(),
        HashType.Sha256 => SHA256.Create(),
        HashType.Sha512 => SHA512.Create(),
        _ => throw new ArgumentException($"不支持的哈希类型：{type}"),
    };
}
