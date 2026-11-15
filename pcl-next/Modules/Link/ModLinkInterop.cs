using System;
using System.Numerics;
using System.Text;

namespace PCL
{

    /// <summary>联机大厅类型，决定网络名前缀、密钥前缀、覆盖 IP 与加密/压缩参数。</summary>
    public enum LobbyKind
    {
        /// <summary>pcl-next 自研大厅（默认，不与其它启动器互通）。</summary>
        PclNext = 0,
        /// <summary>PCL2-CE 早期大厅（数字邀请码，网络名带 PCLCELobby 前缀）。</summary>
        PclCe = 1,
        /// <summary>陶瓦（Terracotta）大厅（25 位 Base34 邀请码）。</summary>
        Terracotta = 2,
        /// <summary>PCL2-CE 新版 Scaffolding 大厅（U/ 开头的邀请码，码内不含端口）。</summary>
        PclCeScaffold = 3
    }

    /// <summary>
    /// 一次联机要用到的大厅参数：网络全名、密钥、覆盖 IP、端口，以及为兼容其它启动器
    /// 需要额外加的加密算法、压缩算法与代理开关。
    /// </summary>
    public sealed class LobbyTarget
    {
        /// <summary>大厅类型。</summary>
        public LobbyKind Kind { get; init; } = LobbyKind.PclNext;
        /// <summary>传给 EasyTier 的完整网络名（已带前缀）。</summary>
        public string FullNetworkName { get; init; } = "";
        /// <summary>传给 EasyTier 的完整网络密钥（已带前缀，若该类型有前缀）。</summary>
        public string NetworkSecret { get; init; } = "";
        /// <summary>房主在本网络里的固定覆盖地址（房主 -i、加入者端口转发都用它）。</summary>
        public string OverlayIp { get; init; } = "";
        /// <summary>房主的 MC 服务器端口；0 表示邀请码里没带，需要用户另填。</summary>
        public ushort Port { get; init; }
        /// <summary>加密算法；空串表示不显式指定（沿用 EasyTier 默认）。</summary>
        public string EncryptionAlgorithm { get; init; } = "";
        /// <summary>压缩算法；空串表示不显式指定。</summary>
        public string Compression { get; init; } = "";
        /// <summary>是否加对方固定的代理开关（quic / kcp / smoltcp / multi-thread）。</summary>
        public bool CompatFlags { get; init; }
        /// <summary>原始邀请码（用于展示与日志）。</summary>
        public string OriginalCode { get; init; } = "";

        /// <summary>大厅类型的显示名。</summary>
        public string KindName => Kind switch
        {
            LobbyKind.PclCe => "PCL2-CE 大厅",
            LobbyKind.Terracotta => "陶瓦大厅",
            LobbyKind.PclCeScaffold => "PCL2-CE（Scaffolding）大厅",
            _ => "pcl-next 大厅"
        };
    }

    /// <summary>
    /// 启动器互通的邀请码编解码与大厅参数推导。
    /// <para>
    /// 支持四类邀请码：pcl-next 自研短码、PCL2-CE 数字码（Base32）、陶瓦码（25 位 Base34 带校验位）、
    /// PCL2-CE 的 U/ 码（Base34 + 7 取模校验，码内不含端口）。
    /// </para>
    /// <para>
    /// 只对齐字符串格式与参数取值（字符表、按位切分、校验式、覆盖 IP、算法名），不复制任何对方源码；
    /// 对方的账号体系与中心大厅服务不接触、不模拟。
    /// </para>
    /// </summary>
    public static class ModLinkInterop
    {

        #region 常量与字符表

        /// <summary>pcl-next 房主的覆盖地址。</summary>
        public const string PclNextOverlayIp = "10.114.114.114";
        /// <summary>PCL2-CE 大厅的覆盖地址。</summary>
        public const string CeOverlayIp = "10.114.51.41";
        /// <summary>陶瓦大厅的覆盖地址。</summary>
        public const string TerracottaOverlayIp = "10.144.144.1";

