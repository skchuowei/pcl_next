using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json.Linq;

namespace PCL
{
    public partial class MyMsgLogin
    {
        private JObject Data;
        private string UserCode; // 需要用户在网页上输入的设备代码
        private string DeviceCode; // 用于轮询的设备代码
        private string Website; // 验证网页的网址
        private int PollInterval;

        #region 弹窗

        private readonly ModMain.MyMsgBoxConverter MyConverter;
        private readonly int Uuid = ModBase.GetUuid();

        public MyMsgLogin(ModMain.MyMsgBoxConverter Converter)
        {
            try
            {
                this.InitializeComponent();
            this.Btn1.Click += (p1, p2) => Btn1_Click();
            this.Btn3.Click += (p1, p2) => Btn3_Click();
            this.PanBorder.MouseLeftButtonDown += (p1, p2) => Drag((object)p1, (MouseButtonEventArgs)p2);
            this.LabTitle.MouseLeftButtonDown += (p1, p2) => Drag((object)p1, (MouseButtonEventArgs)p2);
                this.Btn1.Name = this.Btn1.Name + ModBase.GetUuid();
                this.Btn2.Name = this.Btn2.Name + ModBase.GetUuid();
                this.Btn3.Name = this.Btn3.Name + ModBase.GetUuid();
                MyConverter = Converter;
                this.ShapeLine.StrokeThickness = ModBase.GetWPFSize(1d);
                Data = (JObject)Converter.Content;
                Init();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "登录弹窗初始化失败", LogBehavior.Toast);
                if (MyConverter is not null)
                    Finished(new InvalidOperationException("无法初始化微软设备码登录。", ex));
            }

            base.Loaded += Load;
        }

