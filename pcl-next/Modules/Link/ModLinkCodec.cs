using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PCL
{

    /// <summary>
    /// 联机邀请码编解码（pcl-next 自研格式，不兼容上游旧格式）。
    /// <para>当前格式：<c>网名5位-密钥5位-前缀8位</c>，例如 <c>K7M3Q-XB4T9-3fa8c1d0</c>。</para>
    /// <para>
    /// 前两段只用无歧义字符集（去掉 0/O、1/I/L），方便口头传播；第三段是 8 位十六进制，
    /// 每次建房由密码学安全随机生成，并与第二段拼成 EasyTier 的 network-secret ——
    /// 密钥熵因此从 5 位（约 24.8 bit）提升到约 56.8 bit，接近 PCL2-CE 的 U/ 码。
    /// </para>
    /// <para>
    /// 端口不再写进码里：加入者通过房主在网内发布的 Scaffolding 服务取端口。
    /// 旧格式 <c>网名9位-密钥5位-端口4位十六进制</c> 仍然可以解析，历史邀请码不会失效。
    /// </para>
    /// </summary>
    public static class ModLinkCodec
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
            /// <summary>邀请码里的网名（不含前缀）。</summary>
            public string Name { get; init; }
            /// <summary>邀请码里的密钥段。</summary>
            public string Secret { get; init; }
            /// <summary>新格式的 8 位十六进制前缀；旧格式为空串。</summary>
            public string Prefix { get; init; } = "";
            /// <summary>旧格式里的 MC 端口；新格式为 0（端口改为网内发布）。</summary>
            public ushort Port { get; init; }
            /// <summary>是否为旧格式（9 位网名 + 4 位端口）。</summary>
            public bool IsLegacy { get; init; }

            /// <summary>该房间是否要求双方都开启 EasyTier 安全模式（码尾带 S）。</summary>
            public bool SecureMode { get; init; }

            /// <summary>参与 EasyTier 的完整网络名（带前缀）。</summary>
            public string FullNetworkName => NetworkPrefix + Name;

            /// <summary>
            /// 参与 EasyTier 的完整密钥。新格式把前缀并进密钥，旧格式只有 5 位密钥。
            /// </summary>
            public string FullSecret => IsLegacy ? Secret : Secret + Prefix;
        }

        /// <summary>生成随机的房间身份（网名 5 位 + 密钥 5 位 + 前缀 8 位十六进制）。</summary>
        public static (string Name, string Secret, string Prefix) NewIdentity()
        {
            return (RandomText(NameLength), RandomText(SecretLength), RandomHex(PrefixLength));
        }

        /// <summary>拼装邀请码（新格式）。开启了安全模式时追加 <c>-S</c> 尾段，加入者据此决定是否开 secure-mode。</summary>
        public static string Build(string name, string secret, string prefix, bool secureMode = false)
        {
            string code = $"{name}-{secret}-{prefix}";
            return secureMode ? code + "-" + SecureModeMark : code;
        }

        /// <summary>解析邀请码，新格式（含安全模式尾段）与旧格式都支持；格式非法时抛出 <see cref="ArgumentException"/>。</summary>
        public static CodeInfo Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("邀请码不能为空");
            string text = code.Trim().Replace(" ", "").Replace("—", "-").Replace("－", "-").ToUpperInvariant()
                .Replace('_', '-');
            string[] parts = text.Split('-');
            if (parts.Length != 3 && parts.Length != 4)
                throw new ArgumentException("邀请码格式不正确，应为「网名-密钥-前缀」三段（安全模式房间多一段 S）");

            string name = parts[0];
            string secret = parts[1];
            string third = parts[2];
            bool secureMode = parts.Length == 4 && parts[3] == SecureModeMark;
            if (parts.Length == 4 && !secureMode)
                throw new ArgumentException("邀请码尾段只接受 S（表示该房间开启了安全模式）");

            // 新格式：5-5-8（第三段为十六进制）
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

            // 旧格式：9-5-4（第三段为十六进制端口）
            if (name.Length == LegacyNameLength && IsClean(name) &&
                secret.Length == SecretLength && IsClean(secret) &&
                third.Length == LegacyPortLength && IsHex(third) &&
                int.TryParse(third, NumberStyles.HexNumber, null, out int port) && port > 0 && port <= 65535)
            {
                return new CodeInfo
                {
                    Name = name,
                    Secret = secret,
                    Prefix = "",
                    Port = (ushort)port,
                    IsLegacy = true
                };
            }

            throw new ArgumentException(
                $"邀请码格式不正确：应为「网名 {NameLength} 位-密钥 {SecretLength} 位-前缀 {PrefixLength} 位」，安全模式房间末尾多一个 -S");
        }

        /// <summary>只取邀请码本身（去掉可能带上的中文说明或网址）。</summary>
        public static string ExtractCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            string text = raw.Trim();
            // 支持整段分享文案里夹着邀请码
            foreach (string token in text.Split(new[] { ' ', '\n', '\r', '\t', '，', ',', '。' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = token.Trim();
                try
                {
                    Parse(candidate);
                    return candidate;
                }
                catch
                {
                    // 不是邀请码，继续找
                }
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
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!ok)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 生成随机码段。必须用密码学安全随机：邀请码里的密钥就是 EasyTier 的 network-secret，
        /// <c>System.Random</c> 是确定性伪随机，状态可由少量输出推回，等于把房间密钥变成可预测的。
        /// </summary>
        private static string RandomText(int length)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            return builder.ToString();
        }

        /// <summary>生成 n 位小写十六进制（等价于 UUID 前 n 位，但每次都重新取随机值）。</summary>
        private static string RandomHex(int length)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append("0123456789abcdef"[RandomNumberGenerator.GetInt32(16)]);
            return builder.ToString();
        }

    }
}
