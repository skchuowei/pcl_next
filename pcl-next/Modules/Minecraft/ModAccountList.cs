using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MeloongCore;
using MeloongCore.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCL
{
    /// <summary>
    /// 统一的游戏账号聚合与操作。
    /// 只读枚举全部登录类型的账号，并提供切换 / 删除 / 复制。
    /// 不修改现有各登录页（PageLogin*）的存储逻辑，仅在其之上做聚合视图。
    /// </summary>
    public class AccountEntry
    {
        public ModLaunch.McLoginType Type;
        public string Name = "";      // 用户名 / 邮箱
        public string Token = "";      // 微软刷新令牌
        public string Server = "";     // Nide / Auth 服务器标识（可选）
        public string Uuid = "";       // 离线 UUID（可选，暂留扩展位）

        /// <summary>账号类型的中文描述，例如「正版（微软）」「离线」。</summary>
        public string TypeText
        {
            get
            {
                switch (Type)
                {
                    case ModLaunch.McLoginType.Ms: return "正版（微软）";
                    case ModLaunch.McLoginType.Legacy: return "离线";
                    case ModLaunch.McLoginType.Nide: return "统一通行证";
                    case ModLaunch.McLoginType.Auth: return "第三方（Authlib）";
                    default: return "";
                }
            }
        }

        /// <summary>副文本：类型 + 服务器（若有）。有效期展示留待后续接入令牌内省。</summary>
        public string InfoText
        {
            get
            {
                if (string.IsNullOrEmpty(Server))
                    return TypeText;
                return TypeText + " · " + Server;
            }
        }
    }

    public static class ModAccountList
    {

        /// <summary>枚举全部登录类型的账号（只读，不修改存储）。</summary>
        public static List<AccountEntry> GetAll()
        {
            var List = new List<AccountEntry>();
            try
            {
                // 微软：LoginMsJson 为 JObject，键=用户名，值=刷新令牌
                var MsJson = (JObject)Settings.Get<string>("LoginMsJson").DeserializeJson();
                foreach (var Acc in MsJson)
                {
                    if (string.IsNullOrEmpty(Acc.Key))
                        continue;
                    List.Add(new AccountEntry() { Type = ModLaunch.McLoginType.Ms, Name = Acc.Key, Token = Acc.Value?.ToString() ?? "" });
                }
            }
            catch
            {
                // LoginMsJson 损坏或为空时忽略，不阻断其他类型
            }
            // 离线：LoginLegacyName 为 ¨ 分隔的用户名串
            var Legacy = Settings.Get<string>("LoginLegacyName") ?? "";
            foreach (var Name in Legacy.Split("¨"))
            {
                if (!string.IsNullOrEmpty(Name.Trim()))
                    List.Add(new AccountEntry() { Type = ModLaunch.McLoginType.Legacy, Name = Name.Trim() });
            }
            // 统一通行证：LoginNideEmail 为 ¨ 分隔邮箱
            var Nide = Settings.Get<string>("LoginNideEmail") ?? "";
            foreach (var Name in Nide.Split("¨"))
            {
                if (!string.IsNullOrEmpty(Name.Trim()))
                    List.Add(new AccountEntry() { Type = ModLaunch.McLoginType.Nide, Name = Name.Trim(), Server = Settings.Get<string>("VersionServerNide") ?? "" });
            }
            // Authlib：LoginAuthEmail 为 ¨ 分隔邮箱
            var Auth = Settings.Get<string>("LoginAuthEmail") ?? "";
            foreach (var Name in Auth.Split("¨"))
            {
                if (!string.IsNullOrEmpty(Name.Trim()))
                    List.Add(new AccountEntry() { Type = ModLaunch.McLoginType.Auth, Name = Name.Trim(), Server = Settings.Get<string>("VersionServerAuthServer") ?? "" });
            }
            return List;
        }

        /// <summary>获取当前选中的账号（与启动页左侧一致）。</summary>
        public static AccountEntry GetCurrent()
        {
            var Type = Settings.Get<ModLaunch.McLoginType>("LoginType");
            switch (Type)
            {
                case ModLaunch.McLoginType.Ms:
                    return new AccountEntry() { Type = Type, Name = Settings.Get<string>("CacheMsV2Name") ?? "", Token = Settings.Get<string>("CacheMsV2OAuthRefresh") ?? "" };
                case ModLaunch.McLoginType.Legacy:
                    return new AccountEntry() { Type = Type, Name = (Settings.Get<string>("LoginLegacyName") ?? "").BeforeFirst("¨").Trim() };
                case ModLaunch.McLoginType.Nide:
                    return new AccountEntry() { Type = Type, Name = (Settings.Get<string>("LoginNideEmail") ?? "").BeforeFirst("¨").Trim() };
                case ModLaunch.McLoginType.Auth:
                    return new AccountEntry() { Type = Type, Name = (Settings.Get<string>("LoginAuthEmail") ?? "").BeforeFirst("¨").Trim() };
                default:
                    return null;
            }
        }

        /// <summary>将某个账号切换为当前账号：设置 LoginType，并把该账号提到对应类型列表首位，刷新启动页与登录页。</summary>
        public static void SwitchTo(AccountEntry Entry)
        {
            if (Entry is null)
                return;
            Settings.Set("LoginType", Entry.Type);
            switch (Entry.Type)
            {
                case ModLaunch.McLoginType.Ms:
                    {
                        Settings.Set("CacheMsV2Name", Entry.Name);
                        Settings.Set("CacheMsV2OAuthRefresh", Entry.Token);
                        ModMain.FrmLoginMs?.Reload(false);
                        break;
                    }
                case ModLaunch.McLoginType.Legacy:
                    {
                        Promote("LoginLegacyName", Entry.Name);
                        ModMain.FrmLoginLegacy?.Reload(false);
                        break;
                    }
                case ModLaunch.McLoginType.Nide:
                    {
                        Promote("LoginNideEmail", Entry.Name);
                        ModMain.FrmLoginNide?.Reload(false);
                        break;
                    }
                case ModLaunch.McLoginType.Auth:
                    {
                        Promote("LoginAuthEmail", Entry.Name);
                        ModMain.FrmLoginAuth?.Reload(false);
                        break;
                    }
            }
            // 同步启动页「正版 / 离线」单选状态，避免 RefreshPage 依据旧单选状态把 LoginType 覆盖回去。
            // 注意：若当前版本强制了单一登录方式（如「仅正版」），RefreshPage 仍会按其约束纠正 LoginType，
            // 这是版本级的登录要求，属于预期行为。
            if (ModMain.FrmLaunchLeft != null &&
                (Entry.Type == ModLaunch.McLoginType.Ms || Entry.Type == ModLaunch.McLoginType.Legacy))
            {
                var Radio = (MyRadioButton)ModMain.FrmLaunchLeft.FindName("RadioLoginType" + (int)Entry.Type);
                if (Radio != null && !Radio.Checked)
                    Radio.Checked = true;
            }
            ModMain.FrmLaunchLeft?.RefreshPage(false, true);
        }

        /// <summary>删除账号（仅移除本地登录记录，不注销远程账号）。</summary>
        public static void Delete(AccountEntry Entry)
        {
            if (Entry is null)
                return;
            try
            {
                switch (Entry.Type)
                {
                    case ModLaunch.McLoginType.Ms:
                        {
                            var MsJson = (JObject)Settings.Get<string>("LoginMsJson").DeserializeJson();
                            if (MsJson[Entry.Name] != null)
                            {
                                MsJson.Remove(Entry.Name);
                                Settings.Set("LoginMsJson", MsJson.ToString(Formatting.None));
                            }
                            ModMain.FrmLoginMs?.Reload(false);
                            break;
                        }
                    case ModLaunch.McLoginType.Legacy:
                        Settings.Set("LoginLegacyName", string.Join("¨", (Settings.Get<string>("LoginLegacyName") ?? "").Split("¨").Where(p => p.Trim() != Entry.Name)));
                        ModMain.FrmLoginLegacy?.Reload(false);
                        break;
                    case ModLaunch.McLoginType.Nide:
                        Settings.Set("LoginNideEmail", string.Join("¨", (Settings.Get<string>("LoginNideEmail") ?? "").Split("¨").Where(p => p.Trim() != Entry.Name)));
                        ModMain.FrmLoginNide?.Reload(false);
                        break;
                    case ModLaunch.McLoginType.Auth:
                        Settings.Set("LoginAuthEmail", string.Join("¨", (Settings.Get<string>("LoginAuthEmail") ?? "").Split("¨").Where(p => p.Trim() != Entry.Name)));
                        ModMain.FrmLoginAuth?.Reload(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "删除账号失败");
            }
            ModMain.FrmLaunchLeft?.RefreshPage(false, true);
        }

        /// <summary>把指定名称提到 ¨ 分隔列表的首位，使其成为当前账号。</summary>
        private static void Promote(string Key, string Name)
        {
            var Parts = (Settings.Get<string>(Key) ?? "").Split("¨").Where(p => !string.IsNullOrEmpty(p.Trim())).ToList();
            Parts.Remove(Name.Trim());
            Parts.Insert(0, Name.Trim());
            Settings.Set(Key, string.Join("¨", Parts));
        }

        /// <summary>复制账号名称到剪贴板。</summary>
        public static void CopyName(AccountEntry Entry)
        {
            if (Entry is null)
                return;
            try
            {
                Clipboard.SetText(Entry.Name);
                ModMain.Hint("已复制账号名称", ModMain.HintType.Green);
            }
            catch
            {
                ModMain.Hint("复制失败", ModMain.HintType.Red);
            }
        }
    }
}
