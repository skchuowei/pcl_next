using Newtonsoft.Json.Linq;

namespace PCL
{

    /// <summary>
    /// 联机（EasyTier）内置配置源。
    /// <para>
    /// 背景：上游把联机页所需配置放在 <c>ModSecret.ServerConfig["Link"]</c> 中，
    /// 而 pcl-next 的开源构建不再拉取该远程配置（<c>ModSecret.ServerConfig</c> 恒为 null，
    /// 见 <c>ModSecret.cs</c> 中「上游遗留字段」注释），导致旧联机页在第一步「获取配置」就直接抛异常、
    /// 整条链路不可用。
    /// </para>
    /// <para>
    /// 这里用内置常量补回与原结构同名同义的字段，<c>PageLinkMain</c> 只把数据源从
    /// <c>ModSecret.ServerConfig</c> 换成 <see cref="Config"/>，其余逻辑不动。
    /// 联机网络层只使用 EasyTier 的公共节点，不依赖任何 pcl-next 自建服务。
    /// </para>
    /// </summary>
    public static class ModLinkConfig
    {

        #region 常量

        /// <summary>内置 EasyTier 版本号（用于与 Settings["LinkEasyTierVersion"] 比较，编码方式：主版本 * 100 + 次版本 * 10，如 2.6.4 -> 264）。</summary>
        public const int EasyTierVersionCode = 264;

        /// <summary>内置 EasyTier 版本名，仅用于提示文案。</summary>
        public const string EasyTierVersionName = "2.6.4";

        #endregion

        #region 配置

        /// <summary>
        /// 与 <c>ModSecret.ServerConfig</c> 同结构的联机配置。
        /// </summary>
        public static readonly JObject Config = new JObject
        {
            ["Link"] = new JObject
            {
                // EasyTier 版本与下载源（{arch} 会被替换为 i686 / x86_64 / arm64）
                ["EasyTierVersion"] = EasyTierVersionCode,
                ["Downloads"] = new JArray
                {
                    "https://github.com/EasyTier/EasyTier/releases/download/v" + EasyTierVersionName + "/easytier-windows-{arch}-v" + EasyTierVersionName + ".zip",
                    "https://ghproxy.net/https://github.com/EasyTier/EasyTier/releases/download/v" + EasyTierVersionName + "/easytier-windows-{arch}-v" + EasyTierVersionName + ".zip"
                },

                // EasyTier 基础启动参数（网络名/密钥/监听端口等由 PageLinkMain 拼接）
                // --no-tun：不创建 TUN 虚拟网卡，避免普通用户（asInvoker）没有管理员权限时启动失败；
                // 这与 PCL2-CE 的做法一致（其 EasyTier 启动参数同样带 no-tun，联机靠端口转发 + 端口白名单打通）。
                ["Argument"] = "--no-tun",

                // 回退发现节点：主节点不可用时房主/加入者共同连它，保证能互相发现
                ["DiscoverPeer"] = "tcp://public.easytier.top:11010",
                ["DiscoverPeerId"] = 0,

                // 内置节点池：EasyTier 官方公共节点（社区免费共享，无 SLA）
                ["Peers"] = new JArray
                {
                    "tcp://public.easytier.top:11010",
                    "tcp://public2.easytier.cn:54321",
                    "udp://public.easytier.top:11010",
                    "udp://public2.easytier.cn:54321"
                },
                ["PeersBlackList"] = new JArray(),
                ["MandatoryPeers"] = new JArray(),
                // 加入者额外连接的中继节点个数
                ["RandomPeer"] = 2,

                // 节点列表 API：留空表示不联网抓节点列表，直接用上面的内置节点池。
                // 原上游使用的 https://uptime.easytier.cn/api/nodes 已停止解析（2026-11-14 实测 DNS 不存在），
                // 因此默认关闭；将来有可用镜像时只需在此填入 URL。
                ["NodeApiUrl"] = "",

                // 留给将来做「与其它启动器联机网络隔离」用；空串表示沿用上游网络名规则，保持邀请码互通
                ["NetworkNamePrefix"] = "",

                // 保留字段语义：远程禁用联机 / 最低版本要求
                ["DisableReason2"] = "",
                ["MinVersionCode"] = 0
            }
        };

        #endregion

        #region 便捷访问

        /// <summary>
        /// 获取 <c>Config["Link"]</c>。
        /// </summary>
        public static JObject Link => (JObject)Config["Link"];

        #endregion

    }
}