        /// <summary>PCL2-CE 大厅的网络名前缀。</summary>
        public const string CeNetworkNamePrefix = "PCLCELobby";
        /// <summary>PCL2-CE 大厅的网络密钥前缀。</summary>
        public const string CeNetworkSecretPrefix = "PCLCEETLOBBY2025";
        /// <summary>陶瓦大厅的网络名前缀。</summary>
        public const string TerracottaNetworkPrefix = "terracotta-mc-";
        /// <summary>PCL2-CE Scaffolding 大厅的网络名前缀。</summary>
        public const string ScaffoldNetworkPrefix = "scaffolding-mc-";
        /// <summary>
        /// PCL2-CE Scaffolding 房主的 EasyTier 主机名前缀。对方靠这个前缀认出房主，
        /// 并把前缀之后的数字当作 Scaffolding 服务端口（`hostname[22..]`）。
        /// </summary>
        public const string ScaffoldHostnamePrefix = "scaffolding-mc-server-";

        /// <summary>PCL2-CE 数字码用的 Base32 字符表（无 0/1/I/O）。</summary>
        private const string Base32Chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        /// <summary>陶瓦码与 U 码用的 Base34 字符表（无 I/O）。</summary>
        private const string Base34Chars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        private const int TerracottaDigits = 24;
        private const int TerracottaLength = TerracottaDigits + 1;
        private const int ScaffoldPayloadLength = 16;
        private const int ScaffoldCodeLength = ScaffoldPayloadLength + 3 + 2;

        private static readonly Random Rng = new Random();

        #endregion

        #region 建房：按互通目标生成大厅参数与邀请码

        /// <summary>当前互通目标设置（0 = 仅 pcl-next，1 = PCL2-CE，2 = 陶瓦）。</summary>
        public static LobbyKind GetHostKind()
        {
            int value;
            try
            {
                value = Settings.Get<int>("LinkInteropTarget");
            }
            catch
            {
                value = 0;
            }
            return value switch
            {
                // 设置项 1 仍是「PCL2-CE 大厅」，但 PCL2-CE 现行建房码是 Scaffolding 的 U/ 码，
                // 旧数字码只保留解析能力（历史上的码仍能被认出），不再生成。
                1 => LobbyKind.PclCeScaffold,
                2 => LobbyKind.Terracotta,
                _ => LobbyKind.PclNext
            };
        }

        /// <summary>
        /// 生成房主侧的大厅参数与邀请码（支持 PclNext / PclCeScaffold / Terracotta）。
        /// <para>
        /// PCL2-CE 的现行建房码是 Scaffolding 的 U/ 码（网名与密钥都从码里切出来，码内不含端口），
        /// 所以这里也生成同构的 U/ 码；旧数字码只在解析侧保留。
        /// </para>
        /// </summary>
        /// <param name="kind">互通目标。</param>
        /// <param name="serverPort">房主本机的 MC 服务器端口。</param>
        public static (LobbyTarget Target, string Code) BuildHost(LobbyKind kind, ushort serverPort)
        {
            if (serverPort == 0)
                throw new ArgumentException("服务器端口不能为 0");
            if (kind == LobbyKind.Terracotta && serverPort < 100)
                throw new ArgumentException("陶瓦大厅要求服务器端口不小于 100");

            if (kind == LobbyKind.PclCeScaffold)
            {
                string code = BuildScaffoldCode();
                string payload = code.Substring(2); // "XXXX-XXXX-XXXX-XXXX"，19 位
                return (new LobbyTarget
                {
                    Kind = LobbyKind.PclCeScaffold,
                    FullNetworkName = ScaffoldNetworkPrefix + payload.Substring(0, 9),
                    NetworkSecret = payload.Substring(10),
                    OverlayIp = CeOverlayIp,
                    Port = serverPort,
                    EncryptionAlgorithm = "aes-gcm",
                    Compression = "zstd",
                    CompatFlags = true,
                    OriginalCode = code
                }, code);
            }

            if (kind == LobbyKind.Terracotta)
            {
                string tcCode = BuildTerracottaCode(serverPort, out string digits, out char check);
                string name = digits.Substring(0, 15).ToLowerInvariant();
                string secret = (digits.Substring(15) + check).ToLowerInvariant();
                return (new LobbyTarget
                {
                    Kind = LobbyKind.Terracotta,
                    FullNetworkName = TerracottaNetworkPrefix + name,
                    NetworkSecret = secret,
                    OverlayIp = TerracottaOverlayIp,
                    Port = serverPort,
                    EncryptionAlgorithm = "chacha20",
                    Compression = "zstd",
                    CompatFlags = true,
                    OriginalCode = tcCode
                }, tcCode);
            }

            (string ownName, string ownSecret) = ModLinkCodec.NewIdentity();
            string ownCode = ModLinkCodec.Build(ownName, ownSecret, serverPort);
            return (new LobbyTarget
            {
                Kind = LobbyKind.PclNext,
                FullNetworkName = ModLinkCodec.NetworkPrefix + ownName,
                NetworkSecret = ownSecret,
                OverlayIp = PclNextOverlayIp,
                Port = serverPort,
                EncryptionAlgorithm = "aes-gcm",
                OriginalCode = ownCode
            }, ownCode);
        }

