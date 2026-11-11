using System;
using MeloongCore;
// 资源源可用性门面：把 C# 侧 ResourceProviderRegistry 的结论交给 VB 使用（dev.4 §2.1 A4 / C）。
// 职责有三：
// 1. 启动时注入 C# 侧的 ResourceApi.Default 与 ResourceSigningOptions.Default（C# 联网方法的前提）；
// 2. 未配置 PCL_CURSEFORGE_API_KEY 时收敛搜索源，避免向 CurseForge 发必然 403 的请求；
// 3. 给 UI 提供可直接展示的降级文案。
using DlPlatforms = PCL.Download.ResourcePlatforms;
using DlRegistry = PCL.Download.ResourceProviderRegistry;
using PCLCS;

namespace PCL
{

    public static class ResourceSourceGate
    {

        private readonly static object InitLock = new object();
        private static DlRegistry _Registry = null;
        private static Download.HttpResourceApi _HttpApi = null;

        /// <summary>
    /// 初始化 C# 侧资源站客户端并注入全局默认请求器。可重复调用，只有首次生效。
    /// 未调用时，直接使用 C# 联网方法（Provider / 在线反查 / 融合搜索）会抛「未注入 IResourceApi」。
    /// </summary>
        public static void EnsureInitialized()
        {
            if (_Registry is not null)
                return;
            lock (InitLock)
            {
                if (_Registry is not null)
                    return;
                // 请求头规则与 ModSecret.SecretHeadersSign 保持一致：UA / Referer / x-api-key
                var Signing = new Download.ResourceSigningOptions()
                {
                    UserAgent = $"PCL2/{ModBase.VersionBaseName}.{(int)ModBase.BuildType}",
                    Referer = $"http://{ModBase.VersionCode}.open.pcl2.server/",
                    CurseForgeApiKey = ModSecret.CurseForgeAPIKey
                };
                Download.ResourceSigningOptions.Default = Signing;
                _HttpApi = new Download.HttpResourceApi(Signing);
                Download.ResourceApi.Default = _HttpApi;
                _Registry = DlRegistry.CreateDefault(_HttpApi, Signing);
                if (!Signing.HasCurseForgeApiKey)
                {
                    Logger.Info("未配置 PCL_CURSEFORGE_API_KEY，CurseForge 源将自动降级为 Modrinth");
                }
            }
        }

        /// <summary>
    /// 已初始化的 Provider 注册表。首次访问会自动完成 <see cref="EnsureInitialized"/>。
    /// </summary>
        public static DlRegistry Registry
        {
            get
            {
                EnsureInitialized();
                return _Registry;
            }
        }

        /// <summary>
    /// CurseForge 源当前是否可用（未配置 API Key 时为 False）。
    /// 门面自身出错时按「可用」处理，避免因可用性探测失败而影响正常搜索。
    /// </summary>
        public static bool CurseForgeAvailable
        {
            get
            {
                try
                {
                    return (bool)Registry.Get(DlPlatforms.CurseForge)?.IsAvailable;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "探测 CurseForge 源可用性失败，按可用处理");
                    return true;
                }
            }
        }

        /// <summary>
    /// 降级提示文案；无降级时返回 Nothing。可直接展示在 UI 上。
    /// </summary>
        public static string DegradationNotice
        {
            get
            {
                if (CurseForgeAvailable)
                    return null;
                return DlRegistry.CurseForgeDisabledNotice;
            }
        }

        /// <summary>
    /// 把「用户期望的源」收敛为「实际可用的源」。
    /// </summary>
    /// <param name="Requested">用户选择的源，可能包含不可用的 CurseForge。</param>
    /// <param name="Notice">输出：因降级产生的提示文案；无降级时为 Nothing。</param>
        public static ResourcePlatforms ResolveSources(ResourcePlatforms Requested, ref string Notice)
        {
            Notice = null;
            try
            {
                var Resolved = Registry.ResolveSources((DlPlatforms)(int)Requested, out Notice);
                return (ResourcePlatforms)(int)Resolved;
            }
            catch (Exception ex)
            {
                // 可用性判断失败不应中断搜索：原样放行，由既有的网络错误提示兜底
                Logger.Warn(ex, "收敛资源源失败，按原选择处理");
                return Requested;
            }
        }

    }
}