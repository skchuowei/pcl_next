using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PCL.Core.Link
{
    /// <summary>
    /// 联机邀请码编解码（GUI 与 CLI 共用格式，纯 BCL，可在 Linux 上跑）。
    /// <para>格式：<c>网名5位-密钥5位-前缀8位</c>，例如 <c>K7M3Q-XB4T9-3fa8c1d0</c>；</para>
    /// <para>
    /// 前两段只用无歧义字符集（去掉 0/O、1/I/L），方便口头传播；第三段是 8 位十六进制，
    /// 每次建房由密码学安全随机生成，并与第二段拼成 EasyTier 的 network-secret。
    /// </para>
    /// <para>
    /// 与 GUI 侧 <c>ModLinkCodec</c> 保持同一格式（旧格式 <c>网名9位-密钥5位-端口4位十六进制</c> 仍可解析），
    /// 因此 CLI 建的房可以用 GUI 加入，反之亦然。CLI 只做测试链路，不解析大厅协议。
    /// </para>
    /// </summary>
    public static class PclLinkCode
    {
        /// <summary>EasyTier 网络名前缀，用于与其它启动器的联机网络隔离。</summary>
        public const string NetworkPrefix = "pclnext-";

        /// <summary>新格式各段长度。</summary>
        public const int NameLength = 5;
        public const int SecretLength = 5;
        public const int PrefixLength = 8;

        /// <summary>旧格式各段长度（仅解析，不再生成）。</summary>
        public const int LegacyNameLength = 9;
        public const int LegacyPortLength = 4;

        /// <summary>安全模式标记段：只有开启了 secure-mode 的房间才会在码尾追加这一段。</summary>
        public const string SecureModeMark = "S";

        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        /// <summary>邀请码解析结果。</summary>
        public sealed class CodeInfo
        {
            public string Name { get; init; } = "";
            public string Secret { get; init; } = "";
            public string Prefix { get; init; } = "";
            public ushort Port { get; init; }
            public bool IsLegacy { get; init; }
            public bool SecureMode { get; init; }

            /// <summary>参与 EasyTier 的完整网络名（带前缀）。</summary>
            public string FullNetworkName => NetworkPrefix + Name;

            /// <summary>参与 EasyTier 的完整密钥。新格式把前缀并进密钥，旧格式只有 5 位密钥。</summary>
            public string FullSecret => IsLegacy ? Secret : Secret + Prefix;
        }

        /// <summary>生成随机的房间身份（网名 5 位 + 密钥 5 位 + 前缀 8 位十六进制）。</summary>
        public static (string Name, string Secret, string Prefix) NewIdentity()
            => (RandomText(NameLength), RandomText(SecretLength), RandomHex(PrefixLength));

        /// <summary>拼装邀请码（新格式）。开启安全模式时追加 <c>-S</c> 尾段。</summary>
        public static string Build(string name, string secret, string prefix, bool secureMode = false)
        {
            string code = $"{name}-{secret}-{prefix}";
            return secureMode ? code + "-" + SecureModeMark : code;
        }

        /// <summary>解析邀请码；新格式（含安全模式尾段）与旧格式都支持。</summary>
        public static CodeInfo Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("邀请码不能为空");
            string text = code.Trim().Replace(" ", "").Replace("—", "-").Replace("－", "-")
                .ToUpperInvariant().Replace('_', '-');
            string[] parts = text.Split('-');
            if (parts.Length is not (3 or 4))
                throw new ArgumentException("邀请码格式不正确，应为「网名-密钥-前缀」三段（安全模式房间多一段 S）");

            string name = parts[0];
            string secret = parts[1];
            string third = parts[2];
            bool secureMode = parts.Length == 4 && parts[3] == SecureModeMark;
            if (parts.Length == 4 && !secureMode)
                throw new ArgumentException("邀请码尾段只接受 S（表示该房间开启了安全模式）");

            if (name.Length == NameLength && IsClean(name) &&
                secret.Length == SecretLength && IsClean(secret) &&
                third.Length == PrefixLength && IsHex(third))
            {
                return new CodeInfo
                {
                    Name = name,
                    Secret = secret,
                    Prefix = third.ToLowerInvariant(),
                    Port = 0,
                    IsLegacy = false,
                    SecureMode = secureMode
                };
            }

            if (name.Length == LegacyNameLength && IsClean(name) &&
                secret.Length == SecretLength && IsClean(secret) &&
                third.Length == LegacyPortLength && IsHex(third) &&
                int.TryParse(third, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int port) &&
                port is > 0 and <= 65535)
            {
                return new CodeInfo
                {
                    Name = name,
                    Secret = secret,
                    Prefix = "",
                    Port = (ushort)port,
                    IsLegacy = true,
                    SecureMode = false
                };
            }

            throw new ArgumentException(
                $"邀请码格式不正确：应为「网名 {NameLength} 位-密钥 {SecretLength} 位-前缀 {PrefixLength} 位」，安全模式房间末尾多一个 -S");
        }

        /// <summary>尝试解析，格式不对时返回 false（命令行做校验时用，不抛异常）。</summary>
        public static bool TryParse(string code, out CodeInfo? info)
        {
            try
            {
                info = Parse(code);
                return true;
            }
            catch (ArgumentException)
            {
                info = null;
                return false;
            }
        }

        /// <summary>只取邀请码本身（去掉可能带上的中文说明或网址）。</summary>
        public static string ExtractCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            string text = raw.Trim();
            foreach (string token in text.Split(
                new[] { ' ', '\n', '\r', '\t', '，', ',', '。' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = token.Trim();
                if (TryParse(candidate, out _))
                    return candidate;
            }
            return text;
        }

        private static bool IsClean(string text)
        {
            foreach (char c in text)
            {
                if (Alphabet.IndexOf(c) < 0)
                    return false;
            }
            return true;
        }

        private static bool IsHex(string text)
        {
            foreach (char c in text)
            {
                bool ok = c is >= '0' and <= '9' || c is >= 'A' and <= 'F' || c is >= 'a' and <= 'f';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>生成随机码段（密码学安全随机，邀请码里的密钥就是 EasyTier network-secret）。</summary>
        private static string RandomText(int length)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            return builder.ToString();
        }

        private static string RandomHex(int length)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append("0123456789abcdef"[RandomNumberGenerator.GetInt32(16)]);
            return builder.ToString();
        }
    }
}
