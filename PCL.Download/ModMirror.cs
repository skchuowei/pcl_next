namespace PCL.Download;

/// <summary>
/// Mod 资源站镜像源地址映射（平移自 VB ModDownload.DlSourceModGet）。
/// </summary>
public static class ModMirror
{
    /// <summary>
    /// 将 Modrinth / CurseForge 的官方地址映射为 MCIM 镜像地址。
    /// 替换顺序与 VB 原实现保持一致（注意：staging-api.modrinth.com 会先被第一条规则命中，
    /// 第二条规则实际不可达，属上游遗留，行为与 VB 等价故不调整）。
    /// </summary>
    public static string Get(string original) => original
        .Replace("api.modrinth.com", "mod.mcimirror.top/modrinth")
        .Replace("staging-api.modrinth.com", "mod.mcimirror.top/modrinth")
        .Replace("cdn.modrinth.com", "mod.mcimirror.top")
        .Replace("api.curseforge.com", "mod.mcimirror.top/curseforge")
        .Replace("edge.forgecdn.net", "mod.mcimirror.top")
        .Replace("mediafilez.forgecdn.net", "mod.mcimirror.top")
        .Replace("media.forgecdn.net", "mod.mcimirror.top");
}
