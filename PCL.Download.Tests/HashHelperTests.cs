namespace PCL.Download.Tests;

/// <summary>
/// HashHelper / HashType 的对拍测试。
/// MurmurHash2 参考值由独立 Python 参考实现（按 aappleby/smhasher 原始规范逐行转写，种子 1）生成，
/// 并与 VB <c>LocalResourceFile.CurseForgeHash</c> 的实现（去 0x09/0x0A/0x0D/0x20 + MurmurHash2）等价性核对：
/// VB 主循环/尾块与标准实现按 XOR 交换律逐项等价。
/// </summary>
public class HashHelperTests
{
    #region Murmur2 原始算法（种子 1）

    [Theory]
    [InlineData("", 0x5bd15e36u)]
    [InlineData("61", 0x2550b18cu)]                         // "a"，尾块 1 字节
    [InlineData("6162", 0x64e150eeu)]                       // "ab"，尾块 2 字节
    [InlineData("616263", 0x60a4fcc1u)]                     // "abc"，尾块 3 字节
    [InlineData("61626364", 0xc93f7a16u)]                   // "abcd"，整块无尾
    [InlineData("6162636465", 0xcec85d7eu)]                 // "abcde"，1 块 + 尾 1
    [InlineData("61626364656667", 0x0afa6525u)]             // "abcdefg"，1 块 + 尾 3
    [InlineData("6162636465666768", 0x4065991au)]           // "abcdefgh"，2 块无尾
    [InlineData("6162636465666768696a6b", 0x9f3df687u)]     // "abcdefghijk"，2 块 + 尾 3
    [InlineData("68656c6c6f20776f726c64", 0x83ea5deeu)]     // "hello world"
    public void Murmur2_MatchesReferenceVectors(string hex, uint expected)
    {
        Assert.Equal(expected, HashHelper.ComputeMurmur2(Convert.FromHexString(hex)));
    }

    [Fact]
    public void Murmur2_ZeroBlock_MatchesReference()
    {
        Assert.Equal(0x32e24b69u, HashHelper.ComputeMurmur2(new byte[16]));
    }

    [Fact]
    public void Murmur2_IncrementingBytes_MatchesReference()
    {
        var data = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        Assert.Equal(0xc1397b87u, HashHelper.ComputeMurmur2(data));
    }

    #endregion

    #region CurseForge 指纹（去空白 + Murmur2）

    [Theory]
    // 去空白语义：只移除 0x09/0x0A/0x0D/0x20
    [InlineData("68656c6c6f20776f726c64", 0xa85cbdedu)]     // "hello world" → 与 raw 不同（含空格）
    [InlineData("61206209630a642065", 0xcec85d7eu)]         // "a b\tc\nd e" → 等价于 "abcde"
    [InlineData("202020", 0x5bd15e36u)]                     // 全空白 → 等价于空输入
    [InlineData("090a0d20", 0x5bd15e36u)]                   // 四类空白混排 → 等价于空输入
    [InlineData("616220", 0x64e150eeu)]                     // "ab " → 等价于 "ab"
    // 非空白控制字符必须保留
    [InlineData("610b62", 0x7d029aa6u)]                     // "a\x0Bb"，VT(0x0B) 保留
    [InlineData("610c62", 0x68ce00ccu)]                     // "a\x0Cb"，FF(0x0C) 保留
    [InlineData("61620063", 0x8a07c762u)]                   // "ab\x00c"，NUL 保留
    public void CurseForgeFingerprint_StripsOnlyWhitespaceBytes(string hex, uint expected)
    {
        Assert.Equal(expected, HashHelper.ComputeCurseForgeFingerprint(Convert.FromHexString(hex)));
    }

    [Fact]
    public void CurseForgeFingerprint_LargeJarLikeInput_MatchesReference()
    {
        // bytes(range(256)) * 3 + "  \t\r\n"（773 字节），参考值 0x0835fbe7
        var data = new byte[256 * 3 + 5];
        for (int i = 0; i < 256 * 3; i++) data[i] = (byte)(i % 256);
        data[^5] = 0x20; data[^4] = 0x20; data[^3] = 0x09; data[^2] = 0x0D; data[^1] = 0x0A;
        Assert.Equal(0x0835fbe7u, HashHelper.ComputeCurseForgeFingerprint(data));
    }

    [Fact]
    public void CurseForgeFingerprint_Empty_MatchesDecimalForApi()
    {
        // CurseForge API 用十进制；空内容指纹的十进制参考值 1540447798
        Assert.Equal(1540447798u, HashHelper.ComputeCurseForgeFingerprint([]));
    }

    #endregion

    #region 统一 HashType 模型

    [Theory]
    [InlineData(HashType.Md5, "MD5")]
    [InlineData(HashType.Sha1, "SHA1")]
    [InlineData(HashType.Sha256, "SHA256")]
    [InlineData(HashType.Sha512, "SHA512")]
    [InlineData(HashType.Murmur2, "MurmurHash2")]
    public void NameOf_ReturnsDisplayName(HashType type, string expected)
        => Assert.Equal(expected, HashHelper.NameOf(type));

    [Fact]
    public void Compute_CryptoHashes_ReturnLowerHex()
    {
        var hello = "hello world"u8.ToArray();
        Assert.Equal("5eb63bbbe01eeed093cb22bb8f5acdc3", HashHelper.Compute(hello, HashType.Md5));
        Assert.Equal("2aae6c35c94fcfb415dbe95f408b9ce91ee846ed", HashHelper.Compute(hello, HashType.Sha1));
        Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", HashHelper.Compute(hello, HashType.Sha256));
        // SHA512 参考值与 BCL 对拍（校验管道与格式，不重复校验算法本身）
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(hello)).ToLowerInvariant(),
            HashHelper.Compute(hello, HashType.Sha512));
    }

    [Fact]
    public void Compute_Murmur2_ReturnsEightHexDigitsWithFingerprintSemantics()
    {
        // 0xCEC85D7E 的 8 位小写十六进制；输入含空白，走 CurseForge 指纹语义（去空白）
        Assert.Equal("cec85d7e", HashHelper.Compute(Convert.FromHexString("61206209630a642065"), HashType.Murmur2));
    }

    [Fact]
    public void ComputeFile_AllTypes_MatchInMemoryCompute()
    {
        var data = Enumerable.Range(0, 1024).Select(i => (byte)(i * 7)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), "pcl-hash-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, data);
            foreach (var type in new[] { HashType.Md5, HashType.Sha1, HashType.Sha256, HashType.Sha512, HashType.Murmur2 })
            {
                Assert.Equal(HashHelper.Compute(data, type), HashHelper.ComputeFile(path, type));
            }
            Assert.Equal(HashHelper.ComputeCurseForgeFingerprint(data), HashHelper.ComputeCurseForgeFingerprintFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion
}
