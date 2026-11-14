using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MeloongCore;
using MeloongCore.Extensions;

namespace PCL
{
    public partial class PageInstanceBackup
    {

        private ModBackup.BackupContext Context;

        public PageInstanceBackup()
        {
            InitializeComponent();
            this.BtnBackupFull.Click += (p1, p2) => Backup_Click(ModBackup.BackupKind.FullZip, ModBackup.BackupScope.Full, "完整备份");
            this.BtnBackupModpack.Click += (p1, p2) => ImportPack_Click();
            this.BtnBackupIncremental.Click += (p1, p2) => Backup_Click(ModBackup.BackupKind.Incremental, ModBackup.BackupScope.Full, "增量备份");
            this.BtnOpenFolder.Click += (p1, p2) => OpenFolder_Click();
            this.BtnInitPack.Click += (p1, p2) => InitPack_Click();
            this.BtnRestoreInitial.Click += (p1, p2) => RestoreInitial_Click();
            this.Loaded += (_, __) =>
            {
                this.ChkSmartBackup.Checked = ModBackup.SmartBackupEnabled;
                RefreshAll();
            };
            this.ChkSmartBackup.Change += (_, __) =>
            {
                ModBackup.SmartBackupEnabled = this.ChkSmartBackup.Checked;
                ModMain.Hint(this.ChkSmartBackup.Checked ? "已开启智能备份。" : "已关闭智能备份：还原前不会再自动做兜底备份。", ModMain.HintType.Blue);
            };
        }

        /// <summary>刷新页面：当前实例、按钮可用性与备份列表。</summary>
        public void RefreshAll()
        {
            var Instance = PageInstanceLeft.Instance;
            if (Instance is null || this.PanList is null)
                return;
            try
            {
                Context = ModBackup.ContextFromInstance(Instance);
                this.LabInstance.Text = "当前实例：" + Instance.Name;
                this.LabBackupRoot.Text = "备份目录：" + Context.BackupRoot
                    + (Context.IsGlobalRoot ? "（该实例未开启版本隔离，完整备份包含全局游戏目录，但会排除其他实例的版本文件）" : "");
                bool CanIncremental = ModBackup.CanCreateIncremental(Context);
                this.BtnBackupIncremental.IsEnabled = CanIncremental;
                this.BtnRestoreInitial.IsEnabled = ModBackup.ListBackups(Context).Any(i => i.Kind == ModBackup.BackupKind.Initial);
                this.LabIncrementalHint.Text = CanIncremental
                    ? "增量备份以当前实例最近的初始基准 / 完整备份为对照，只保存发生变化的文件。"
                    : "当前实例还没有可用基准：请先做一次完整备份，或重新导入整合包以生成初始基准包。";
                RefreshList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "刷新实例备份页失败");
            }
        }

        #region 列表

        private void RefreshList()
        {
            if (Context is null)
                return;
            this.PanList.Children.Clear();
            var All = ModBackup.ListBackups(Context);
            this.LabEmpty.Visibility = All.Any() ? Visibility.Collapsed : Visibility.Visible;
            AddGroup("导入包备份", All.Where(i => i.Kind == ModBackup.BackupKind.Import));
            AddGroup("初始基准", All.Where(i => i.Kind == ModBackup.BackupKind.Initial));
            AddGroup("手动备份", All.Where(i => i.Kind == ModBackup.BackupKind.FullZip || i.Kind == ModBackup.BackupKind.Modpack || i.Kind == ModBackup.BackupKind.Incremental));
            AddGroup("还原前兜底", All.Where(i => i.Kind == ModBackup.BackupKind.PreRestore));
        }

        private void AddGroup(string Title, IEnumerable<ModBackup.BackupItem> Backups)
        {
            var List = Backups.ToList();
            if (!List.Any())
                return;
            this.PanList.Children.Add(new TextBlock()
            {
                Text = Title + "（" + List.Count + "）",
                FontSize = 13d,
                Opacity = 0.6d,
                Margin = new Thickness(0, 10, 0, 6)
            });
            foreach (var Backup in List)
                this.PanList.Children.Add(BuildListItem(Backup));
        }