        #endregion

        #region 加入：自动识别邀请码

        /// <summary>
        /// 自动识别邀请码并推导大厅参数。
        /// </summary>
        /// <param name="raw">用户输入的邀请码（可以夹在分享文案里）。</param>
        /// <param name="portFallback">码内没有端口时使用的端口（0 表示没有）。</param>
        /// <exception cref="ArgumentException">四类格式都无法识别。</exception>
        public static LobbyTarget Parse(string raw, ushort portFallback = 0)
        {
            string text = Normalize(raw);
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("邀请码不能为空");

            // 1) pcl-next 自研短码（网名-密钥-端口十六进制）
            try
            {
                ModLinkCodec.CodeInfo info = ModLinkCodec.Parse(text);
                return new LobbyTarget
                {
                    Kind = LobbyKind.PclNext,
                    FullNetworkName = ModLinkCodec.NetworkPrefix + info.Name,
                    NetworkSecret = info.Secret,
                    OverlayIp = PclNextOverlayIp,
                    Port = info.Port,
                    EncryptionAlgorithm = "aes-gcm",
                    OriginalCode = text
                };
            }
            catch (ArgumentException)
            {
                // 不是 pcl-next 码，继续按其它启动器的格式试
            }

            // 2) PCL2-CE 的 U 码（Scaffolding 大厅）
            if (text.StartsWith("U/", StringComparison.Ordinal))
                return ParseScaffold(text, portFallback);

            // 3) 与 4) 的分流与对方实现一致：5 段按陶瓦，其余按 PCL2-CE 数字码
            if (text.Split('-').Length == 5)
                return ParseTerracotta(text);

            return ParseCeLegacy(text);
        }

        /// <summary>从用户输入（可能夹着分享文案）里抽出邀请码本身，四类码都支持。</summary>
        public static string ExtractCode(string raw) => Normalize(raw);

