using System;
using System.IO;
using MeloongCore;

namespace PCL
{
    // 本文件由本分支新增：把上游因合规原因在编译期关闭的 MC 下载功能，改为部署期可配置。

    /// <summary>
/// Minecraft 下载功能的部署期开关。
/// 上游 PCL 用编译期常量 <c>ModSecret.RestrictMcDownload = True</c> 固定关闭该功能，
/// 本分支将该决策从“编译期”下移到“部署期”，使其可在不重新编译的前提下切换，
/// 从而把合规决策与代码实现解耦。
/// </summary>
/// <remarks>
/// 生效顺序（先命中者优先）：
/// <list type="number">
/// <item>环境变量 <c>PCL_ALLOW_MC_DOWNLOAD</c>：True/1/yes/on 为启用，False/0/no/off 为禁用</item>
/// <item>启动器目录下的 <c>DownloadPolicy.ini</c>，文件内写 <c>AllowMcDownload=False</c> 之类的键值对</item>
/// <item>内置默认值 <see cref="DefaultAllowMcDownload"/></item>
/// </list>
/// 每次启动仅在首次访问时读取一次，随后使用缓存，因此运行期修改配置文件不会立即生效。
/// </remarks>
    internal static class ModDownloadPolicy
    {

        /// <summary>
    /// 未做任何配置时，MC 下载功能的默认状态。
    /// 本分支的定位是恢复该功能，故默认启用；若要回到上游的禁用状态，
    /// 只需在 DownloadPolicy.ini 中写入 <c>AllowMcDownload=False</c>。
    /// </summary>
        public const bool DefaultAllowMcDownload = true;

        /// <summary>
    /// 配置文件名称，位于启动器所在目录。
    /// </summary>
        public const string PolicyFileName = "DownloadPolicy.ini";

        /// <summary>
    /// 覆盖开关的环境变量名。
    /// </summary>
        public const string PolicyEnvName = "PCL_ALLOW_MC_DOWNLOAD";

        private readonly static object LoadLock = new object();
        private static bool? LoadedAllowMcDownload;
        private static string LoadedPolicySource = "";

        /// <summary>
    /// 是否允许下载 Minecraft 本体与整合包，以及与之相关的安装、修改入口。
    /// </summary>
        public static bool AllowMcDownload
        {
            get
            {
                EnsureLoaded();
                return LoadedAllowMcDownload.Value;
            }
        }

        /// <summary>
    /// 是否禁用 MC 下载相关功能。
    /// 保留该名称以便沿用上游的调用点写法（等价的取反），减少与上游的合并冲突。
    /// </summary>
        public static bool RestrictMcDownload
        {
            get
            {
                return !AllowMcDownload;
            }
        }

        /// <summary>
    /// 当前策略的来源描述，便于日志与问题排查。
    /// </summary>
        public static string PolicySource
        {
            get
            {
                EnsureLoaded();
                return LoadedPolicySource;
            }
        }

        /// <summary>
    /// 配置文件的预期路径（启动器所在目录）。
    /// </summary>
        public static string PolicyFilePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PolicyFileName);
            }
        }

        private static void EnsureLoaded()
        {
            lock (LoadLock)
            {
                if (LoadedAllowMcDownload is not null)
                    return;
                bool Allow = DefaultAllowMcDownload;
                string Source = "内置默认值";

                // 1. 环境变量优先
                string EnvRaw = Environment.GetEnvironmentVariable(PolicyEnvName);
                if (!string.IsNullOrWhiteSpace(EnvRaw))
                {
                    var EnvValue = default(bool);
                    if (TryParseSwitch(EnvRaw, ref EnvValue))
                    {
                        Allow = EnvValue;
                        Source = "环境变量 " + PolicyEnvName;
                    }
                    else
                    {
                        Logger.Warn($"环境变量 {PolicyEnvName} 的取值无效（{EnvRaw}），已忽略");
                    }
                }

                // 2. 启动器目录下的配置文件
                if (Source == "内置默认值")
                {
                    try
                    {
                        string ConfigPath = PolicyFilePath;
                        if (File.Exists(ConfigPath))
                        {
                            var FileValue = default(bool);
                            if (TryReadIniSwitch(ConfigPath, ref FileValue))
                            {
                                Allow = FileValue;
                                Source = ConfigPath;
                            }
                            else
                            {
                                Logger.Warn($"{PolicyFileName} 中未找到有效的 AllowMcDownload 配置项，已忽略");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, $"读取 {PolicyFileName} 失败，将使用默认的 MC 下载策略");
                    }
                }

                LoadedAllowMcDownload = Allow;
                LoadedPolicySource = Source;
                Logger.Info($"MC 下载功能：{(Allow ? "已启用" : "已禁用")}（来源：{Source}）");
            }
        }

        /// <summary>
    /// 解析开关型文本，支持 True/False、1/0、yes/no、on/off。
    /// </summary>
        private static bool TryParseSwitch(string Raw, ref bool Value)
        {
            switch (Raw.Trim().ToLowerInvariant() ?? "")
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    {
                        Value = true;
                        return true;
                    }
                case "false":
                case "0":
                case "no":
                case "off":
                    {
                        Value = false;
                        return true;
                    }

                default:
                    {
                        return false;
                    }
            }
        }

        /// <summary>
    /// 从 ini 风格文件中读取 AllowMcDownload 开关。找到合法取值则返回 True。
    /// 以 ; # 开头的行视为注释，[Section] 行会被跳过（不区分节）。
    /// </summary>
        private static bool TryReadIniSwitch(string ConfigPath, ref bool Value)
        {
            foreach (string RawLine in File.ReadAllLines(ConfigPath))
            {
                string Line = RawLine.Trim();
                if (Line.Length == 0)
                    continue;
                if (Line.StartsWith(";") || Line.StartsWith("#") || Line.StartsWith("["))
                    continue;
                int SplitIndex = Line.IndexOf('=');
                if (SplitIndex <= 0)
                    continue;
                if (!Line.Substring(0, SplitIndex).Trim().Equals("AllowMcDownload", StringComparison.OrdinalIgnoreCase))
                    continue;
                return TryParseSwitch(Line.Substring(SplitIndex + 1), ref Value);
            }
            return false;
        }

    }
}