        private MyListItem BuildListItem(ModBackup.BackupItem Backup)
        {
            var Item = new MyListItem()
            {
                Title = $"{Backup.KindTag}备份 · {Backup.CreatedAt:yyyy-MM-dd HH:mm}",
                Info = BuildInfo(Backup),
                Logo = ModBase.Logo.IconButtonSave,
                LogoScale = 0.9d,
                IsScaleAnimationEnabled = false,
                MinPaddingRight = 105,
                Tag = Backup
            };
            var Buttons = new List<MyIconButton>();
            var BtnRestore = new MyIconButton() { Logo = ModBase.Logo.IconButtonRefresh, LogoScale = 0.85d, Tag = Item, ToolTip = "还原到此备份" };
            BtnRestore.Click += (_, __) => Restore_Click((MyListItem)BtnRestore.Tag, EventArgs.Empty);
            Buttons.Add(BtnRestore);
            var BtnRemark = new MyIconButton() { Logo = ModBase.Logo.IconButtonEdit, LogoScale = 0.95d, Tag = Item, ToolTip = "编辑备注" };
            BtnRemark.Click += (_, __) => Remark_Click((MyListItem)BtnRemark.Tag, EventArgs.Empty);
            Buttons.Add(BtnRemark);
            if (!Backup.Locked)
            {
                var BtnDelete = new MyIconButton() { Logo = ModBase.Logo.IconButtonDelete, LogoScale = 0.95d, Tag = Item, ToolTip = "删除备份" };
                BtnDelete.Click += (_, __) => Delete_Click((MyListItem)BtnDelete.Tag, EventArgs.Empty);
                Buttons.Add(BtnDelete);
            }
            Item.Buttons = Buttons.ToArray();
            return Item;
        }

        private static string BuildInfo(ModBackup.BackupItem Backup)
        {
            var Parts = new List<string>()
            {
                StringUtils.FormatByteSize(Backup.Size),
                Backup.Files.Count + " 个文件"
            };
            if (!string.IsNullOrWhiteSpace(Backup.Remark))
                Parts.Add(Backup.Remark);
            if (Backup.IsIncremental && !string.IsNullOrWhiteSpace(Backup.BaseFile))
                Parts.Add("基准 " + Backup.BaseFile);
            if (Backup.Locked)
                Parts.Add("已锁定");
            return string.Join(" · ", Parts);
        }

        #endregion

        /// <summary>
        /// 把一项操作拆成多条子任务（阶段）：任务管理器里每条单独一行，
        /// 前一条完成后标为完成，再开始下一条，而不是一行名字反复改写。
        /// </summary>
        private void RunStages(string BaseName, List<(string Name, Action<ModLoader.LoaderTask<string, int>> Body)> Stages,
            ModMain.MyMsgProgressHandle Handle = null, Action<ModBase.LoadState> OnDone = null)
        {
            if (ModLoader.LoaderTaskbar.Any(l => (l.Name ?? "") == (BaseName ?? "")))
            {
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
                return;
            }
            var Tasks = new List<ModLoader.LoaderBase>();
            foreach (var Stage in Stages)
            {
                var Task = new ModLoader.LoaderTask<string, int>(Stage.Name, (Loader) =>
                {
                    Handle?.Report(-1d, Stage.Name);
                    var Progress = Loader.CreateSyncProgressProvider();
                    Progress.ProgressChanged += (Pair) => Handle?.Report(Pair.actual, null);
                    Stage.Body(Loader);
                });
                Tasks.Add(Task);
            }
            var Combo = new ModLoader.LoaderCombo<string>(BaseName, Tasks) { Show = true };
            Combo.OnStateChangedUi += (Loader, NewState, OldState) =>
            {
                if (NewState != ModBase.LoadState.Finished && NewState != ModBase.LoadState.Failed)
                    return;
                Handle?.Finish();
                OnDone?.Invoke(NewState);
            };
            Combo.Start();
            ModLoader.LoaderTaskbarAdd(Combo);
            ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
            ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
        }

        #region 新建备份

