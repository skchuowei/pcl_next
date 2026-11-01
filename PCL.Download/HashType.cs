namespace PCL.Download;

/// <summary>
/// 统一哈希算法标识（《MC启动器市场现状与开源实现调研.md》P0：统一 HashType 模型，含 sha512）。
/// 下载引擎与资源子系统共用 <see cref="HashHelper"/> 的同一实现，禁止各模块私建算法表。
/// </summary>
public enum HashType
{
    /// <summary>MD5，十六进制 32 字符。</summary>
    Md5,

    /// <summary>SHA1，十六进制 40 字符。</summary>
    Sha1,

    /// <summary>SHA256，十六进制 64 字符。</summary>
    Sha256,

    /// <summary>SHA512，十六进制 128 字符。</summary>
    Sha512,

    /// <summary>
    /// CurseForge 指纹：去空白（仅移除 0x09/0x0A/0x0D/0x20）后的 MurmurHash2（种子 1），
    /// 与 VB <c>LocalResourceFile.CurseForgeHash</c> 逐位等价。
    /// 由 <see cref="HashHelper.Compute"/> / <see cref="HashHelper.ComputeFile"/> 计算时返回 8 位小写十六进制；
    /// 调 CurseForge API 需要十进制格式，用 <see cref="HashHelper.ComputeCurseForgeFingerprint"/> 取 uint 后格式化。
    /// </summary>
    Murmur2,
}
