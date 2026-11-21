using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeloongCore;
using MeloongCore.Wpf;

namespace PCL
{
    public partial class PageOtherTest
    {

        private ModNet.LoaderDownload DownloadTask;
        private DispatcherTimer StatusTimer;
        private string CurrentTarget;

        public PageOtherTest()
        {
            InitializeComponent();
            this.TextFolder.Text = GetDefaultDownloadFolder();
        }

        #region 自定义下载

        /// <summary>
        /// 从事件或命令行发起的自定义下载入口。
        /// </summary>
        public static void StartCustomDownload(string Url, string FileName, string Folder = null)
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                ModMain.Hint("自定义下载失败：下载地址不能为空。", ModMain.HintType.Red);
                return;
            }
            ModBase.RunInUi(() =>
            {
                try
                {
                    ModMain.FrmMain.PageChange(new FormMain.PageStackData { Page = FormMain.PageType.Other }, FormMain.PageSubType.OtherTest);
                    if (ModMain.FrmOtherTest is null)
                    {
                        ModMain.Hint("无法打开百宝箱页面，自定义下载已取消。", ModMain.HintType.Red);
                        return;
                    }
                    ModMain.FrmOtherTest.StartDownload(Url, FileName, Folder);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "打开自定义下载页面失败");
                    ModMain.Hint("无法打开百宝箱页面，自定义下载已取消。", ModMain.HintType.Red);
                }
            });
        }

        private void StartDownload(string Url, string FileName, string Folder)
        {
            this.TextUrl.Text = Url;
            if (!string.IsNullOrWhiteSpace(FileName))
                this.TextFileName.Text = FileName;
            if (!string.IsNullOrWhiteSpace(Folder))
                this.TextFolder.Text = Folder;
            BeginDownload();
        }

        private void BtnStartDownload_Click(object sender, MouseButtonEventArgs e)
        {
            BeginDownload();
        }

        private void BtnCancelDownload_Click(object sender, MouseButtonEventArgs e)
        {
            if (DownloadTask is null || DownloadTask.State != ModBase.LoadState.Loading)
                return;
            this.LabStatus.Text = "正在取消下载……";
            DownloadTask.Cancel();
        }

        private void BtnSelectFolder_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                string Folder = Dialogs.SelectFolder("选择下载文件的保存位置", false, this.TextFolder.Text).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(Folder))
                    this.TextFolder.Text = Folder;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "选择下载保存文件夹失败");
                ModMain.Hint("无法打开文件夹选择窗口。", ModMain.HintType.Red);
            }
        }

        private void BtnOpenFolder_Click(object sender, MouseButtonEventArgs e)
        {
            string Folder = string.IsNullOrWhiteSpace(CurrentTarget) ? this.TextFolder.Text.Trim() : Path.GetDirectoryName(CurrentTarget);
            if (string.IsNullOrEmpty(Folder) || !Directory.Exists(Folder))
            {
                ModMain.Hint("保存文件夹不存在。", ModMain.HintType.Blue);
                return;
            }
            ModBase.OpenExplorer(Folder);
        }

        private void BeginDownload()
        {
            if (DownloadTask is not null && DownloadTask.State == ModBase.LoadState.Loading)
            {
                ModMain.Hint("已有下载任务正在进行，请等待完成或先取消。", ModMain.HintType.Blue);
                return;
            }

            string Url = this.TextUrl.Text.Trim();
            if (!TryGetHttpUri(Url, out Uri Link))
            {
                ModMain.Hint("请输入以 http:// 或 https:// 开头的下载直链。", ModMain.HintType.Red);
                this.TextUrl.Focus();
                return;
            }

            string Folder = this.TextFolder.Text.Trim();
            if (string.IsNullOrWhiteSpace(Folder))
            {
                Folder = GetDefaultDownloadFolder();
                this.TextFolder.Text = Folder;
            }
            try
            {
                Directory.CreateDirectory(Folder);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "创建自定义下载文件夹失败");
                ModMain.Hint("无法创建保存文件夹：" + ex.Message, ModMain.HintType.Red);
                return;
            }

            string FileName = this.TextFileName.Text.Trim();
            if (string.IsNullOrWhiteSpace(FileName))
                FileName = GuessFileName(Link);
            FileName = SanitizeFileName(FileName);
            if (string.IsNullOrWhiteSpace(FileName))
            {
                ModMain.Hint("无法从链接中识别文件名，请手动填写。", ModMain.HintType.Red);
                this.TextFileName.Focus();
                return;
            }

            string Target = Path.Combine(Folder, FileName);
            if (File.Exists(Target))
            {
                if (ModMain.MyMsgBox($"目标位置已存在同名文件：{Environment.NewLine}{Target}{Environment.NewLine}{Environment.NewLine}是否覆盖？", "自定义下载", "覆盖", "取消") != 1)
                    return;
                try
                {
                    File.Delete(Target);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "覆盖已有文件失败");
                    ModMain.Hint("无法覆盖已有文件：" + ex.Message, ModMain.HintType.Red);
                    return;
                }
            }

            CurrentTarget = Target;
            Logger.Info($"[自定义下载] 开始：{Url} → {Target}");
            var FileTask = new ModNet.NetFile(new[] { Url }, Target) { LocalName = FileName };
            DownloadTask = new ModNet.LoaderDownload("自定义下载", new List<ModNet.NetFile> { FileTask });
            DownloadTask.OnStateChangedUi += DownloadTask_OnStateChangedUi;

            this.TextUrl.IsEnabled = false;
            this.TextFolder.IsEnabled = false;
            this.TextFileName.IsEnabled = false;
            this.BtnSelectFolder.IsEnabled = false;
            this.BtnStartDownload.Visibility = Visibility.Collapsed;
            this.BtnCancelDownload.Visibility = Visibility.Visible;
            this.BtnOpenFolder.Visibility = Visibility.Collapsed;
            this.LoadDownload.Visibility = Visibility.Visible;
            this.LoadDownload.Text = "正在下载 " + FileName;
            this.LoadDownload.State = DownloadTask;
            this.LabStatus.Text = "正在连接服务器……";
            StartStatusTimer();
            DownloadTask.Start();
        }

        private void DownloadTask_OnStateChangedUi(ModLoader.LoaderBase Loader, ModBase.LoadState NewState, ModBase.LoadState OldState)
        {
            if (NewState == ModBase.LoadState.Loading)
                return;
            StatusTimer?.Stop();
            this.LoadDownload.Visibility = Visibility.Collapsed;
            this.BtnCancelDownload.Visibility = Visibility.Collapsed;
            this.BtnStartDownload.Visibility = Visibility.Visible;
            this.TextUrl.IsEnabled = true;
            this.TextFolder.IsEnabled = true;
            this.TextFileName.IsEnabled = true;
            this.BtnSelectFolder.IsEnabled = true;

            switch (NewState)
            {
                case ModBase.LoadState.Finished:
                    {
                        string SizeText = "";
                        try
                        {
                            if (CurrentTarget is not null && File.Exists(CurrentTarget))
                                SizeText = $"（{StringUtils.FormatByteSize(new FileInfo(CurrentTarget).Length)}）";
                        }
                        catch
                        {
                        }
                        this.LabStatus.Text = $"下载完成{SizeText}：{CurrentTarget}";
                        ModMain.Hint("自定义下载完成。", ModMain.HintType.Green);
                        this.BtnOpenFolder.Visibility = Visibility.Visible;
                        break;
                    }
                case ModBase.LoadState.Failed:
                    {
                        string Message = Loader?.Error?.Message ?? "未知错误";
                        this.LabStatus.Text = "下载失败：" + Message;
                        ModMain.Hint("自定义下载失败：" + Message, ModMain.HintType.Red);
                        break;
                    }
                case ModBase.LoadState.Canceled:
                    {
                        this.LabStatus.Text = "下载已取消。";
                        ModMain.Hint("自定义下载已取消。", ModMain.HintType.Blue);
                        break;
                    }
            }
            DownloadTask = null;
        }

        private void StartStatusTimer()
        {
            if (StatusTimer is null)
            {
                StatusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                StatusTimer.Tick += (_, __) => RefreshDownloadStatus();
            }
            StatusTimer.Start();
        }

        private void RefreshDownloadStatus()
        {
            ModNet.LoaderDownload Task = DownloadTask;
            if (Task is null)
            {
                StatusTimer?.Stop();
                return;
            }
            ModNet.NetFile File = Task.Files.FirstOrDefault();
            if (File is null)
                return;
            long Done = File.DownloadDone;
            long Total = File.FileSize > 0L ? File.FileSize : -1L;
            long Speed = Math.Max(0L, File.Speed);
            string Progress = Total > 0L
                ? $"{StringUtils.FormatByteSize(Done)} / {StringUtils.FormatByteSize(Total)}"
                : StringUtils.FormatByteSize(Done);
            this.LabStatus.Text = $"{Progress}　　{StringUtils.FormatByteSize(Speed)}/s";
        }

        private static string GetDefaultDownloadFolder()
        {
            string Downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(Downloads))
                return Downloads;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCL Next 下载");
        }

        private static bool TryGetHttpUri(string Url, out Uri Link)
        {
            if (!System.Uri.TryCreate(Url, UriKind.Absolute, out Link))
                return false;
            return Link.Scheme == System.Uri.UriSchemeHttp || Link.Scheme == System.Uri.UriSchemeHttps;
        }

        private static string GuessFileName(Uri Uri)
        {
            string Name = Uri.UnescapeDataString(Path.GetFileName(Uri.LocalPath));
            return string.IsNullOrWhiteSpace(Name) ? "download.bin" : Name;
        }

        private static string SanitizeFileName(string Name)
        {
            if (string.IsNullOrWhiteSpace(Name))
                return "";
            Name = Name.Trim();
            foreach (char C in Path.GetInvalidFileNameChars())
                Name = Name.Replace(C, '_');
            Name = Name.Trim('.', ' ');
            if (Name == "." || Name == "..")
                return "";
            return Name;
        }

        #endregion

        public static void Jrrp()
        {
            ModMain.Hint("为便于维护，开源内容中不包含百宝箱功能……");
        }
        public static void RubbishClear()
        {
            ModMain.Hint("为便于维护，开源内容中不包含百宝箱功能……");
        }
        public static void MemoryOptimize(bool ShowHint)
        {
            if (ShowHint)
                ModMain.Hint("为便于维护，开源内容中不包含百宝箱功能……");
        }
        public static void MemoryOptimizeInternal(bool ShowHint)
        {
            if (ShowHint)
                ModMain.Hint("为便于维护，开源内容中不包含百宝箱功能……");
        }
        public static string GetRandomCave()
        {
            return "为便于维护，开源内容中不包含百宝箱功能……";
        }
        public static string GetRandomHint()
        {
            return "为便于维护，开源内容中不包含百宝箱功能……";
        }
        public static string GetRandomPresetHint()
        {
            return "为便于维护，开源内容中不包含百宝箱功能……";
        }

    }
}