        private void Backup_Click(ModBackup.BackupKind Kind, ModBackup.BackupScope Scope, string DisplayName)
        {
            var Instance = PageInstanceLeft.Instance;
            if (Instance is null)
                return;
            if (ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该实例正在被修改，请稍后再备份！", ModMain.HintType.Red);
                return;
            }
            if (Kind == ModBackup.BackupKind.Incremental && !ModBackup.CanCreateIncremental(Context))
            {
                ModMain.Hint("当前实例还没有可用的基准备份，请先做一次完整备份。", ModMain.HintType.Red);
                return;
            }
            var Capture = Context;
            string Remark = ModMain.MyMsgBoxInput("备份备注", $"为即将创建的{DisplayName}写一句备注（可留空，会写进文件名）。", "", HintText: "例如：装完光影后、联机前", Button1: "开始备份");
            if (Remark is null)
                return;
            string InstanceName = Instance.Name;
            string BaseName = "备份实例：" + InstanceName;
            var Draft = default(ModBackup.BackupDraft);
            var Stages = new List<(string, Action<ModLoader.LoaderTask<string, int>>)>
            {
                ("① 收集需要备份的文件", Loader => Draft = ModBackup.DraftBackup(Capture, Kind, Scope, Remark.Trim(), false, null, Loader.CreateCancellationToken())),
                ("② 写入压缩包", Loader => ModBackup.CommitBackup(Draft, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider())),
                ("③ 写入备份清单", Loader => ModBackup.FinishBackup(Draft, Loader.CreateSyncProgressProvider()))
            };
            RunStages(BaseName, Stages, null, State =>
            {
                if (State != ModBase.LoadState.Finished)
                    return;
                ModMain.Hint("已创建" + DisplayName + "！", ModMain.HintType.Green);
                RefreshAll();
            });
        }

        /// <summary>
        /// 从当前实例现场生成一份「导入包」：只含整合包相关内容 + 版本/加载器信息。
        /// 还原时按它走普通导入流程，即先清空实例再重新导入安装。
        /// </summary>
        private void ImportPack_Click()
        {
            var Instance = PageInstanceLeft.Instance;
            if (Instance is null || Context is null)
                return;
            if (ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该实例正在被修改，请稍后再生成导入包！", ModMain.HintType.Red);
                return;
            }
            var Capture = Context;
            var CaptureInstance = Instance;
            string InstanceName = Instance.Name;
            string LoaderName = "生成导入包：" + InstanceName;
            if (ModLoader.LoaderTaskbar.Any(l => (l.Name ?? "") == (LoaderName ?? "")))
            {
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
                return;
            }
            var Task = new ModLoader.LoaderTask<string, string>(LoaderName, (Loader) =>
            {
                var Result = ModBackup.CreateImportPack(Capture, CaptureInstance, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider());
                Loader.Output = Result.FileName;
            });
            var Combo = new ModLoader.LoaderCombo<string>(LoaderName, new List<ModLoader.LoaderBase>() { Task }) { Show = true };
            Combo.OnStateChangedUi += (Loader, NewState, OldState) =>
            {
                if (NewState != ModBase.LoadState.Finished)
                    return;
                ModMain.Hint("已生成导入包备份！还原时会按它清空并重新导入当前实例。", ModMain.HintType.Green);
                RefreshAll();
            };
            Combo.Start(InstanceName);
            ModLoader.LoaderTaskbarAdd(Combo);
            ModMain.FrmMain.BtnExtraDownload.ShowRefresh();
        }

        /// <summary>把当前实例打包成初始基准包。</summary>
        private void InitPack_Click()
        {
            var Instance = PageInstanceLeft.Instance;
            if (Instance is null || Context is null)
                return;
            if (ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该实例正在被修改，请稍后再初始化整合包！", ModMain.HintType.Red);
                return;
            }
            var Capture = Context;
            string BaseName = "初始化整合包：" + Instance.Name;
            var Draft = default(ModBackup.BackupDraft);
            var Stages = new List<(string, Action<ModLoader.LoaderTask<string, int>>)>
            {
                ("① 收集需要打包的文件", Loader => Draft = ModBackup.DraftBackup(Capture, ModBackup.BackupKind.Initial, ModBackup.BackupScope.Full, "手动初始化的整合包基准", true, null, Loader.CreateCancellationToken())),
                ("② 写入压缩包", Loader => ModBackup.CommitBackup(Draft, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider())),
                ("③ 写入备份清单并锁定", Loader => ModBackup.FinishBackup(Draft, Loader.CreateSyncProgressProvider()))
            };
            RunStages(BaseName, Stages, null, State =>
            {
                if (State != ModBase.LoadState.Finished)
                    return;
                ModMain.Hint("初始基准包已生成，可用「还原到初始包」回到这个状态。", ModMain.HintType.Green);
                RefreshAll();
            });
        }

