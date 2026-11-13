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
            var NideServer = GetServerDisplay("VersionServerNide", "CacheNideServer");
            foreach (var Name in Nide.Split("¨"))
            {
                if (!string.IsNullOrEmpty(Name.Trim()))
                    List.Add(new AccountEntry() { Type = ModLaunch.McLoginType.Nide, Name = Name.Trim(), Server = NideServer });
            }
            // Authlib：LoginAuthEmail 为 ¨ 分隔邮箱
            var Auth = Settings.Get<string>("LoginAuthEmail") ?? "";
            var AuthServer = GetServerDisplay("VersionServerAuthServer", "CacheAuthServerName");
            foreach (var Name in Auth.Split("¨"))
            {
                if (!string.IsNullOrEmpty(Name.Trim()))
                    List.Add(new AccountEntry() { Type = ModLaunch.McLoginType.Auth, Name = Name.Trim(), Server = AuthServer });
            }
            return List;
        }

        /// <summary>
        /// 读取展示用的服务器名：选中版本时优先取该版本的独立设置，
        /// 否则回退到全局缓存（CacheNideServer / CacheAuthServerName）。
        /// VersionServerNide / VersionServerAuthServer 是版本独立设置（Source = Instance），
        /// 必须先有目标版本才能读；启动页在任何时候都会刷新账号列表，
        /// 直接无参读取会抛 NullReferenceException 并中断整个列表。
        /// </summary>
        private static string GetServerDisplay(string InstanceKey, string CacheKey)
        {
            try
            {
                var Instance = ModMinecraft.McInstanceSelected;
                if (Instance is not null)
                    return Settings.Get<string>(InstanceKey, Instance) ?? "";
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取版本服务器设置失败：" + InstanceKey);
            }
            try
            {
                return Settings.Get<string>(CacheKey) ?? "";
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取服务器缓存失败：" + CacheKey);
                return "";
            }
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
            SetCurrent(Entry);
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

        /// <summary>
        /// 把某个账号设为当前账号：只写设置，不触发界面刷新（供 SwitchTo / 启动页自检使用）。
        /// </summary>
        public static void SetCurrent(AccountEntry Entry)
        {
            if (Entry is null)
                return;
            try
            {
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
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "设置当前账号失败：" + (Entry.Name ?? ""));
            }
        }

        /// <summary>登录（或新建档案确认）后把账号加入对应类型列表首位，同一种登录方式可以保存多个账号。</summary>
        public static void AddAccount(ModLaunch.McLoginType Type, string Name, string Password = null)
        {
            Name = (Name ?? "").Replace("¨", "").Trim();
            if (string.IsNullOrEmpty(Name))
                return;
            try
            {
                switch (Type)
                {
                    case ModLaunch.McLoginType.Legacy:
                        Promote("LoginLegacyName", Name);
                        break;
                    case ModLaunch.McLoginType.Nide:
                        Promote("LoginNideEmail", Name);
                        if (!string.IsNullOrEmpty(Password))
                            Promote("LoginNidePass", Password.Replace("¨", "").Trim());
                        break;
                    case ModLaunch.McLoginType.Auth:
                        Promote("LoginAuthEmail", Name);
                        if (!string.IsNullOrEmpty(Password))
                            Promote("LoginAuthPass", Password.Replace("¨", "").Trim());
                        break;
                }
                Settings.Set("LoginType", Type);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "保存账号失败：" + Name);
            }
        }

        /// <summary>
        /// 启动页自检：当前 LoginType 指向的账号名已经为空（例如唯一账号被删），
        /// 但列表里还有别的账号时，自动切到列表第一个账号，保证界面显示与实际启动使用的是同一个账号。
        /// </summary>
        public static void EnsureCurrent()
        {
            try
            {
                var Current = GetCurrent();
                if (Current is not null && !string.IsNullOrEmpty(Current.Name))
                    return;
                var Accounts = GetAll();
                if (Accounts.Count == 0)
                    return;
                SetCurrent(Accounts[0]);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "修正当前账号状态失败");
            }
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