        /// <summary>把邀请码清洗成只剩码本身（从分享文案里挑出候选片段）。</summary>
        private static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            string text = raw.Trim();
            foreach (string token in text.Split(new[] { ' ', '\n', '\r', '\t', '，', ',', '。', '：', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = token.Trim().ToUpperInvariant();
                if (candidate.Length >= 9 && LooksLikeCode(candidate))
                    return candidate;
            }
            return text.ToUpperInvariant().Replace(" ", "");
        }

        private static bool LooksLikeCode(string text)
        {
            if (text.StartsWith("U/", StringComparison.Ordinal))
                return true;
            foreach (char c in text)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
            return true;
        }

        /// <summary>解析 PCL2-CE 数字码：整串 Base32，解出「网名 8 位 + 密钥 2 位 + 端口」的十进制串。</summary>
        private static LobbyTarget ParseCeLegacy(string text)
        {
            BigInteger value;
            try
            {
                value = Base32Decode(text);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException("无法识别邀请码：既不是 pcl-next 短码，也不是 PCL2-CE 或陶瓦大厅码");
            }
            string decimalText = value.ToString();
            if (decimalText.Length < 11)
                throw new ArgumentException("PCL2-CE 大厅码长度不足，可能复制不完整");
            string name = decimalText.Substring(0, 8);
            string secret = decimalText.Substring(8, 2);
            string portText = decimalText.Substring(10);
            if (!ushort.TryParse(portText, out ushort port) || port == 0)
                throw new ArgumentException("PCL2-CE 大厅码里的端口无效，可能复制不完整");
            return new LobbyTarget
            {
                Kind = LobbyKind.PclCe,
                FullNetworkName = CeNetworkNamePrefix + name,
                NetworkSecret = CeNetworkSecretPrefix + secret,
                OverlayIp = CeOverlayIp,
                Port = port,
                EncryptionAlgorithm = "chacha20",
                Compression = "zstd",
                CompatFlags = true,
                OriginalCode = text
            };
        }

        /// <summary>解析陶瓦码：25 位 Base34，前 24 位小端数值，第 25 位校验，端口 = 数值 % 65536。</summary>
        private static LobbyTarget ParseTerracotta(string text)
        {
            string compact = text.Replace("-", "").ToUpperInvariant().Replace("I", "1").Replace("O", "0");
            if (compact.Length != TerracottaLength)
                throw new ArgumentException("陶瓦邀请码应为 25 位（5 组 5 位）");
            int[] digits = new int[TerracottaDigits];
            int sum = 0;
            BigInteger value = 0;
            BigInteger power = 1;
            for (int i = 0; i < TerracottaDigits; i++)
            {
                int digit = Base34Value(compact[i]);
                if (digit < 0)
                    throw new ArgumentException("陶瓦邀请码含非法字符");
                digits[i] = digit;
                sum = (sum + digit) % Base34Chars.Length;
                value += digit * power;
                power *= Base34Chars.Length;
            }
            int checkValue = Base34Value(compact[TerracottaDigits]);
            if (checkValue < 0 || checkValue != sum)
                throw new ArgumentException("陶瓦邀请码校验位不正确，可能抄错了");

            int port = (int)(value % 65536);
            if (port < 100)
                throw new ArgumentException("陶瓦邀请码里的端口无效，可能抄错了");
            string name = compact.Substring(0, 15).ToLowerInvariant();
            string secret = compact.Substring(15, 10).ToLowerInvariant();
            return new LobbyTarget
            {
                Kind = LobbyKind.Terracotta,
                FullNetworkName = TerracottaNetworkPrefix + name,
                NetworkSecret = secret,
                OverlayIp = TerracottaOverlayIp,
                Port = (ushort)port,
                EncryptionAlgorithm = "chacha20",
                Compression = "zstd",
                CompatFlags = true,
                OriginalCode = text
            };
        }

        /// <summary>解析 PCL2-CE 的 U 码（Scaffolding 大厅）：码内不含端口。</summary>
        private static LobbyTarget ParseScaffold(string text, ushort portFallback)
        {
            string upper = text.ToUpperInvariant();
            if (upper.Length != ScaffoldCodeLength)
                throw new ArgumentException("PCL2-CE 的 U 码应为 21 位（U/ 加 4 组 4 位）");
            string payload = upper.Substring(2);
            int[] values = new int[ScaffoldPayloadLength];
            int index = 0;
            for (int i = 0; i < payload.Length; i++)
            {
                char c = payload[i];
                if (c == '-')
                {
                    if (i != 4 && i != 9 && i != 14)
                        throw new ArgumentException("PCL2-CE 的 U 码连字符位置不正确");
                    continue;
                }
                int digit = Base34Value(c);
                if (digit < 0 || index >= ScaffoldPayloadLength)
                    throw new ArgumentException("PCL2-CE 的 U 码含非法字符");
                values[index++] = digit;
            }
            if (index != ScaffoldPayloadLength)
                throw new ArgumentException("PCL2-CE 的 U 码长度不正确");
            BigInteger value = 0;
            for (int i = ScaffoldPayloadLength - 1; i >= 0; i--)
                value = value * Base34Chars.Length + values[i];
            if (value % 7 != 0)
                throw new ArgumentException("PCL2-CE 的 U 码校验不通过，可能抄错了");

            return new LobbyTarget
            {
                Kind = LobbyKind.PclCeScaffold,
                FullNetworkName = ScaffoldNetworkPrefix + payload.Substring(0, 9),
                NetworkSecret = payload.Substring(10),
                OverlayIp = CeOverlayIp,
                Port = portFallback,
                EncryptionAlgorithm = "aes-gcm",
                Compression = "zstd",
                CompatFlags = true,
                OriginalCode = upper
            };
        }

        #endregion

        #region 编码工具

        /// <summary>
        /// 生成 PCL2-CE 的 U 码（Scaffolding）：16 位 Base34 小端数值，整体对 7 取模为 0，
        /// 展示成 <c>U/XXXX-XXXX-XXXX-XXXX</c>。码内不含端口（端口由对方的 Scaffolding 协议索取）。
        /// </summary>
        private static string BuildScaffoldCode()
        {
            BigInteger max = BigInteger.Pow(Base34Chars.Length, ScaffoldPayloadLength);
            byte[] buffer = new byte[16];
            Rng.NextBytes(buffer);
            BigInteger value = new BigInteger(buffer, true, false) % max;
            value -= value % 7;
            if (value <= 0)
                value = 7;

            int[] values = new int[ScaffoldPayloadLength];
            BigInteger current = value;
            for (int i = 0; i < ScaffoldPayloadLength; i++)
            {
                values[i] = (int)(current % Base34Chars.Length);
                current /= Base34Chars.Length;
            }
            StringBuilder compact = new StringBuilder(ScaffoldPayloadLength);
            foreach (int digit in values)
                compact.Append(Base34Chars[digit]);

            StringBuilder display = new StringBuilder(ScaffoldCodeLength);
            display.Append("U/");
            for (int i = 0; i < compact.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                    display.Append('-');
                display.Append(compact[i]);
            }
            return display.ToString();
        }

        /// <summary>把十进制数编码成 PCL2-CE 的 Base32 邀请码。</summary>
        private static string Base32Encode(BigInteger value)
        {
            if (value <= 0)
                throw new ArgumentException("邀请码数值必须为正");
            StringBuilder builder = new StringBuilder();
            BigInteger current = value;
            while (current > 0)
            {
                int digit = (int)(current % Base32Chars.Length);
                builder.Insert(0, Base32Chars[digit]);
                current /= Base32Chars.Length;
            }
            return builder.ToString();
        }

        /// <summary>把 PCL2-CE 的 Base32 邀请码解回十进制数。</summary>
        private static BigInteger Base32Decode(string text)
        {
            if (text.Length == 0)
                throw new ArgumentException("邀请码不能为空");
            BigInteger value = 0;
            foreach (char c in text.ToUpperInvariant())
            {
                int digit = Base32Chars.IndexOf(c);
                if (digit < 0)
                    throw new ArgumentException("邀请码含非法字符");
                value = value * Base32Chars.Length + digit;
            }
            return value;
        }

        /// <summary>取 Base34 字符的值，非法字符返回 -1（兼容对方把 I/O 当 1/0 的写法）。</summary>
        private static int Base34Value(char c)
        {
            char upper = char.ToUpperInvariant(c);
            if (upper == 'I')
                upper = '1';
            else if (upper == 'O')
                upper = '0';
            int index = Base34Chars.IndexOf(upper);
            return index < 0 ? -1 : index;
        }

        private static string RandomDigits(int length, bool nonZeroFirst)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append(i == 0 && nonZeroFirst ? (char)('1' + Rng.Next(9)) : (char)('0' + Rng.Next(10)));
            return builder.ToString();
        }