        /// <summary>用最新的初始基准包还原当前实例。</summary>
        private void RestoreInitial_Click()
        {
            var Instance = PageInstanceLeft.Instance;
            if (Instance is null || Context is null)
                return;
            if (ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该实例正在被修改，请稍后再还原！", ModMain.HintType.Red);
                return;
            }
            if (!ModBackup.ListBackups(Context).Any(i => i.Kind == ModBackup.BackupKind.Initial))
            {
                ModMain.Hint("该实例还没有初始基准包，请先点「初始化整合包」。", ModMain.HintType.Red);
                return;
            }
            if (ModMain.MyMsgBox("将用最新的初始基准包覆盖当前实例的全部内容，是否继续？", "还原到初始包", Button2: "取消", IsWarn: true) != 1)
                return;
            var Capture = Context;
            var CaptureInstance = Instance;
            string BaseName = "还原到初始包：" + Instance.Name;
            var Handle = ModMain.MyMsgProgress("正在还原到初始包", "准备中……",
                "只回放初始基准包里的文件，不会重新下载游戏内容；期间无法进行其他操作，完成后窗口会自动关闭。");
            var Initial = default(ModBackup.BackupItem);
            var Stages = new List<(string, Action<ModLoader.LoaderTask<string, int>>)>
            {
                ("① 查找最新的初始基准包", Loader =>
                {
                    Initial = ModBackup.FindInitial(Capture);
                    if (Initial is null)
                        throw new Exception("该实例还没有初始基准包，请先点「初始化整合包」。");
                }),
                ("② 创建还原前兜底备份", Loader => ModBackup.PreRestoreStep(Capture, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider())),
                ("③ 清空并回放初始基准包", Loader => ModBackup.RestoreSnapshotStep(Capture, Initial, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider()))
            };
            RunStages(BaseName, Stages, Handle, State =>
            {
                if (State != ModBase.LoadState.Finished)
                    return;
                ModMain.Hint("已还原到初始包！", ModMain.HintType.Green);
                try
                {
                    CaptureInstance.ResetSettingsCache();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "还原后重置实例设置缓存失败");
                }
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                RefreshAll();
            });
        }

        private void OpenFolder_Click()        {
            if (Context is null)
                return;
            try
            {
                DirectoryUtils.Create(Context.BackupRoot);
                ModBase.OpenExplorer(Context.BackupRoot);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "打开备份文件夹失败");
            }
        }

        #endregion

        #region 还原 / 备注 / 删除

        private void Restore_Click(MyListItem Sender, EventArgs e)
        {
            var Backup = (ModBackup.BackupItem)Sender.Tag;
            var Instance = PageInstanceLeft.Instance;
            if (Instance is null || Context is null || Backup is null)
                return;
            if (ModMinecraft.McInstanceLock.IsLocked(Instance))
            {
                ModMain.Hint("该实例正在被修改，请稍后再还原！", ModMain.HintType.Red);
                return;
            }
            bool IsImport = Backup.Kind == ModBackup.BackupKind.Import || !string.IsNullOrEmpty(Backup.PackFile);
            var Text = IsImport
                ? $"将使用导入包「{Backup.FileName}」重新安装当前实例 {Instance.Name}。" + "\r\n"
                    + "该实例现有的存档、Mod、配置会被全部清空，随后按整合包重新下载全部内容，" + "\r\n"
                    + "相当于把游戏删掉再装一遍，耗时取决于网速，中途不要进行其他操作。" + "\r\n" + "\r\n"
                    + "是否继续？"
                : $"将使用备份「{Backup.FileName}」覆盖当前实例 {Instance.Name} 的内容。" + "\r\n"
                    + "存档、Mod、配置等文件会被替换，还原前会自动生成一份兜底备份。" + "\r\n" + "\r\n"
                    + "是否继续？";
            if (ModMain.MyMsgBox(Text, IsImport ? "按导入包还原" : "还原确认", Button2: "取消", IsWarn: true) != 1)
                return;

            string LoaderName = "还原实例备份：" + Instance.Name;
            if (ModLoader.LoaderTaskbar.Any(l => (l.Name ?? "") == (LoaderName ?? "")))
            {
                ModMain.FrmMain.PageChange((FormMain.PageStackData)FormMain.PageType.TaskManager);
                return;
            }
            var CaptureContext = Context;
            var CaptureInstance = Instance;
            string BaseName = "还原实例：" + Instance.Name;
            // 不可关闭的进度弹窗：还原期间挡住所有交互；任务管理器里同一项操作拆成多条阶段任务
            var Handle = ModMain.MyMsgProgress("正在还原实例", "准备中……",
                IsImport
                    ? "将先清空该实例的全部内容，再按导入包重新下载安装；期间无法进行其他操作，完成后窗口会自动关闭。"
                    : "只回放备份里的文件，不会重新下载游戏内容；期间无法进行其他操作，完成后窗口会自动关闭。");
            var Stages = IsImport
                ? new List<(string, Action<ModLoader.LoaderTask<string, int>>)>
                {
                    ("① 清空实例内容", Loader => ModBackup.ClearForImportStep(CaptureContext, Backup, Loader.CreateCancellationToken())),
                    ("② 按导入包重新下载并安装", Loader => ModBackup.InstallForImportStep(CaptureContext, Backup, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider()))
                }
                : new List<(string, Action<ModLoader.LoaderTask<string, int>>)>
                {
                    ("① 创建还原前兜底备份", Loader => ModBackup.PreRestoreStep(CaptureContext, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider())),
                    ("② 清空并回放备份文件", Loader => ModBackup.RestoreSnapshotStep(CaptureContext, Backup, Loader.CreateCancellationToken(), Loader.CreateSyncProgressProvider()))
                };
            RunStages(BaseName, Stages, Handle, State =>
            {
                if (State != ModBase.LoadState.Finished)
                    return;
                ModMain.Hint("已还原到备份「" + Backup.FileName + "」！", ModMain.HintType.Green);
                try
                {
                    CaptureInstance.ResetSettingsCache();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "还原后重置实例设置缓存失败");
                }
                ModLoader.LoaderFolderRun(ModMinecraft.McInstanceListLoader, ModMinecraft.McFolderSelected, ModLoader.LoaderFolderRunType.ForceRun, MaxDepth: 1, ExtraPath: @"versions\");
                RefreshAll();
            });
        }
        private void Remark_Click(MyListItem Sender, EventArgs e)
        {
            var Backup = (ModBackup.BackupItem)Sender.Tag;
            if (Backup is null)
                return;
            string Result = ModMain.MyMsgBoxInput("备份备注", "为这份备份写一句备注，便于日后辨认。", Backup.Remark, HintText: "例如：装完光影后、联机前", Button1: "保存");
            if (Result is null)
                return;
            try
            {
                ModBackup.SetRemark(Backup, Result.Trim());
                RefreshList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "修改备份备注失败", LogBehavior.Alert);
            }
        }

        private void Delete_Click(MyListItem Sender, EventArgs e)
        {
            var Backup = (ModBackup.BackupItem)Sender.Tag;
            if (Backup is null)
                return;
            if (Backup.Locked)
            {
                ModMain.Hint("初始基准包已锁定，不能删除。", ModMain.HintType.Red);
                return;
            }
            if (ModMain.MyMsgBox($"确定要删除备份「{Backup.FileName}」吗？此操作不可撤销。", "删除备份", Button2: "取消", IsWarn: true) != 1)
                return;
            try
            {
                ModBackup.Delete(Backup);
                ModMain.Hint("已删除备份！", ModMain.HintType.Green);
                RefreshAll();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "删除备份失败", LogBehavior.Alert);
            }
        }

        #endregion

    }
}
