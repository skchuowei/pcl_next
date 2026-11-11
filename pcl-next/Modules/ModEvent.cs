using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;


#region 附加属性

using static PCL.FormMain;

namespace PCL
{

    /// <summary>
/// 用于在 XAML 中初始化列表对象。
/// 附加属性无法在 XAML 中为每个对象初始化独立的列表对象，因此需要一个包装类，然后在 XAML 中显式初始化。
/// </summary>
    [System.Windows.Markup.ContentProperty("Events")]
    public class CustomEventCollection : IEnumerable<CustomEvent>
    {
        private List<CustomEvent> _Events = new List<CustomEvent>();
        public List<CustomEvent> Events
        {
            get
            {
                return _Events;
            }
        }
        public IEnumerator<CustomEvent> GetEnumerator()
        {
            return ((IEnumerable<CustomEvent>)Events).GetEnumerator();
        }
        private IEnumerator IEnumerable_GetEnumerator()
        {
            return ((IEnumerable)Events).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => IEnumerable_GetEnumerator();
    }

    /// <summary>
/// 提供自定义事件的附加属性。
/// </summary>
    public class CustomEventService
    {

        // Events
        public static readonly DependencyProperty EventsProperty = DependencyProperty.RegisterAttached("Events", typeof(CustomEventCollection), typeof(CustomEventService), new PropertyMetadata(null));
        [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
        public static void SetEvents(DependencyObject d, CustomEventCollection value)
        {
            d.SetValue(EventsProperty, value);
        }
        [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
        public static CustomEventCollection GetEvents(DependencyObject d)
        {
            if (d.GetValue(EventsProperty) is null)
                d.SetValue(EventsProperty, new CustomEventCollection());
            return (CustomEventCollection)d.GetValue(EventsProperty);
        }

        // EventType
        public static readonly DependencyProperty EventTypeProperty = DependencyProperty.RegisterAttached("EventType", typeof(CustomEvent.EventType), typeof(CustomEventService), new PropertyMetadata(null));
        [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
        public static void SetEventType(DependencyObject d, CustomEvent.EventType value)
        {
            d.SetValue(EventTypeProperty, value);
        }
        [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
        public static CustomEvent.EventType GetEventType(DependencyObject d)
        {
            return (CustomEvent.EventType)Conversions.ToInteger(d.GetValue(EventTypeProperty));
        }

        // EventData
        public static readonly DependencyProperty EventDataProperty = DependencyProperty.RegisterAttached("EventData", typeof(string), typeof(CustomEventService), new PropertyMetadata(null));
        [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
        public static void SetEventData(DependencyObject d, string value)
        {
            d.SetValue(EventDataProperty, value);
        }
        [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
        public static string GetEventData(DependencyObject d)
        {
            return Conversions.ToString(d.GetValue(EventDataProperty));
        }

    }

    public static partial class ModMain
    {

        /// <summary>
    /// 触发该控件上的自定义事件。
    /// 事件会在新线程中执行。
    /// </summary>
        public static void RaiseCustomEvent(this DependencyObject Control)
        {
            // 收集事件列表
            var Events = CustomEventService.GetEvents(Control).ToList();
            var EventType = CustomEventService.GetEventType(Control);
            if (EventType != CustomEvent.EventType.None)
                Events.Add(new CustomEvent(EventType, CustomEventService.GetEventData(Control)));
            // 执行事件
            if (!Events.Any())
                return;
            ModBase.RunInNewThread(() => { foreach (var e in Events) e.Raise(); }, "执行自定义事件 " + ModBase.GetUuid());
        }

    }

    #endregion

    /// <summary>
/// 自定义事件。
/// </summary>
    public class CustomEvent : DependencyObject
    {

        #region 属性与触发

        public EventType Type
        {
            get
            {
                var Value = EventType.None;
                ModBase.RunInUiWait(() => Value = (EventType)Conversions.ToInteger(GetValue(TypeProperty)));
                return Value;
            }
            set
            {
                SetValue(TypeProperty, value);
            }
        }
        public static readonly DependencyProperty TypeProperty = DependencyProperty.Register("Type", typeof(EventType), typeof(CustomEvent), new PropertyMetadata(EventType.None));

        public string Data
        {
            get
            {
                string Value = null;
                ModBase.RunInUiWait(() => Value = Conversions.ToString(GetValue(DataProperty)));
                return Value;
            }
            set
            {
                SetValue(DataProperty, value);
            }
        }
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register("Data", typeof(string), typeof(CustomEvent), new PropertyMetadata(null));

        public CustomEvent()
        {
        }
        public CustomEvent(EventType Type, string Data)
        {
            this.Type = Type;
            this.Data = Data;
        }

        /// <summary>
    /// 在当前线程中触发该自定义事件。
    /// </summary>
        public void Raise()
        {
            Raise(Type, Data);
        }

        #endregion

        public enum EventType
        {
            None = 0,
            打开网页,
            打开文件,
            打开帮助,
            执行命令,
            启动游戏,
            复制文本,
            刷新主页,
            刷新页面,
            刷新帮助,
            今日人品,
            内存优化,
            清理垃圾,
            弹出窗口,
            弹出提示,
            切换页面,
            导入整合包,
            安装整合包,
            下载文件,
            修改设置,
            写入设置,
            修改变量,
            写入变量,
            加入房间,
            检查更新
        }

        /// <summary>
    /// 在当前线程中触发一个自定义事件。
    /// </summary>
        public static void Raise(EventType Type, string Arg)
        {
            if (Type == EventType.None)
                return;
            Logger.Info($"执行自定义事件：{Type}, {Arg}");
            if (Arg is null)
                Arg = "";
            string[] Args = Arg.Split("|");
            string Arg0 = Arg.BeforeFirst("|");
            string Arg1 = Arg.AfterFirst("|");
            try
            {
                switch (Type)
                {
                    case EventType.打开网页:
                        {
                            Arg = Arg.Replace(@"\", "/");
                            if (!Arg.Contains("://") || Arg.StartsWithF("file", true)) // 为了支持更多协议（#2200）
                            {
                                ModMain.MyMsgBox("EventData 必须为一个网址。" + Constants.vbCrLf + "如果想要启动程序，请将 EventType 改为 打开文件。", "事件执行失败");
                                return;
                            }
                            ModMain.Hint("正在开启，请稍候：" + Arg);
                            ModBase.RunInThread(() => ModBase.OpenWebsite(Arg));
                            break;
                        }

                    case EventType.打开文件:
                    case EventType.打开帮助:
                    case EventType.执行命令:
                        {
                            ModBase.RunInThread(() =>
                {
                    try
                    {
                        // 确认实际路径
                        string[] ActualPaths = GetAbsoluteUrls(Arg0, Type);
                        string Location = ActualPaths[0];
                        string WorkingDir = ActualPaths[1];
                        Logger.Info($"打开类自定义事件实际路径：{Location}，工作目录：{WorkingDir}");
                        // 执行
                        if (Type == EventType.打开帮助)
                        {
                            PageOtherHelp.EnterHelpPage(Location);
                        }
                        else
                        {
                            if (!EventSafetyConfirm("即将执行：" + Location + (Args.Length >= 2 ? " " + Arg1 : "")))
                                return;
                            var Info = new ProcessStartInfo()
                            {
                                Arguments = Args.Length >= 2 ? Arg1 : "",
                                FileName = Location,
                                WorkingDirectory = WorkingDir
                            };
                            ModBase.StartProcess(Info);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "执行打开类自定义事件失败", LogBehavior.Alert);
                    }
                });
                            break;
                        }

                    case EventType.启动游戏:
                        {
                            if (Arg0 == @"\current")
                            {
                                if (ModMinecraft.McInstanceSelected is null)
                                {
                                    ModMain.Hint("请先选择一个 Minecraft 版本！", ModMain.HintType.Red);
                                    return;
                                }
                                else
                                {
                                    Arg0 = ModMinecraft.McInstanceSelected.Name;
                                }
                            }
                            ModBase.RunInUi(() => { if (ModLaunch.McLaunchStart(new ModLaunch.McLaunchOptions() { ServerIp = Args.Length >= 2 ? Args[1] : null, Instance = new ModMinecraft.McInstance(Arg0) })) { ModMain.Hint($"正在启动 {Arg0}……"); } });
                            break;
                        }

                    case EventType.复制文本:
                        {
                            ModBase.ClipboardSet(Arg);
                            break;
                        }

                    case EventType.刷新主页:
                        {
                            ModBase.RunInUi(() =>
                {
                    ModMain.FrmLaunchRight.ForceRefresh();
                    if (string.IsNullOrEmpty(Arg))
                        ModMain.Hint("已刷新！", ModMain.HintType.Green);
                });
                            break;
                        }

                    case EventType.刷新页面:
                        {
                            if (ModMain.FrmMain.PageRight is IRefreshable)
                            {
                                ModBase.RunInUiWait(() => ((IRefreshable)ModMain.FrmMain.PageRight).Refresh());
                                if (string.IsNullOrEmpty(Arg))
                                    ModMain.Hint("已刷新！", ModMain.HintType.Green);
                            }
                            else
                            {
                                ModMain.Hint("当前页面不支持刷新操作！", ModMain.HintType.Red);
                            }

                            break;
                        }

                    case EventType.刷新帮助:
                        {
                            ModBase.RunInUiWait(() => PageOtherLeft.RefreshHelp());
                            if (string.IsNullOrEmpty(Arg))
                                ModMain.Hint("已刷新！", ModMain.HintType.Green);
                            break;
                        }

                    case EventType.今日人品:
                        {
                            PageOtherTest.Jrrp();
                            break;
                        }

                    case EventType.内存优化:
                        {
                            ModBase.RunInThread(() => PageOtherTest.MemoryOptimize(true));
                            break;
                        }

                    case EventType.清理垃圾:
                        {
                            ModBase.RunInThread(() => PageOtherTest.RubbishClear());
                            break;
                        }

                    case EventType.弹出窗口:
                        {
                            if (Args.Length == 1)
                                throw new Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 弹窗标题|弹窗内容");
                            ModMain.MyMsgBox(Arg1.BeforeLast("|").Replace(@"\n", Constants.vbCrLf), Arg0.Replace(@"\n", Constants.vbCrLf), Args.Length > 2 ? Arg1.AfterLast("|") : "确定");
                            break;
                        }

                    case EventType.弹出提示:
                        {
                            ModMain.Hint(Arg0.Replace(@"\n", Constants.vbCrLf), Args.Length == 1 ? ModMain.HintType.Blue : EnumUtils.FromString<ModMain.HintType>(Arg1));
                            break;
                        }

                    case EventType.切换页面:
                        {
                            ModBase.RunInUi(() =>
                {
                    var TargetPage = EnumUtils.FromString<PageType>(Arg0);
                    if (TargetPage == PageType.InstanceSetup)
                    {
                        if (ModMinecraft.McInstanceSelected is null)
                        {
                            ModMain.Hint("请先选择一个 Minecraft 版本！", ModMain.HintType.Red);
                            return;
                        }
                        if (ModMinecraft.McInstanceLock.IsLocked(ModMinecraft.McInstanceSelected))
                        {
                            ModMain.Hint("该版本正在被修改，暂时无法进入版本设置！", ModMain.HintType.Red);
                            return;
                        }
                        PageInstanceLeft.Instance = ModMinecraft.McInstanceSelected;
                    }
                    ModMain.FrmMain.PageChange((PageStackData)TargetPage, Args.Length == 1 ? PageSubType.Default : EnumUtils.FromString<PageSubType>(Arg1));
                });
                            break;
                        }

                    case EventType.导入整合包:
                    case EventType.安装整合包:
                        {
                            ModBase.RunInUi(() => ModModpack.ModpackInstall());
                            break;
                        }

                    case EventType.下载文件:
                        {
                            Arg0 = Arg0.Replace(@"\", "/");
                            if (!(Arg0.StartsWithF("http://", true) || Arg0.StartsWithF("https://", true)))
                            {
                                ModMain.MyMsgBox("EventData 必须为以 http:// 或 https:// 开头的网址。" + Constants.vbCrLf + "PCL Next 不支持其他乱七八糟的下载协议。", "事件执行失败");
                                return;
                            }
                            if (!EventSafetyConfirm("即将从该网址下载文件：" + Constants.vbCrLf + Arg0))
                                return;
                            ModBase.RunInUi(() => { try { switch (Args.Length) { case 1: { PageOtherTest.StartCustomDownload(Arg0, PathUtils.GetLastPart(Arg0)); break; } case 2: { PageOtherTest.StartCustomDownload(Arg0, Arg1); break; } default: { PageOtherTest.StartCustomDownload(Arg0, Args[1], Args[2]); break; } } } catch { PageOtherTest.StartCustomDownload(Arg0, "未知"); } });
                            break;
                        }

                    case EventType.修改设置:
                    case EventType.写入设置:
                        {
                            if (Args.Length == 1)
                                throw new Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 UiLauncherTransparent|400");
                            Settings.SetSafe(Arg0, Args[1], Instance: ModMinecraft.McInstanceSelected);
                            if (Args.Length == 2)
                                ModMain.Hint($"已写入设置：{Arg0} → {Args[1]}", ModMain.HintType.Green);
                            break;
                        }

                    case EventType.修改变量:
                    case EventType.写入变量:
                        {
                            if (Args.Length == 1)
                                throw new Exception($"EventType {Type} 需要至少 2 个以 | 分割的参数，例如 VariableName|SomeValue");
                            try
                            {
                                RegistryUtils.Write($@"HKEY_CURRENT_USER\Software\{ModSecret.RegFolder}", $"CustomEvent{Arg0}", Args[1]);
                                if (Args.Length == 2)
                                    ModMain.Hint($"已写入变量：{Arg0} → {Args[1]}", ModMain.HintType.Green);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn(ex, $"写入变量失败：{Arg0} → {Args[1]}", LogBehavior.Toast);
                            }

                            break;
                        }

                    case EventType.加入房间:
                        {
                            ModBase.RunInUi(() => { if (string.IsNullOrWhiteSpace(Arg)) { PageLinkMain.Join(); } else { if (!EventSafetyConfirm("即将加入第三方联机房间。")) return; PageLinkMain.Join(Arg); } });
                            break;
                        }

                    case EventType.检查更新:
                        {
                            ModSecret.UpdateCheckByButton();
                            break;
                        }

                    default:
                        {
                            ModMain.MyMsgBox("未知的事件类型：" + ((int)Type).ToString() + Constants.vbCrLf + "请检查事件类型填写是否正确，或者 PCL Next 是否为最新版本。", "事件执行失败");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"事件执行失败（{Type}, {Arg}）", LogBehavior.Alert);
            }
        }

        /// <summary>
    /// 返回自定义事件的绝对 Url。实际返回 {绝对 Url, WorkingDir}。
    /// 失败会抛出异常。
    /// </summary>
        public static string[] GetAbsoluteUrls(string RelativeUrl, EventType Type)
        {

            // 网页确认
            if (RelativeUrl.StartsWithF("http", true))
            {
                if (ModBase.RunInUi())
                {
                    throw new Exception("能打开联网帮助页面的 MyListItem 必须手动设置 Title、Info 属性！");
                }
                // 获取文件名
                string RawFileName;
                try
                {
                    RawFileName = PathUtils.GetLastPart(RelativeUrl);
                    if (!(PathUtils.GetExtension(RawFileName) == "json"))
                        throw new Exception("未指向 .json 后缀的文件");
                }
                catch (Exception ex)
                {
                    throw new Exception("联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！" + Constants.vbCrLf + "例如：" + Constants.vbCrLf + " - https://www.baidu.com/test.json（填写这个路径）" + Constants.vbCrLf + " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", ex);
                }
                // 下载文件
                string LocalTemp = ModMain.RequestTaskTempFolder() + RawFileName;
                Logger.Info($"转换网络资源：{RelativeUrl} -> {LocalTemp}");
                try
                {
                    ModNet.NetDownloadByClient(RelativeUrl, LocalTemp);
                    ModNet.NetDownloadByClient(RelativeUrl.Replace(".json", ".xaml"), LocalTemp.Replace(".json", ".xaml"));
                }
                catch (Exception ex)
                {
                    throw new Exception("下载指定的文件失败！" + Constants.vbCrLf + "注意，联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！" + Constants.vbCrLf + "例如：" + Constants.vbCrLf + " - https://www.baidu.com/test.json（填写这个路径）" + Constants.vbCrLf + " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", ex);
                }
                RelativeUrl = LocalTemp;
            }
            RelativeUrl = RelativeUrl.Replace("/", @"\").Lower().TrimStart('\\');

            // 确认实际路径
            string Location;
            string WorkingDir = Paths.Base + "PCL";
            ModMain.HelpTryExtract();
            if (RelativeUrl.Contains(@":\"))
            {
                // 绝对路径
                Location = RelativeUrl;
                Logger.Info($"自定义事件中由绝对路径{Type}：{Location}");
            }
            else if (FileUtils.Exists(Paths.Base + @"PCL\" + RelativeUrl))
            {
                // 相对 PCL 文件夹的路径
                Location = Paths.Base + @"PCL\" + RelativeUrl;
                Logger.Info($"自定义事件中由相对 PCL 文件夹的路径{Type}：{Location}");
            }
            else if (FileUtils.Exists(Paths.Base + @"PCL\Help\" + RelativeUrl))
            {
                // 相对 PCL 本地帮助文件夹的路径
                Location = Paths.Base + @"PCL\Help\" + RelativeUrl;
                WorkingDir = Paths.Base + @"PCL\Help\";
                Logger.Info($"自定义事件中由相对 PCL 本地帮助文件夹的路径{Type}：{Location}");
            }
            else if (Type == EventType.打开帮助 && FileUtils.Exists(ModBase.PathTemp + @"Help\" + RelativeUrl))
            {
                // 相对 PCL 自带帮助文件夹的路径
                Location = ModBase.PathTemp + @"Help\" + RelativeUrl;
                WorkingDir = ModBase.PathTemp + @"Help\";
                Logger.Info($"自定义事件中由相对 PCL 自带帮助文件夹的路径{Type}：{Location}");
            }
            else if (Type == EventType.打开文件 || Type == EventType.执行命令)
            {
                // 直接使用原有路径启动程序
                Location = RelativeUrl;
                Logger.Info($"自定义事件中直接{Type}：{Location}");
            }
            else
            {
                // 打开帮助，但是格式不对劲
                throw new FileNotFoundException("未找到 EventData 指向的本地 xaml 文件：" + RelativeUrl, RelativeUrl);
            }

            return new[] { Location, WorkingDir };
        }

        /// <summary>
    /// 弹出安全确认弹窗。返回是否继续执行。
    /// </summary>
        private static bool EventSafetyConfirm(string Message)
        {
            if (Settings.Get<bool>("HintCustomCommand"))
                return true;
            switch (ModMain.MyMsgBox(Message + Constants.vbCrLf + "请在确认没有安全隐患后再继续。", "执行确认", "继续", "继续且今后不再要求确认", "取消"))
            {
                case 1:
                    {
                        return true;
                    }
                case 2:
                    {
                        Settings.Set("HintCustomCommand", true);
                        return true;
                    }

                default:
                    {
                        return false;
                    }
            }
        }

    }
}