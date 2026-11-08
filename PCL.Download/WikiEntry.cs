namespace PCL.Download;

/// <summary>
/// MC 百科数据库条目（PCL.Download 侧的精简 DTO）。
/// <para/>字段与 PCLCS.Resource.WikiEntry 一一对应（Id / ChineseName / Slugs / Popularity），
/// 由宿主（Phase 4 桥接层）将 PCLCS.WikiEntry 映射后注入 <see cref="ResourceConfig.WikiEntries"/>。
/// 这样 PCL.Download 不反向引用 net10.0-windows 的 PCLCS，符合迁移计划 §3.3 的 DTO 解耦方案。
/// </summary>
public sealed class WikiEntry
{
    /// <summary>在 MC 百科中的对应 ID。</summary>
    public int Id;

    /// <summary>中文译名。null 代表没有翻译。</summary>
    public string? ChineseName;

    /// <summary>各个 Mod 平台的 Slug（例如 advanced-solar-panels）。若没有对应键则为不在该平台上。</summary>
    public Dictionary<ResourcePlatforms, string> Slugs { get; init; } = [];

    /// <summary>MC 百科的浏览量逆序排行，1 代表浏览量最低。</summary>
    public int Popularity;
}