        private void Load(object sender, EventArgs e)
        {
            try
            {
                // 动画
                this.Opacity = 0d;
                ModAnimation.AniStart(ModAnimation.AaColor(ModMain.FrmMain.PanMsg, Panel.BackgroundProperty, (MyConverter.IsWarn ? new ModBase.MyColor(140d, 80d, 0d, 0d) : new ModBase.MyColor(90d, 0d, 0d, 0d)) - ModMain.FrmMain.PanMsg.Background, 200), "PanMsg Background");
                ModAnimation.AniStart(new[] { ModAnimation.AaOpacity(this, 1d, 120, 60), ModAnimation.AaDouble(i => this.TransformPos.Y = Conversions.ToDouble(Conversions.ToDouble(this.TransformPos.Y) + Conversions.ToDouble(i)), -Conversions.ToDouble(this.TransformPos.Y), 300, 60, new ModAnimation.AniEaseOutBack(ModAnimation.AniEasePower.Weak)), ModAnimation.AaDouble(i => this.TransformRotate.Angle = Conversions.ToDouble(Conversions.ToDouble(this.TransformRotate.Angle) + Conversions.ToDouble(i)), -Conversions.ToDouble(this.TransformRotate.Angle), 300, 60, new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)) }, "MyMsgBox " + Uuid);
                // 记录日志
                Logger.Info($"登录弹窗：{this.LabTitle.Text}{"\r\n"}{this.LabCaption.Text}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "登录弹窗加载失败", LogBehavior.Toast);
            }
        }
        private void Close()
        {
            // 动画
            ModAnimation.AniStart(new[] { ModAnimation.AaCode(() => { if (!ModMain.WaitingMyMsgBox.Any()) { ModAnimation.AniStart(ModAnimation.AaColor(ModMain.FrmMain.PanMsg, Panel.BackgroundProperty, new ModBase.MyColor(0d, 0d, 0d, 0d) - ModMain.FrmMain.PanMsg.Background, 200, Ease: new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak))); } }, 30), ModAnimation.AaOpacity(this, -this.Opacity, 80, 20), ModAnimation.AaDouble(i => this.TransformPos.Y = Conversions.ToDouble(Conversions.ToDouble(this.TransformPos.Y) + Conversions.ToDouble(i)), 20d - Conversions.ToDouble(this.TransformPos.Y), 150, 0, new ModAnimation.AniEaseOutFluent()), ModAnimation.AaDouble(i => this.TransformRotate.Angle = Conversions.ToDouble(Conversions.ToDouble(this.TransformRotate.Angle) + Conversions.ToDouble(i)), 6d - Conversions.ToDouble(this.TransformRotate.Angle), 150, 0, new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)), ModAnimation.AaCode(() => ((Grid)this.Parent).Children.Remove(this), After: true) }, "MyMsgBox " + Uuid);
        }

        // 实现回车和 Esc 的接口（#4857）
        public void Btn1_Click()
        {
        }
        public void Btn3_Click()
        {
            Finished(new OperationCanceledException());
        }

        private void Drag(object sender, MouseButtonEventArgs e)
        {
            // VB 原文 Drag 方法开头是 On Error Resume Next，作用是吞掉拖拽窗口时的异常。
            // 这里唯一可能抛出的异常是 Window.DragMove 在鼠标左键未按下时抛出的 InvalidOperationException
            //（例如鼠标已在标题栏外释放），按原文行为静默忽略，其余异常照常上抛
            try
            {
                if (e.GetPosition(this.ShapeLine).Y <= 2d)
                    ModMain.FrmMain.DragMove();
            }
            catch (InvalidOperationException)
            {
                // 吞掉 InvalidOperationException：仅来自 Window.DragMove 在鼠标左键未按下时抛出，
                // 对应 VB 原文 On Error Resume Next 的静默忽略，其余异常类型不被吞掉
            }
        }

        #endregion

        private void Finished(object Result)
        {
            if (MyConverter.IsExited)
                return;
            MyConverter.IsExited = true;
            MyConverter.Result = Result;
            ModBase.RunInUi(Close);
            Thread.Sleep(200);
            ModMain.FrmMain.ShowWindowToTop();
        }

        private void Init()
        {
            UserCode = Data?["user_code"]?.ToString();
            DeviceCode = Data?["device_code"]?.ToString();
            Website = Data?["verification_uri"]?.ToString();
            if (string.IsNullOrWhiteSpace(UserCode) || string.IsNullOrWhiteSpace(DeviceCode) ||
                string.IsNullOrWhiteSpace(Website) ||
                !int.TryParse(Data?["interval"]?.ToString(), out PollInterval) ||
                PollInterval < 1 || PollInterval > int.MaxValue / 1000)
                throw new System.IO.InvalidDataException("微软设备码响应缺少有效的 user_code、device_code、verification_uri 或 interval。");
            // 设置 UI
            this.LabTitle.Text = "登录 Minecraft";
            this.LabCaption.Text = $"登录网页将自动开启，请在网页中输入 {UserCode}（已自动复制）。" + "\r\n" + "\r\n" + $"如果网络环境不佳，网页可能一直加载不出来，届时请使用加速器或 VPN 改善网络环境。" + "\r\n" + $"你也可以用其他设备打开 {Website} 并输入上述代码。";
            CustomEventService.SetEventData(this.Btn1, Website);
            CustomEventService.SetEventData(this.Btn2, UserCode);
            // 启动工作线程
            ModBase.RunInNewThread(WorkThread, "MyMsgLogin");
        }

        private void WorkThread()
        {
            Thread.Sleep(2000);
            if (MyConverter.IsExited)
                return;
            ModBase.OpenWebsite(Website);
            ModBase.ClipboardSet(UserCode);
            Thread.Sleep((PollInterval - 1) * 1000);
            // 轮询
            int UnknownFailureCount = 0;
            while (!MyConverter.IsExited)
            {
                try
                {
                    string Result = ModNet.NetRequestByClient("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", HttpMethod.Post, Content: "grant_type=urn:ietf:params:oauth:grant-type:device_code" + "&" + "client_id=" + ModSecret.OAuthClientId + "&" + "device_code=" + DeviceCode + "&" + "scope=XboxLive.signin%20offline_access", ContentType: "application/x-www-form-urlencoded", Timeout: 5000 + UnknownFailureCount * 5000, MakeLog: false, RequireJson: true);
                    // 获取结果
                    if (Result.DeserializeJson() is not JObject ResultJson)
                        throw new System.IO.InvalidDataException("微软令牌响应不是有效的 JSON 对象。");
                    string AccessToken = ResultJson["access_token"]?.ToString();
                    string RefreshToken = ResultJson["refresh_token"]?.ToString();
                    if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(RefreshToken))
                        throw new System.IO.InvalidDataException("微软令牌响应缺少 access_token 或 refresh_token。");
                    ModLaunch.McLaunchLog($"令牌过期时间：{ResultJson["expires_in"]} 秒");
                    ModMain.Hint("网页登录成功！", ModMain.HintType.Green);
                    Finished(new[] { AccessToken, RefreshToken });
                    return;
                }
                catch (Exception ex)
                {
                    if (ex is System.IO.InvalidDataException)
                    {
                        Finished(new Exception("微软设备码登录响应无效", ex));
                        return;
                    }
                    // 修改错误列表时，同时修改 ModLaunch.MsLoginStep1Refresh 中的对应代码
                    if (ex is HttpRequestCodeException)
                    {
                        string Response = ((HttpRequestCodeException)ex).Response ?? "";
                        if (Response.Contains("authorization_declined"))
                        {
                            Finished(new Exception("$你拒绝了 PCL Next 申请的权限……"));
                            return;
                        }
                        else if (Response.Contains("expired_token"))
                        {
                            Finished(new Exception("$登录用时太长啦，重新试试吧！"));
                            return;
                        }
                        else if (Response.Contains("Account security interrupt"))
                        {
                            Finished(new Exception("$该账号由于安全问题无法登陆，请前往微软账户页获取更多信息。"));
                            return;
                        }
                        else if (Response.Contains("service abuse"))
                        {
                            Finished(new Exception("$非常抱歉，该账号已被微软封禁，无法登录。"));
                            return;
                        }
                        else if (Response.Contains("AADSTS70000")) // 可能不能判 “invalid_grant”，见 #269
                        {
                            Finished(new ModBase.RestartException());
                            return;
                        }
                        else if (Response.Contains("authorization_pending"))
                        {
                            Thread.Sleep(2000);
                            continue;
                        }
                    }
                    if (UnknownFailureCount <= 2)
                    {
                        UnknownFailureCount += 1;
                        Logger.Warn(ex, $"登录轮询第 {UnknownFailureCount} 次失败");
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        Finished(new Exception("登录轮询失败", ex));
                        return;
                    }
                }
            }
        }

    }
}