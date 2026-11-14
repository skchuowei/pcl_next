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

        /// <summary>
        /// 界面展示顺序缓存。切换当前账号时存储必须把该账号提到 ¨ 列表首位（首位 = 当前账号），
        /// 若界面直接按存储顺序渲染，点一下别的账号列表就会整排往上跳。
        /// 这里记录首次枚举到的顺序：老账号位置固定，新账号追加到末尾，重命名/删除原地生效。
        /// </summary>
        private static readonly Dictionary<ModLaunch.McLoginType, List<string>> DisplayOrders = new Dictionary<ModLaunch.McLoginType, List<string>>();

        private static List<AccountEntry> ApplyDisplayOrder(List<AccountEntry> Entries)
        {
            var Result = new List<AccountEntry>(Entries.Count);
            // 按「类型首次出现」分组，保持 GetAll 里各类型的先后顺序
            foreach (var Group in Entries.GroupBy(e => e.Type))
            {
                if (!DisplayOrders.TryGetValue(Group.Key, out var Order))
                {
                    Order = new List<string>();
                    DisplayOrders[Group.Key] = Order;
                }
                foreach (var Entry in Group)
                {
                    if (!Order.Contains(Entry.Name))
                        Order.Add(Entry.Name);
                }
                // OrderBy 是稳定排序，缺失项（理论上不会有）统一排到末尾
                foreach (var Entry in Group.OrderBy(e =>
                {
                    int Index = Order.IndexOf(e.Name);
                    return Index < 0 ? int.MaxValue : Index;
                }))
                    Result.Add(Entry);
            }
            return Result;
        }

        /// <summary>重命名后同步展示顺序，避免改名把该账号甩到列表末尾。</summary>
        private static void RenameInOrder(ModLaunch.McLoginType Type, string OldName, string NewName)
        {
            if (!DisplayOrders.TryGetValue(Type, out var Order))
                return;
            int Index = Order.FindIndex(p => p == OldName);
            if (Index < 0)
                return;
            Order[Index] = NewName;
        }

        private static void RemoveFromOrder(ModLaunch.McLoginType Type, string Name)
        {
            if (!DisplayOrders.TryGetValue(Type, out var Order))
                return;
            Order.RemoveAll(p => p == Name);
        }

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
            return ApplyDisplayOrder(List);
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
            string Result = "";
            try
            {
                var Instance = ModMinecraft.McInstanceSelected;
                if (Instance is not null)
                    Result = Settings.Get<string>(InstanceKey, Instance) ?? "";
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "读取版本服务器设置失败：" + InstanceKey);
            }
            // 版本独立设置为空时继续读全局缓存，不能直接返回空串，
            // 否则新建版本没填过服务器，列表里就永远看不到实际使用的验证服务器。
            if (!string.IsNullOrEmpty(Result))
                return Result;
            try
            {
                Result = Settings.Get<string>(CacheKey) ?? "";
                // Authlib 未显式保存服务器名时展示默认验证服务器的主机名，
                // 避免列表里只显示「第三方（Authlib）」而看不出实际连的是哪个服务器
                if (string.IsNullOrEmpty(Result) && CacheKey == "CacheAuthServerName")
                {
                    try
                    {
                        return new Uri(ModMinecraft.McAuthServerGet()).Host;
                    }
                    catch
                    {
                        return "";
                    }
                }
                return Result;
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
                            PromotePair("LoginNideEmail", "LoginNidePass", Entry.Name, null);
                            ModMain.FrmLoginNide?.Reload(false);
                            break;
                        }
                    case ModLaunch.McLoginType.Auth:
                        {
                            PromotePair("LoginAuthEmail", "LoginAuthPass", Entry.Name, null);
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
                        PromotePair("LoginNideEmail", "LoginNidePass", Name, Password);
                        break;
                    case ModLaunch.McLoginType.Auth:
                        PromotePair("LoginAuthEmail", "LoginAuthPass", Name, Password);
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

        /// <summary>重命名离线账号（原地替换，保持列表顺序）。返回空字符串表示成功，否则为错误提示。</summary>
        public static string Rename(AccountEntry Entry, string NewName)
        {
            if (Entry is null)
                return "账号不存在";
            if (Entry.Type != ModLaunch.McLoginType.Legacy)
                return "只有离线账号支持修改玩家名";
            NewName = (NewName ?? "").Replace("¨", "").Trim();
            if (string.IsNullOrEmpty(NewName))
                return "玩家名不能为空";
            if (NewName == Entry.Name)
                return "";
            var Names = SplitList("LoginLegacyName");
            int Index = Names.FindIndex(p => p.Trim() == Entry.Name.Trim());
            if (Index < 0)
                return "账号已不存在，请刷新后重试";
            if (Names.Where((p, i) => i != Index).Any(p => p.Trim() == NewName))
                return "已存在同名账号";
            Names[Index] = NewName;
            Settings.Set("LoginLegacyName", string.Join("¨", Names));
            RenameInOrder(ModLaunch.McLoginType.Legacy, Entry.Name, NewName);
            ModMain.FrmLoginLegacy?.Reload(false);
            ModMain.FrmLaunchLeft?.RefreshPage(false, true);
            return "";
        }

        /// <summary>修改 Authlib / 统一通行证的登录密码（原地替换，保持账号与密码列表不错位）。</summary>
        public static string SetPassword(AccountEntry Entry, string NewPassword)
        {
            if (Entry is null)
                return "账号不存在";
            if (Entry.Type != ModLaunch.McLoginType.Auth && Entry.Type != ModLaunch.McLoginType.Nide)
                return "该账号类型不支持修改密码";
            NewPassword = (NewPassword ?? "").Replace("¨", "");
            if (string.IsNullOrEmpty(NewPassword))
                return "密码不能为空";
            string NameKey = Entry.Type == ModLaunch.McLoginType.Auth ? "LoginAuthEmail" : "LoginNideEmail";
            string PassKey = Entry.Type == ModLaunch.McLoginType.Auth ? "LoginAuthPass" : "LoginNidePass";
            var Names = SplitList(NameKey);
            var Passes = SplitList(PassKey);
            int Index = Names.FindIndex(p => p.Trim() == Entry.Name.Trim());
            if (Index < 0)
                return "账号已不存在，请刷新后重试";
            while (Passes.Count <= Index)
                Passes.Add("");
            Passes[Index] = NewPassword;
            while (Passes.Count > Names.Count)
                Passes.RemoveAt(Passes.Count - 1);
            Settings.Set(PassKey, string.Join("¨", Passes));
            if (Entry.Type == ModLaunch.McLoginType.Auth)
                ModMain.FrmLoginAuth?.Reload(false);
            else
                ModMain.FrmLoginNide?.Reload(false);
            ModMain.FrmLaunchLeft?.RefreshPage(false, true);
            return "";
        }

        /// <summary>
        /// 保存 Authlib 验证服务器：清掉上一个服务器的登录与皮肤缓存，
        /// 否则切换服务器后还会继续用旧 UUID 取皮肤/校验令牌。
        /// </summary>
        public static string SetAuthServer(string Server)
        {
            Server = (Server ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(Server, UriKind.Absolute, out var Parsed) ||
                (Parsed.Scheme != "http" && Parsed.Scheme != "https"))
                return "请输入完整的 http/https 服务器地址";
            string OldServer = (Settings.Get<string>("CacheAuthServerServer") ?? "").Trim().TrimEnd('/');
            Settings.Set("CacheAuthServerServer", Server);
            Settings.Set("CacheAuthServerName", Parsed.Host);
            if (string.IsNullOrEmpty(Settings.Get<string>("CacheAuthServerRegister")))
                Settings.Set("CacheAuthServerRegister", Parsed.GetLeftPart(UriPartial.Authority) + "/auth/register");
            if (OldServer != Server)
            {
                Settings.Set("CacheAuthAccess", "");
                Settings.Set("CacheAuthClient", "");
                Settings.Set("CacheAuthUuid", "");
                Settings.Set("CacheAuthName", "");
                Settings.Set("CacheAuthPass", "");
            }
            ModMain.FrmLoginAuth?.Reload(false);
            ModMain.FrmLaunchLeft?.RefreshPage(false, true);
            return "";
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
                        RemovePair("LoginNideEmail", "LoginNidePass", Entry.Name);
                        ModMain.FrmLoginNide?.Reload(false);
                        break;
                    case ModLaunch.McLoginType.Auth:
                        RemovePair("LoginAuthEmail", "LoginAuthPass", Entry.Name);
                        ModMain.FrmLoginAuth?.Reload(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "删除账号失败");
            }
            RemoveFromOrder(Entry.Type, Entry.Name);
            ModMain.FrmLaunchLeft?.RefreshPage(false, true);
        }

        /// <summary>把指定名称提到 ¨ 分隔列表的首位，使其成为当前账号。</summary>
        private static void Promote(string Key, string Name)
        {
            var Parts = SplitList(Key).Where(p => !string.IsNullOrEmpty(p.Trim())).ToList();
            Parts.RemoveAll(p => p.Trim() == Name.Trim());
            Parts.Insert(0, Name.Trim());
            Settings.Set(Key, string.Join("¨", Parts));
        }

        /// <summary>
        /// 名称与密码是两个平行的 ¨ 分隔列表：切换账号时必须把同一行的密码一起提到首位，
        /// 否则「显示的账号」与「实际登录用的密码」会错位（表现为切换后登录失败）。
        /// </summary>
        private static void PromotePair(string NameKey, string PasswordKey, string Name, string Password)
        {
            Name = (Name ?? "").Replace("¨", "").Trim();
            if (string.IsNullOrEmpty(Name))
                return;
            var Names = SplitList(NameKey).Where(p => !string.IsNullOrEmpty(p.Trim())).ToList();
            var Passes = SplitList(PasswordKey);
            int Index = Names.FindIndex(p => p.Trim() == Name);
            string OldPassword = Index >= 0 && Index < Passes.Count ? Passes[Index] : "";
            if (Index >= 0)
            {
                Names.RemoveAt(Index);
                if (Index < Passes.Count)
                    Passes.RemoveAt(Index);
            }
            Names.Insert(0, Name);
            Passes.Insert(0, Password is null ? OldPassword : Password.Replace("¨", "").Trim());
            // 两个列表长度必须一致，否则下一次按下标取值会错行
            while (Passes.Count < Names.Count)
                Passes.Add("");
            while (Passes.Count > Names.Count)
                Passes.RemoveAt(Passes.Count - 1);
            Settings.Set(NameKey, string.Join("¨", Names));
            Settings.Set(PasswordKey, string.Join("¨", Passes));
        }

        private static List<string> SplitList(string Key)
        {
            var Raw = Settings.Get<string>(Key) ?? "";
            return Raw.Length == 0 ? new List<string>() : Raw.Split("¨").ToList();
        }

        /// <summary>删除账号时同步删除与之同行的密码，避免邮箱列表与密码列表错位。</summary>
        private static void RemovePair(string NameKey, string PasswordKey, string Name)
        {
            var Names = SplitList(NameKey);
            var Passes = SplitList(PasswordKey);
            int Index = Names.FindIndex(p => p.Trim() == (Name ?? "").Trim());
            if (Index < 0)
                return;
            Names.RemoveAt(Index);
            if (Index < Passes.Count)
                Passes.RemoveAt(Index);
            Settings.Set(NameKey, string.Join("¨", Names.Where(p => !string.IsNullOrEmpty(p.Trim()))));
            while (Passes.Count < Names.Count)
                Passes.Add("");
            while (Passes.Count > Names.Count)
                Passes.RemoveAt(Passes.Count - 1);
            Settings.Set(PasswordKey, string.Join("¨", Passes));
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