        /// <summary>生成陶瓦邀请码：数值满足「端口 = 数值 % 65536」，第 25 位是各位和模 34。</summary>
        private static string BuildTerracottaCode(ushort port, out string digits, out char check)
        {
            BigInteger max = BigInteger.Pow(Base34Chars.Length, TerracottaDigits);
            byte[] buffer = new byte[16];
            Rng.NextBytes(buffer);
            BigInteger value = new BigInteger(buffer, true, false) % max;
            long remainder = (long)(value % 65536);
            long delta = (((long)port - remainder) % 65536 + 65536) % 65536;
            value += delta;
            if (value >= max)
                value -= 65536;

            int[] values = new int[TerracottaDigits];
            BigInteger current = value;
            int sum = 0;
            for (int i = 0; i < TerracottaDigits; i++)
            {
                int digit = (int)(current % Base34Chars.Length);
                values[i] = digit;
                sum = (sum + digit) % Base34Chars.Length;
                current /= Base34Chars.Length;
            }
            StringBuilder builder = new StringBuilder(TerracottaLength);
            foreach (int digit in values)
                builder.Append(Base34Chars[digit]);
            digits = builder.ToString();
            check = Base34Chars[sum];
            builder.Append(check);
            string compact = builder.ToString();

            // 展示成 5 组 5 位（校验位属于最后一组）
            StringBuilder display = new StringBuilder(TerracottaLength + 4);
            for (int i = 0; i < compact.Length; i++)
            {
                if (i > 0 && i % 5 == 0)
                    display.Append('-');
                display.Append(compact[i]);
            }
            return display.ToString();
        }

        #endregion

    }
}
