using System;
using System.Text;

namespace PCL
{

    /// <summary>
    /// 联机邀请码编解码（pcl-next 自研格式，不兼容上游旧格式）。
    /// <para>格式：<c>网名9位-密钥5位-端口4位十六进制</c>，例如 <c>K7M3Q9XZB-3HTRA-63C5</c>。</para>
    /// <para>网名与密钥只用无歧义字符集（去掉了 0/O、1/I/L 等），方便口头传播。</para>
    /// </summary>
    public static class ModLinkCodec
    {

        /// <summary>EasyTier 网络名前缀，用于与其它启动器的联机网络隔离。</summary>
        public const string NetworkPrefix = "pclnext-";

        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        private static readonly Random Rng = new Random();

        /// <summary>邀请码解析结果。</summary>
        public sealed class CodeInfo
        {
            /// <summary>邀请码里的网名（不含前缀）。</summary>
            public string Name { get; init; }
            /// <summary>邀请码里的密钥。</summary>
            public string Secret { get; init; }
            /// <summary>房主的 MC 服务器端口。</summary>
            public ushort Port { get; init; }
            /// <summary>参与 EasyTier 的完整网络名（带前缀）。</summary>
            public string FullNetworkName => NetworkPrefix + Name;
        }

        /// <summary>生成随机的房间身份（网名 9 位 + 密钥 5 位）。</summary>
        public static (string Name, string Secret) NewIdentity()
        {
            return (RandomText(9), RandomText(5));
        }

        /// <summary>拼装邀请码。</summary>
        public static string Build(string name, string secret, ushort port)
        {
            return $"{name}-{secret}-{port:X4}";
        }

        /// <summary>解析邀请码，格式非法时抛出 <see cref="ArgumentException"/>。</summary>
        public static CodeInfo Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("邀请码不能为空");
            string text = code.Trim().Replace(" ", "").Replace("—", "-").Replace("－", "-").ToUpperInvariant();
            string[] parts = text.Split('-');
            if (parts.Length != 3)
                throw new ArgumentException("邀请码格式不正确，应为「网名-密钥-端口」三段");
            string name = parts[0];
            string secret = parts[1];
            string portText = parts[2];
            if (name.Length != 9 || !IsClean(name))
                throw new ArgumentException("邀请码第一段（网名）应为 9 位字母或数字");
            if (secret.Length != 5 || !IsClean(secret))
                throw new ArgumentException("邀请码第二段（密钥）应为 5 位字母或数字");
            if (portText.Length != 4 || !int.TryParse(portText, System.Globalization.NumberStyles.HexNumber, null, out int port) || port <= 0 || port > 65535)
                throw new ArgumentException("邀请码第三段（端口）应为 4 位十六进制数");
            return new CodeInfo { Name = name, Secret = secret, Port = (ushort)port };
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

        private static string RandomText(int length)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append(Alphabet[Rng.Next(Alphabet.Length)]);
            return builder.ToString();
        }

    }
}
