using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xaml;
using System.Xml.Linq;
using MeloongCore;
using MeloongCore.Extensions;
using MeloongCore.Wpf.Extensions;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using ThrottleDebounce;

namespace PCL
{

    public static class ModBase
    {

        #region 声明

        // 下列版本信息由更新器自动修改
        public const string VersionBaseName = "2.100.0.6"; // 显示用版本名
        public const string CommitHash = ""; // Commit Hash，由 GitHub Workflow 自动替换
                                             // 版本号按《分支与发布规范》§5.2 的号段表分配，必须全局单调递增，否则会误触发降级：
                                             // 周期基址 base = 5000 + 100 × 周期序号（周期 1 = 2.100 → 5000）
                                             // dev = base+0…9，slush = base+10…19，alpha = base+20…59，beta = base+60…89
                                             // PC = base+90…94，SP = base+95…99
                                             // 号段按代号阶梯严格递增，且每个代号都占独立区间——
                                             // dev 与 slush 若共用同一号段，切换时代会被判成版本倒退而误弹降级提示。
                                             // 本值由 scripts\Set-VersionInfo.ps1 依据标签写入，不可手工填写。
        public const int VersionCode = 5006;

        // 版本信息
        /// <summary>
    /// 显示用版本信息，例如「内测版 2.100.0.1」。
    /// SP 渠道额外标注 SP 序号，例如「正式版 2.100.1.0（SP1）」。
    /// </summary>
        public static string VersionDisplay
        {
            get
            {
                return BuildTypeDisplay + " " + VersionBaseName + ModBuildChannel.ChannelSpSuffix;
            }
        }
        // 渠道（发布通道）由 CI 按分支注入编译期常量决定，
        // 详见 ModBuildChannel 与《分支与发布规范》§5.4。
        // 渠道由 csproj 的 PclChannel → PclChannelConstant 注入 DefineConstants（见 pcl-next.csproj）。
        // 原 VB 用 #If PCL_CHANNEL_ALPHA … #Else 链，转换器把整段打平只剩 dev 分支，
        // 会导致 -p:PclChannel=Alpha 编出来的程序仍标成「开发版」；此处按 C# 预处理器还原完整链路。
#if PCL_CHANNEL_ALPHA
        public const BuildTypes BuildType = BuildTypes.Alpha;
#elif PCL_CHANNEL_BETA
        public const BuildTypes BuildType = BuildTypes.Beta;
#elif PCL_CHANNEL_RC
        public const BuildTypes BuildType = BuildTypes.Release;
#elif PCL_CHANNEL_SNAPSHOT
        public const BuildTypes BuildType = BuildTypes.Snapshot;
#elif PCL_CHANNEL_SLUSH
        public const BuildTypes BuildType = BuildTypes.Slush;
#else
        public const BuildTypes BuildType = BuildTypes.Debug;
#endif
        public const string BuildTypeDisplay = ModBuildChannel.ChannelDisplay;
        public enum BuildTypes
        {
            Debug = 100,
            Slush = 90,
            Alpha = 80,
            Beta = 70,
            Release = 50,
            Snapshot = 0
        }

        /// <summary>
    /// 主窗口句柄。
    /// </summary>
        public static nint Handle;
        /// <summary>
    /// 包含程序名的完整路径。
    /// </summary>
        public static string PathExe = Paths.Base + Path.GetFileName(Environment.ProcessPath);
        /// <summary>
    /// 程序内嵌图片文件夹路径，以 / 结尾。
    /// </summary>
        public static string PathImage = "pack://application:,,,/pcl-next;component/Images/";
        /// <summary>
    /// 程序的缓存文件夹路径，以 \ 结尾。
    /// </summary>
        public static string PathTemp = PathUtils.AddSlashSuffix((string.IsNullOrEmpty(Settings.Get<string>("SystemSystemCache")) ? Path.GetTempPath() + @"PCL\" : Settings.Get<string>("SystemSystemCache")).Replace("/", @"\"));
        /// <summary>
    /// 当前程序的语言。
    /// </summary>
        public static string Lang = "zh_CN";
        /// <summary>
    /// 程序的打开计时。
    /// </summary>
        public static long ApplicationStartTick = GetTimeMs();
        /// <summary>
    /// 程序打开时的时间。
    /// </summary>
        public static DateTime ApplicationOpenTime = DateTime.Now;
        /// <summary>
    /// 识别码。
    /// </summary>
        public static string Identify = ModSecret.GetIdentify();
        /// <summary>
    /// 程序是否正在结束。
    /// </summary>
        public static bool IsProgramEnding = false;
        /// <summary>
    /// 是否使用 GBK 编码。
    /// </summary>
        public static bool IsGBKEncoding = Encoding.Default.CodePage == 936;
        /// <summary>
    /// 系统盘盘符，以 \ 结尾。例如 “C:\”。
    /// </summary>
        public static string OsDrive = Environment.GetLogicalDrives().First(p => Directory.Exists(p)); // #3799

        public static bool ModeDebug
        {
            get
            {
                return Settings.Get<bool>("SystemDebugMode");
            }
        }

        #endregion

        #region 矢量图标

        public class Logo
        {
            /// <summary>
        /// 图标按钮，心（空心），1.1x
        /// </summary>
            public const string IconButtonLikeLine = "M512 896a42.666667 42.666667 0 0 1-30.293333-12.373333l-331.52-331.946667a224.426667 224.426667 0 0 1 0-315.733333 223.573333 223.573333 0 0 1 315.733333 0L512 282.026667l46.08-46.08a223.573333 223.573333 0 0 1 315.733333 0 224.426667 224.426667 0 0 1 0 315.733333l-331.52 331.946667A42.666667 42.666667 0 0 1 512 896zM308.053333 256a136.533333 136.533333 0 0 0-97.28 40.106667 138.24 138.24 0 0 0 0 194.986666L512 792.746667l301.226667-301.653334a138.24 138.24 0 0 0 0-194.986666 141.653333 141.653333 0 0 0-194.56 0l-76.373334 76.8a42.666667 42.666667 0 0 1-60.586666 0L405.333333 296.106667A136.533333 136.533333 0 0 0 308.053333 256z";
            /// <summary>
        /// 图标按钮，心（实心），1.1x
        /// </summary>
            public const string IconButtonLikeFill = "M700.856 155.543c-74.769 0-144.295 72.696-190.046 127.26-45.737-54.576-115.247-127.26-190.056-127.26-134.79 0-244.443 105.78-244.443 235.799 0 77.57 39.278 131.988 70.845 175.713C238.908 694.053 469.62 852.094 479.39 858.757c9.41 6.414 20.424 9.629 31.401 9.629 11.006 0 21.998-3.215 31.398-9.63 9.782-6.662 240.514-164.703 332.238-291.701 31.587-43.724 70.874-98.143 70.874-175.713-0.001-130.02-109.656-235.8-244.445-235.8z m0 0";
            /// <summary>
        /// 图标按钮，垃圾桶，1.1x
        /// </summary>
            public const string IconButtonDelete = "M520.192 0C408.43 0 317.44 82.87 313.563 186.734H52.736c-29.038 0-52.663 21.943-52.663 49.079s23.625 49.152 52.663 49.152h58.075v550.473c0 103.35 75.118 187.757 167.717 187.757h472.43c92.599 0 167.716-83.894 167.716-187.757V285.477h52.59c29.038 0 52.59-21.943 52.663-49.08-0.073-27.135-23.625-49.151-52.663-49.151H726.235C723.237 83.017 631.955 0 520.192 0zM404.846 177.957c3.803-50.03 50.176-89.015 107.447-89.015 57.197 0 103.57 38.985 106.788 89.015H404.92zM284.379 933.669c-33.353 0-69.997-39.351-69.997-95.525v-549.01H833.39v549.522c0 56.247-36.645 95.525-69.998 95.525H284.379v-0.512z M357.23 800.695a48.274 48.274 0 0 0 47.616-49.006V471.7a48.274 48.274 0 0 0-47.543-49.08 48.274 48.274 0 0 0-47.69 49.006V751.69c0 27.282 20.846 49.006 47.617 49.006z m166.62 0a48.274 48.274 0 0 0 47.688-49.006V471.7a48.274 48.274 0 0 0-47.689-49.08 48.274 48.274 0 0 0-47.543 49.006V751.69c0 27.282 21.431 49.006 47.543 49.006z m142.92 0a48.274 48.274 0 0 0 47.543-49.006V471.7a48.274 48.274 0 0 0-47.543-49.08 48.274 48.274 0 0 0-47.616 49.006V751.69c0 27.282 20.773 49.006 47.543 49.006z";
            /// <summary>
        /// 图标按钮，禁止，1x
        /// </summary>
            public const string IconButtonStop = "M508 990.4c-261.6 0-474.4-212-474.4-474.4S246.4 41.6 508 41.6s474.4 212 474.4 474.4S769.6 990.4 508 990.4zM508 136.8c-209.6 0-379.2 169.6-379.2 379.2 0 209.6 169.6 379.2 379.2 379.2s379.2-169.6 379.2-379.2C887.2 306.4 717.6 136.8 508 136.8zM697.6 563.2 318.4 563.2c-26.4 0-47.2-21.6-47.2-47.2 0-26.4 21.6-47.2 47.2-47.2l379.2 0c26.4 0 47.2 21.6 47.2 47.2C744.8 542.4 724 563.2 697.6 563.2z";
            /// <summary>
        /// 图标按钮，勾选，1x
        /// </summary>
            public const string IconButtonCheck = "M512 0a512 512 0 1 0 512 512A512 512 0 0 0 512 0z m0 921.6a409.6 409.6 0 1 1 409.6-409.6 409.6 409.6 0 0 1-409.6 409.6z M716.8 339.968l-256 253.44L328.192 460.8A51.2 51.2 0 0 0 256 532.992l168.448 168.96a51.2 51.2 0 0 0 72.704 0l289.28-289.792A51.2 51.2 0 0 0 716.8 339.968z";
            /// <summary>
        /// 图标按钮，笔，1x
        /// </summary>
            public const string IconButtonEdit = "M732.64 64.32C688.576 21.216 613.696 21.216 569.6 64.32L120.128 499.52c-17.6 12.896-26.432 30.144-30.848 51.68L32 870.048c0 25.856 8.8 56 26.432 73.248 17.632 17.216 17.632 48.704 88.64 48.704h13.248l326.08-56c22.016-4.32 39.68-12.928 52.864-30.176l449.472-435.2c22.048-21.536 35.264-47.36 35.264-77.536 0-30.176-13.216-56-35.264-77.568l-256.096-251.2zM139.712 903.776l56-326.912 311.04-295.136 267.104 269.44-310.976 295.168-323.168 57.44zM844.576 467.84l-273.984-260.672 61.856-59.84c8.832-8.512 26.528-8.512 39.776 0l234.24 226.496c4.384 4.288 8.832 12.8 8.832 17.088s-4.416 8.544-8.864 12.8l-61.856 64.128z";
            /// <summary>
        /// 图标按钮，齿轮，1.1x
        /// </summary>
            public const string IconButtonSetup = "M651.946667 1001.813333c-22.186667 0-42.666667-10.24-61.44-27.306666-23.893333-23.893333-49.493333-35.84-75.093334-35.84-29.013333 0-56.32 11.946667-73.386666 30.72v3.413333c-17.066667 17.066667-42.666667 27.306667-66.56 27.306667h-6.826667c-6.826667 0-11.946667-1.706667-15.36-1.706667l-6.826667-1.706667c-64.853333-20.48-121.173333-54.613333-168.96-98.986666-29.013333-23.893333-37.546667-63.146667-25.6-95.573334 8.533333-23.893333 5.12-51.2-10.24-75.093333-15.36-27.306667-34.133333-40.96-59.733333-47.786667h-1.706667l-5.12-1.706666c-35.84-8.533333-61.44-34.133333-66.56-69.973334C1.706667 575.146667 0 537.6 0 512c0-32.426667 3.413333-63.146667 8.533333-93.866667v-6.826666l3.413334-8.533334c10.24-23.893333 23.893333-40.96 44.373333-51.2 5.12-3.413333 11.946667-6.826667 20.48-8.533333 27.306667-8.533333 51.2-25.6 63.146667-44.373333 13.653333-23.893333 17.066667-52.906667 10.24-81.92-11.946667-34.133333 0-71.68 30.72-93.866667 44.373333-37.546667 97.28-68.266667 158.72-93.866667l3.413333-1.706666c44.373333-13.653333 75.093333 3.413333 92.16 20.48 23.893333 23.893333 49.493333 35.84 75.093333 35.84 30.72 0 56.32-10.24 71.68-30.72l3.413334-3.413334c27.306667-27.306667 63.146667-35.84 93.866666-22.186666 63.146667 22.186667 117.76 54.613333 165.546667 97.28 29.013333 23.893333 37.546667 63.146667 25.6 95.573333-8.533333 23.893333-5.12 51.2 10.24 75.093333 15.36 27.306667 34.133333 40.96 59.733333 47.786667h1.706667l5.12 1.706667c35.84 8.533333 61.44 34.133333 66.56 71.68 6.826667 30.72 10.24 63.146667 11.946667 93.866666v3.413334c0 32.426667-3.413333 63.146667-8.533334 93.866666v6.826667l-3.413333 8.533333c-10.24 23.893333-23.893333 40.96-44.373333 51.2-5.12 3.413333-11.946667 6.826667-20.48 8.533334-27.306667 8.533333-51.2 25.6-63.146667 46.08-13.653333 23.893333-17.066667 52.906667-10.24 81.92 11.946667 35.84-1.706667 75.093333-30.72 95.573333-44.373333 35.84-95.573333 66.56-157.013333 92.16-15.36 3.413333-27.306667 3.413333-35.84 3.413333z m3.413333-83.626666z m1.706667 0zM517.12 853.333333c47.786667 0 93.866667 20.48 134.826667 59.733334 1.706667 1.706667 3.413333 1.706667 3.413333 3.413333 52.906667-22.186667 97.28-49.493333 136.533333-80.213333l1.706667-1.706667v-3.413333c-13.653333-52.906667-8.533333-104.106667 17.066667-148.48 23.893333-39.253333 64.853333-69.973333 114.346666-85.333334 1.706667 0 3.413333-1.706667 6.826667-6.826666 5.12-25.6 8.533333-51.2 8.533333-78.506667-1.706667-29.013333-3.413333-56.32-10.24-81.92v-5.12h-1.706666c-51.2-11.946667-90.453333-39.253333-119.466667-87.04-27.306667-44.373333-34.133333-100.693333-17.066667-148.48l-1.706666-1.706667h-3.413334c-39.253333-35.84-85.333333-63.146667-136.533333-80.213333H648.533333s-1.706667 1.706667-3.413333 1.706667c-32.426667 39.253333-80.213333 59.733333-136.533333 59.733333-47.786667 0-93.866667-20.48-134.826667-59.733333l-1.706667-1.706667h-1.706666c-54.613333 22.186667-98.986667 49.493333-136.533334 80.213333l-1.706666 1.706667v3.413333c13.653333 52.906667 8.533333 104.106667-17.066667 148.48-23.893333 39.253333-64.853333 69.973333-114.346667 85.333334-1.706667 0-3.413333 1.706667-6.826666 6.826666-6.826667 25.6-8.533333 51.2-8.533334 78.506667 0 30.72 3.413333 58.026667 6.826667 76.8l1.706667 5.12h1.706666c51.2 11.946667 90.453333 39.253333 119.466667 87.04 27.306667 44.373333 34.133333 100.693333 17.066667 148.48l1.706666 1.706667 1.706667 1.706666c37.546667 35.84 83.626667 63.146667 134.826667 80.213334 1.706667 0 3.413333 0 3.413333 1.706666h1.706667s1.706667 0 5.12-1.706666c34.133333-37.546667 81.92-59.733333 136.533333-59.733334z m-6.826667-146.773333c-110.933333 0-199.68-85.333333-199.68-196.266667 0-109.226667 87.04-196.266667 199.68-196.266666s199.68 85.333333 199.68 196.266666c-1.706667 109.226667-88.746667 196.266667-199.68 196.266667z m0-307.2c-63.146667 0-114.346667 49.493333-114.346666 110.933333 0 63.146667 49.493333 110.933333 114.346666 110.933334 30.72 0 59.733333-11.946667 80.213334-32.426667 20.48-20.48 32.426667-49.493333 32.426666-78.506667 0-63.146667-49.493333-110.933333-112.64-110.933333z";
            /// <summary>
        /// 图标按钮，重置，0.9x
        /// </summary>
            public const string IconButtonReset = "M667.6817627 313.65283203l-45.28564454 55.76660156L858.06933594 391.27124023 787.61950684 165.93066406l-56.01379395 69.01611328A354.47387695 354.47387695 0 0 0 520.89892578 165.93066406C324.87536621 165.93066406 165.93066406 324.43041992 165.93066406 519.91015625c0 195.52917481 158.94470215 353.97949219 354.96826172 353.97949219a355.06713867 355.06713867 0 0 0 331.73217774-227.66418458 50.52612305 50.52612305 0 0 0-29.21813966-65.25878905 50.77331543 50.77331543 0 0 0-65.50598144 29.16870117A253.61938477 253.61938477 0 0 1 520.94836426 772.78796387c-140.05920411 0-253.61938477-113.21411133-253.61938477-252.87780762 0-139.61425781 113.56018067-252.82836914 253.61938477-252.82836914 53.59130859 0 104.46350098 16.61132813 146.73339843 46.57104492";
            /// <summary>
        /// 图标按钮，刷新，0.85x
        /// </summary>
            public const string IconButtonRefresh = "M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z";
            /// <summary>
        /// 图标按钮，软盘，1x
        /// </summary>
            public const string IconButtonSave = "M819.392 0L1024 202.752v652.16a168.96 168.96 0 0 1-168.832 168.768h-104.192a47.296 47.296 0 0 1-10.752 0H283.776a47.232 47.232 0 0 1-10.752 0H168.832A168.96 168.96 0 0 1 0 854.912V168.768A168.96 168.96 0 0 1 168.832 0h650.56z m110.208 854.912V242.112l-149.12-147.776H168.896c-41.088 0-74.432 33.408-74.432 74.432v686.144c0 41.024 33.344 74.432 74.432 74.432h62.4v-190.528c0-33.408 27.136-60.544 60.544-60.544h440.448c33.408 0 60.544 27.136 60.544 60.544v190.528h62.4c41.088 0 74.432-33.408 74.432-74.432z m-604.032 74.432h372.864v-156.736H325.568v156.736z m403.52-596.48a47.168 47.168 0 1 1 0 94.336H287.872a47.168 47.168 0 1 1 0-94.336h441.216z m0-153.728a47.168 47.168 0 1 1 0 94.4H287.872a47.168 47.168 0 1 1 0-94.4h441.216z";
            /// <summary>
        /// 图标按钮，信息，1.05x
        /// </summary>
            public const string IconButtonInfo = "M512 917.333333c223.861333 0 405.333333-181.472 405.333333-405.333333S735.861333 106.666667 512 106.666667 106.666667 288.138667 106.666667 512s181.472 405.333333 405.333333 405.333333z m0 106.666667C229.226667 1024 0 794.773333 0 512S229.226667 0 512 0s512 229.226667 512 512-229.226667 512-512 512z m-32-597.333333h64a21.333333 21.333333 0 0 1 21.333333 21.333333v320a21.333333 21.333333 0 0 1-21.333333 21.333333h-64a21.333333 21.333333 0 0 1-21.333333-21.333333V448a21.333333 21.333333 0 0 1 21.333333-21.333333z m0-192h64a21.333333 21.333333 0 0 1 21.333333 21.333333v64a21.333333 21.333333 0 0 1-21.333333 21.333333h-64a21.333333 21.333333 0 0 1-21.333333-21.333333v-64a21.333333 21.333333 0 0 1 21.333333-21.333333z";
            /// <summary>
        /// 图标按钮，列表，1x
        /// </summary>
            public const string IconButtonList = "M384 128h640v128H384zM160 192m-96 0a96 96 0 1 0 192 0 96 96 0 1 0-192 0ZM384 448h640v128H384zM160 512m-96 0a96 96 0 1 0 192 0 96 96 0 1 0-192 0ZM384 768h640v128H384zM160 832m-96 0a96 96 0 1 0 192 0 96 96 0 1 0-192 0Z";
            /// <summary>
        /// 图标按钮，文件夹，1.1x
        /// </summary>
            public const string IconButtonOpen = "M889.018182 418.909091H884.363636V316.509091a93.090909 93.090909 0 0 0-99.607272-89.832727h-302.545455l-93.090909-76.334546A46.545455 46.545455 0 0 0 358.865455 139.636364H146.152727A93.090909 93.090909 0 0 0 46.545455 229.469091V837.818182a46.545455 46.545455 0 0 0 46.545454 46.545454 46.545455 46.545455 0 0 0 16.756364-3.258181 109.381818 109.381818 0 0 0 25.134545 3.258181h586.472727a85.178182 85.178182 0 0 0 87.04-63.301818l163.374546-302.545454a46.545455 46.545455 0 0 0 5.585454-21.876364A82.385455 82.385455 0 0 0 889.018182 418.909091z m-744.727273-186.181818h198.283636l93.09091 76.334545a46.545455 46.545455 0 0 0 29.323636 10.705455h319.301818a12.101818 12.101818 0 0 1 6.516364 0V418.909091H302.545455a85.178182 85.178182 0 0 0-87.04 63.301818L139.636364 622.778182V232.727273a19.549091 19.549091 0 0 1 6.516363 0z m578.094546 552.029091a27.461818 27.461818 0 0 0-2.792728 6.516363H154.530909l147.083636-272.290909a27.461818 27.461818 0 0 0 2.792728-6.981818h565.061818z";
            /// <summary>
        /// 图标按钮，上箭头，0.95x
        /// </summary>
            public const string IconButtonArrowUp = "M554 333V853h-85V333l-228 228-60-60L512 170l331 331-60 60L554 333z";
            /// <summary>
        /// 图标按钮，下箭头，0.95x
        /// </summary>
            public const string IconButtonArrowDown = "M554 691V171h-85V691L241 463l-60 60L512 854l331-331-60-60L554 691z";
            /// <summary>
        /// 图标按钮，名片，1.1x
        /// </summary>
            public const string IconButtonCard = "M834.5 684.1c-31.2-70.4-98.9-120.9-179.1-127.3 63.5-8.5 112.6-63 112.6-128.8 0-71.8-58.2-130-130-130s-130 58.2-130 130c0 65.9 49 120.3 112.6 128.8-80.2 6.4-148 57-179.1 127.3-8.7 19.7 6 42 27.6 42 12.1 0 22.7-7.5 27.7-18.5 24.3-53.9 78.5-91.5 141.3-91.5s117 37.6 141.3 91.5c5 11.1 15.6 18.5 27.7 18.5 21.4 0 36.1-22.3 27.4-42zM567.9 427.9c0-38.6 31.4-70 70-70s70 31.4 70 70-31.4 70-70 70-70-31.4-70-70zM460.3 347.9H216.9c-16.6 0-30 13.4-30 30s13.4 30 30 30h243.3c16.6 0 30-13.4 30-30 0.1-16.5-13.4-30-29.9-30zM367.4 459.6H216.9c-16.6 0-30 13.4-30 30s13.4 30 30 30h150.4c16.6 0 30-13.4 30-30 0.1-16.6-13.4-30-29.9-30zM297.4 571.2H217c-16.6 0-30 13.4-30 30s13.4 30 30 30h80.4c16.6 0 30-13.4 30-30 0-16.5-13.5-30-30-30zM900 236v552H124V236h776m0-60H124c-33.1 0-60 26.9-60 60v552c0 33.1 26.9 60 60 60h776c33.1 0 60-26.9 60-60V236c0-33.1-26.9-60-60-60z";
            /// <summary>
        /// 图标按钮，×，0.85x
        /// </summary>
            public const string IconButtonCross = "F1 M 26.9166,22.1667L 37.9999,33.25L 49.0832,22.1668L 53.8332,26.9168L 42.7499,38L 53.8332,49.0834L 49.0833,53.8334L 37.9999,42.75L 26.9166,53.8334L 22.1666,49.0833L 33.25,38L 22.1667,26.9167L 26.9166,22.1667 Z";
            /// <summary>
        /// 图标按钮，Mojang，1.1x
        /// </summary>
            public const string IconButtonMojang = "M9.183,18.967c-0.109-2.239,1.336-5.119,3.92-4.96c3.657-0.027,7.319-0.044,10.977,0.005	c3.712,0.214,6.596,2.759,9.652,4.533c0.181-1.697-0.197-3.717,1.237-4.982c1.899,2.091,3.143,4.894,5.677,6.334	c1.577,0.805,2.973-0.668,4.221-1.44c1.55,2.305,2.108,5.075,2.622,7.752c0.657,3.438,0.947,6.925,1.111,10.413	c-1.734-0.733-3.355-1.708-4.971-2.671c-1.396-4.933-5.349-8.656-9.723-11.059c-3.46-1.817-7.752-2.185-11.338-0.52	c-3.761,1.593-6.285,5.666-5.891,9.75c0.121,4.577,3.17,8.765,7.391,10.456c7.746,3.285,16.407,2.234,24.521,1.243	c0.454,2.315-1.511,4.527-3.695,4.889c-10.577,0.038-21.148-0.011-31.725,0.022c-2.217,0.279-4.079-1.879-3.964-4.008	C9.167,36.141,9.216,27.556,9.183,18.967z M40.114,8.09c0.294-0.801,0.872-1.417,1.542-1.924c1.379,2.589,2.742,5.482,2.311,8.491	c-0.332,2.54-4.42,2.507-5.052,0.163C38.349,12.527,39.209,10.178,40.114,8.09z M0,53 l0.1,0 z";
            /// <summary>
        /// 图标按钮，用户，0.95x
        /// </summary>
            public const string IconButtonUser = "M660.338 528.065c63.61-46.825 105.131-121.964 105.131-206.83 0-141.7-115.29-256.987-256.997-256.987-141.706 0-256.998 115.288-256.998 256.987 0 85.901 42.52 161.887 107.456 208.562-152.1 59.92-260.185 207.961-260.185 381.077 0 21.276 17.253 38.53 38.53 38.53 21.278 0 38.53-17.254 38.53-38.53 0-183.426 149.232-332.671 332.667-332.671 1.589 0 3.113-0.207 4.694-0.244 0.8 0.056 1.553 0.244 2.362 0.244 183.434 0 332.664 149.245 332.664 332.671 0 21.276 17.255 38.53 38.533 38.53 21.277 0 38.53-17.254 38.53-38.53 0-174.885-110.354-324.13-264.917-382.809z m-331.803-206.83c0-99.22 80.72-179.927 179.935-179.927s179.937 80.708 179.937 179.927c0 99.203-80.721 179.91-179.937 179.91s-179.935-80.708-179.935-179.91z";
            /// <summary>
        /// 图标按钮，盾牌，1x
        /// </summary>
            public const string IconButtonShield = "M511.488256 95.184408c35.310345 22.516742 95.184408 55.78011 167.34033 84.437781 75.738131 29.681159 148.405797 40.93953 191.392304 45.033483v353.615193c0 73.691154-50.662669 164.781609-136.123938 244.101949C649.65917 901.181409 558.568716 942.12094 512 942.12094c-46.568716 0-137.65917-40.93953-222.096952-119.748126C204.441779 742.54073 153.77911 651.450275 153.77911 577.247376v-353.103448c42.474763-4.093953 116.165917-15.352324 191.904048-45.545227 75.226387-30.192904 133.565217-63.456272 165.805098-83.414293M512 0c-4.093953 0-8.187906 1.535232-11.258371 3.582209l-14.84058 10.234882c-1.023488 0.511744-67.550225 47.592204-170.410794 88.531735-100.813593 39.916042-198.556722 41.963018-199.58021 41.963018l-25.075462 0.511744c-10.746627 0.511744-18.934533 8.187906-18.934533 18.422789v414.000999c0 216.97951 286.064968 446.24088 440.09995 446.24088s440.09995-229.261369 440.09995-445.729136V163.758121c0-10.234883-8.69965-18.422789-18.934533-18.422789l-24.563718-0.511744c-1.023488 0-98.766617-2.046977-199.58021-41.963018-103.372314-40.93953-170.410795-88.01999-170.922538-88.531734L523.258371 3.582209c-3.070465-2.558721-7.164418-3.582209-11.258371-3.582209z M743.308346 410.930535l-260.477761 260.477761c-15.864068 15.864068-41.963018 15.864068-57.827087 0l-144.823588-144.823588c-15.864068-15.864068-15.864068-41.963018 0-57.827087 8.187906-8.187906 18.422789-11.770115 29.169415-11.770115 10.234883 0 20.981509 4.093953 29.169416 11.770115l115.654173 115.654173L685.993003 352.591704c15.864068-15.864068 41.963018-15.864068 57.827087 0 15.352324 16.375812 15.352324 42.474763-0.511744 58.338831z";
            /// <summary>
        /// 图标按钮，离线，0.85x
        /// </summary>
            public const string IconButtonOffline = "M533.293176 788.841412a60.235294 60.235294 0 1 1 85.202824 85.202823l-42.616471 42.586353c-129.355294 129.385412-339.124706 129.385412-468.510117 0-129.385412-129.385412-129.385412-339.124706 0-468.510117l42.586353-42.616471a60.235294 60.235294 0 1 1 85.202823 85.202824l-42.61647 42.586352a210.823529 210.823529 0 1 0 298.164706 298.164706l42.586352-42.61647z m255.548236-255.548236l42.61647-42.586352a210.823529 210.823529 0 1 0-298.164706-298.164706l-42.586352 42.61647a60.235294 60.235294 0 1 1-85.202824-85.202823l42.616471-42.586353c129.355294-129.385412 339.124706-129.385412 468.510117 0 129.385412 129.385412 129.385412 339.124706 0 468.510117l-42.586353 42.616471a60.235294 60.235294 0 1 1-85.202823-85.202824zM192.542118 192.542118a60.235294 60.235294 0 0 1 85.202823 0l553.712941 553.712941a60.235294 60.235294 0 0 1-85.202823 85.202823L192.542118 277.744941a60.235294 60.235294 0 0 1 0-85.202823z";
            /// <summary>
        /// 图标，服务端，1x
        /// </summary>
            public const string IconButtonServer = "M224 160a64 64 0 0 0-64 64v576a64 64 0 0 0 64 64h576a64 64 0 0 0 64-64V224a64 64 0 0 0-64-64H224z m0 384h576v256H224v-256z m192 96v64h320v-64H416z m-128 0v64h64v-64H288zM224 224h576v256H224V224z m192 96v64h320v-64H416z m-128 0v64h64v-64H288z";
            /// <summary>
        /// 图标，音符，1x
        /// </summary>
            public const string IconMusic = "M348.293565 716.53287V254.797913c0-41.672348 28.004174-78.358261 68.919652-90.37913L815.994435 40.826435c62.775652-18.610087 125.907478 26.579478 125.907478 89.933913v539.158261c8.013913 42.25113-8.94887 89.177043-47.014956 127.109565a232.848696 232.848696 0 0 1-170.785392 65.758609c-61.885217-2.938435-111.081739-33.435826-129.113043-80.050087-18.031304-46.614261-2.137043-102.177391 41.672348-145.853218a232.848696 232.848696 0 0 1 170.785391-65.80313c21.014261 1.024 40.514783 5.164522 57.878261 12.065391V233.338435c0-12.109913-10.551652-20.034783-20.569044-20.034783a24.620522 24.620522 0 0 0-5.787826 0.934957L439.785739 338.18713a19.545043 19.545043 0 0 0-14.825739 19.144348v438.984348H423.846957c11.53113 43.987478-5.164522 94.208-45.412174 134.322087a232.848696 232.848696 0 0 1-170.785392 65.758609c-61.885217-2.938435-111.081739-33.435826-129.113043-80.050087-18.031304-46.614261-2.137043-102.177391 41.672348-145.853218a232.848696 232.848696 0 0 1 170.785391-65.80313c20.791652 1.024 40.069565 5.075478 57.299478 11.842783z";
            /// <summary>
        /// 图标，播放，0.8x
        /// </summary>
            public const string IconPlay = "M803.904 463.936a55.168 55.168 0 0 1 0 96.128l-463.616 264.448C302.848 845.888 256 819.136 256 776.448V247.616c0-42.752 46.848-69.44 84.288-48.064l463.616 264.384z";
        }

        #endregion

        #region 自定义类

        /// <summary>
    /// 支持小数与常见类型隐式转换的颜色。
    /// </summary>
        public class MyColor
        {

            public double A = 255d;
            public double R = 0d;
            public double G = 0d;
            public double B = 0d;

            // 类型转换
            public static implicit operator MyColor(string str)
            {
                return new MyColor(str);
            }
            public static implicit operator MyColor(Color col)
            {
                return new MyColor(col);
            }
            public static implicit operator Color(MyColor conv)
            {
                return Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B));
            }
            public static implicit operator System.Drawing.Color(MyColor conv)
            {
                return System.Drawing.Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B));
            }
            public static implicit operator MyColor(SolidColorBrush bru)
            {
                return new MyColor(bru.Color);
            }
            public static implicit operator SolidColorBrush(MyColor conv)
            {
                return new SolidColorBrush(Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B)));
            }
            public static implicit operator MyColor(Brush bru)
            {
                return new MyColor(bru);
            }
            public static implicit operator Brush(MyColor conv)
            {
                return new SolidColorBrush(Color.FromArgb(ClampToByte(conv.A), ClampToByte(conv.R), ClampToByte(conv.G), ClampToByte(conv.B)));
            }

            // 颜色运算
            public static MyColor operator +(MyColor a, MyColor b)
            {
                return new MyColor() { A = a.A + b.A, B = a.B + b.B, G = a.G + b.G, R = a.R + b.R };
            }
            public static MyColor operator -(MyColor a, MyColor b)
            {
                return new MyColor() { A = a.A - b.A, B = a.B - b.B, G = a.G - b.G, R = a.R - b.R };
            }
            public static MyColor operator *(MyColor a, double b)
            {
                return new MyColor() { A = a.A * b, B = a.B * b, G = a.G * b, R = a.R * b };
            }
            public static MyColor operator /(MyColor a, double b)
            {
                return new MyColor() { A = a.A / b, B = a.B / b, G = a.G / b, R = a.R / b };
            }
            public static bool operator ==(MyColor a, MyColor b)
            {
                // 注意：此处必须用 is null（引用比较），不能用 a == null。
                // 在 operator == 内部写 a == null 会再次调用本运算符，导致无限递归栈溢出（VB 原文为 IsNothing）。
                if (a is null && b is null)
                    return true;
                if (a is null || b is null)
                    return false;
                return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
            }
            public static bool operator !=(MyColor a, MyColor b)
            {
                if (a is null && b is null)
                    return false;
                if (a is null || b is null)
                    return true;
                return !(a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B);
            }
            /// <summary>
        /// 获取两颜色间的百分比，基于 RGB 计算。
        /// </summary>
            public static MyColor Lerp(MyColor ValueA, MyColor ValueB, double Percent)
            {
                return Round(ValueA * (1d - Percent) + ValueB * Percent, 6);
            }
            public static MyColor Round(MyColor col, int w = 0)
            {
                return new MyColor() { A = Math.Round(col.A, w), R = Math.Round(col.R, w), G = Math.Round(col.G, w), B = Math.Round(col.B, w) };
            }
            /// <summary>
        /// 将一个数字限制为 0~255 的 Byte 值。
        /// </summary>
            public static byte ClampToByte(double d)
            {
                if (d < 0d)
                    d = 0d;
                if (d > 255d)
                    d = 255d;
                return (byte)Math.Round(Math.Round(d));
            }


            // 构造函数
            public MyColor()
            {
            }
            public MyColor(Color col)
            {
                A = col.A;
                R = col.R;
                G = col.G;
                B = col.B;
            }
            public MyColor(string HexString)
            {
                Color StringColor = (Color)ColorConverter.ConvertFromString(HexString);
                A = StringColor.A;
                R = StringColor.R;
                G = StringColor.G;
                B = StringColor.B;
            }
            public MyColor(double newA, MyColor col)
            {
                A = newA;
                R = col.R;
                G = col.G;
                B = col.B;
            }
            public MyColor(double newR, double newG, double newB)
            {
                A = 255d;
                R = newR;
                G = newG;
                B = newB;
            }
            public MyColor(double newA, double newR, double newG, double newB)
            {
                A = newA;
                R = newR;
                G = newG;
                B = newB;
            }
            public MyColor(Brush brush)
            {
                var Color = ((SolidColorBrush)brush).Color;
                A = Color.A;
                R = Color.R;
                G = Color.G;
                B = Color.B;
            }
            public MyColor(SolidColorBrush brush)
            {
                var Color = brush.Color;
                A = Color.A;
                R = Color.R;
                G = Color.G;
                B = Color.B;
            }
            public MyColor(object obj)
            {
                if (obj is null)
                {
                    A = 255d;
                    R = 255d;
                    G = 255d;
                    B = 255d;
                }
                else if (obj is SolidColorBrush)
                {
                    // 避免反复获取 Color 对象造成性能下降
                    var Color = ((SolidColorBrush)obj).Color;
                    A = Color.A;
                    R = Color.R;
                    G = Color.G;
                    B = Color.B;
                }
                else
                {
                    A = Conversions.ToDouble(((dynamic)obj).A);
                    R = Conversions.ToDouble(((dynamic)obj).R);
                    G = Conversions.ToDouble(((dynamic)obj).G);
                    B = Conversions.ToDouble(((dynamic)obj).B);
                }
            }

            // HSL
            public double Hue(double v1, double v2, double vH)
            {
                if (vH < 0d)
                    vH += 1d;
                if (vH > 1d)
                    vH -= 1d;
                if (vH < 0.16667d)
                    return v1 + (v2 - v1) * 6d * vH;
                if (vH < 0.5d)
                    return v2;
                if (vH < 0.66667d)
                    return v1 + (v2 - v1) * (4d - vH * 6d);
                return v1;
            }
            public MyColor FromHSL(double sH, double sS, double sL)
            {
                A = 255d;
                if (sS == 0d)
                {
                    R = sL * 2.55d;
                    G = R;
                    B = R;
                    return this;
                }

                double H = sH / 360d;
                double S = sS / 100d;
                double L = sL / 100d;
                S = L < 0.5d ? S * L + L : S * (1.0d - L) + L;
                L = 2d * L - S;
                R = 255d * Hue(L, S, H + 1d / 3d);
                G = 255d * Hue(L, S, H);
                B = 255d * Hue(L, S, H - 1d / 3d);
                return this;
            }
            public MyColor FromHSL2(double sH, double sS, double sL)
            {
                A = 255d;
                if (sS == 0d)
                {
                    R = sL * 2.55d;
                    G = R;
                    B = R;
                    return this;
                }

                // 初始化
                sH = (sH + 3600000d) % 360d;
                double[] cent = new[] { +0.1d, -0.06d, -0.3d, -0.19d, -0.15d, -0.24d, -0.32d, -0.09d, +0.18d, +0.05d, -0.12d, -0.02d, +0.1d, -0.06d }; // 0, 30, 60
                                                                                                                                                       // 90, 120, 150
                                                                                                                                                       // 180, 210, 240
                                                                                                                                                       // 270, 300, 330
                                                                                                                                                       // 最后两位与前两位一致，加是变亮，减是变暗
                                                                                                                                                       // 计算色调对应的亮度片区
                double center = sH / 30.0d;
                int intCenter = (int)Math.Round(Math.Floor(center)); // 亮度片区编号
                center = 50d - ((1d - center + intCenter) * cent[intCenter] + (center - intCenter) * cent[intCenter + 1]) * sS;
                sL = (sL < center ? sL / center : 1d + (sL - center) / (100d - center)) * 50d;
                FromHSL(sH, sS, sL);
                return this;
            }

            public override string ToString()
            {
                return "(" + A + "," + R + "," + G + "," + B + ")";
            }
            public override bool Equals(object obj)
            {
                // VB 原文是 Me = obj（走自定义 Operator =）。这里直接复用本类型的 == 重载；
                // 绝不能再用 Operators.ConditionalCompareObjectEqual——VB 运行时会回调本运算符/本方法，造成递归栈溢出。
                return obj is MyColor other && this == other;
            }
            public override int GetHashCode()
            {
                // 必须与 == / Equals 的取值保持一致，否则在 HashSet/Dictionary/Distinct 中会失效。
                return HashCode.Combine(A, R, G, B);
            }

        }

        /// <summary>
    /// 支持负数与浮点数的矩形。
    /// </summary>
        public class MyRect
        {

            // 属性
            public double Width { get; set; } = 0d;
            public double Height { get; set; } = 0d;
            public double Left { get; set; } = 0d;
            public double Top { get; set; } = 0d;

            // 构造函数
            public MyRect()
            {
            }
            public MyRect(double left, double top, double width, double height)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

        }

        /// <summary>
    /// 模块加载状态枚举。
    /// </summary>
        public enum LoadState
        {
            Waiting,
            Loading,
            Finished,
            Failed,
            Canceled
        }

        /// <summary>
    /// 执行返回值。
    /// </summary>
        public enum ProcessReturnValues
        {
            /// <summary>
        /// 执行成功。
        /// </summary>
            Success = 0,
            /// <summary>
        /// 执行失败。
        /// </summary>
            Fail = 1,
            /// <summary>
        /// 执行时出现未经处理的异常。
        /// </summary>
            Exception = 2,
            /// <summary>
        /// 执行超时。
        /// </summary>
            Timeout = 3,
            /// <summary>
        /// 取消执行。可能是由于不满足执行的前置条件。
        /// </summary>
            Cancel = 4,
            /// <summary>
        /// 任务成功完成。
        /// </summary>
            TaskDone = 5
        }

        #endregion

        #region 文件

        // ini

        private readonly static ConcurrentDictionary<string, ConcurrentDictionary<string, string>> IniCache = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
        /// <summary>
    /// 清除某 ini 文件的运行时缓存。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
        public static void IniClearCache(string FileName)
        {
            if (!FileName.Contains(@":\"))
                FileName = $@"{Paths.Base}PCL\{FileName}.ini";
            IniCache.Remove(FileName);
        }
        /// <summary>
    /// 获取 ini 文件缓存。如果没有，则新读取 ini 文件内容。
    /// 在文件不存在或读取失败时返回 Nothing。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
        private static ConcurrentDictionary<string, string> IniGetContent(string FileName)
        {
            try
            {
                // 还原文件路径
                if (!FileName.Contains(@":\"))
                    FileName = $@"{Paths.Base}PCL\{FileName}.ini";
                // 检索缓存
                ConcurrentDictionary<string, string> Cache = null;
                if (IniCache.TryGetValue(FileName, out Cache))
                    return Cache;
                // 读取文件
                if (!FileUtils.Exists(FileName))
                    return null;
                var Ini = new ConcurrentDictionary<string, string>();
                foreach (var Line in FileUtils.ReadAsLines(FileName, true))
                {
                    int Index = Line.IndexOfF(":");
                    if (Index > 0)
                        Ini[Line.Substring(0, Index)] = Line.Substring(Index + 1); // 可能会有重复键，见 #3616
                }
                IniCache[FileName] = Ini;
                return Ini;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"生成 ini 文件缓存失败（{FileName}）", LogBehavior.Toast);
                return null;
            }
        }
        /// <summary>
    /// 读取 ini 文件。这可能会使用到缓存。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    /// <param name="Key">键。</param>
    /// <param name="DefaultValue">没有找到键时返回的默认值。</param>
        public static string ReadIni(string FileName, string Key, string DefaultValue = "")
        {
            var Content = IniGetContent(FileName);
            if (Content is null)
                return DefaultValue;
            return Content.GetOrDefault(Key, DefaultValue);
        }
        /// <summary>
    /// 判断 ini 文件中是否包含某个键。这可能会使用到缓存。
    /// </summary>
        public static bool HasIniKey(string FileName, string Key)
        {
            var Content = IniGetContent(FileName);
            return Content is not null && Content.ContainsKey(Key);
        }
        /// <summary>
    /// 从 ini 文件中移除某个键。这会更新缓存。
    /// </summary>
        public static void DeleteIniKey(string FileName, string Key)
        {
            WriteIni(FileName, Key, null);
        }
        /// <summary>
    /// 写入 ini 文件，这会更新缓存。
    /// 若 Value 为 Nothing，则删除该键。
    /// </summary>
    /// <param name="FileName">文件完整路径或简写文件名。简写将会使用“ApplicationName\文件名.ini”作为路径。</param>
    /// <param name="Key">键。</param>
    /// <param name="Value">值。</param>
        public static void WriteIni(string FileName, string Key, string Value)
        {
            try
            {
                // 预处理
                if (Key.Contains(":"))
                    throw new Exception($"尝试写入 ini 文件 {FileName} 的键名中包含了冒号：{Key}");
                Key = Key.ReplaceLineEndings("");
                Value = Value?.ReplaceLineEndings("");
                // 获取目前文件
                var Content = IniGetContent(FileName);
                if (Content is null)
                    Content = new ConcurrentDictionary<string, string>();
                // 更新值
                if (Value is null)
                {
                    if (!Content.Remove(Key))
                        return; // 无需处理
                }
                else
                {
                    string ExistingValue = null;
                    if (Content.TryGetValue(Key, out ExistingValue) && (ExistingValue ?? "") == (Value ?? ""))
                        return; // 无需处理
                    Content[Key] = Value;
                }
                // 写入文件
                var FileContent = new StringBuilder();
                foreach (var Pair in Content)
                {
                    FileContent.Append(Pair.Key);
                    FileContent.Append(":");
                    FileContent.Append(Pair.Value);
                    FileContent.Append(Constants.vbCrLf);
                }
                if (!FileName.Contains(@":\"))
                    FileName = $@"{Paths.Base}PCL\{FileName}.ini";
                // 处理相对路径
                FileName = FileName.Contains(@":\") ? FileName : Paths.Base + FileName;
                FileUtils.Write(FileName, FileContent.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"写入文件失败（{FileName} → {Key}:{Value}）", LogBehavior.Toast);
            }
        }

        // 权限检查

        /// <summary>
    /// 检查是否拥有某一文件夹的 I/O 权限。如果文件夹不存在，会返回 False。
    /// </summary>
        public static bool CheckPermission(string Folder)
        {
            try
            {
                if (string.IsNullOrEmpty(Folder))
                    return false;
                Folder = PathUtils.AddSlashSuffix(Folder);
                if (Folder.EndsWithF(@":\System Volume Information\", true) || Folder.EndsWithF(@":\$RECYCLE.BIN\", true))
                    return false;
                if (!DirectoryUtils.Exists(Folder))
                    return false;
                string TestFilePath = $"{Folder}CheckPermission{GetUuid()}.txt";
                FileUtils.Write(TestFilePath, "临时文件，用于检测该文件夹的权限是否正常。");
                FileUtils.Delete(TestFilePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"没有对文件夹 {Folder} 的权限，请尝试以管理员权限运行 PCL Next");
                return false;
            }
        }
        /// <summary>
    /// 检查是否拥有某一文件夹的 I/O 权限。如果出错，则抛出异常。
    /// </summary>
        public static void CheckPermissionWithException(string Folder)
        {
            if (string.IsNullOrWhiteSpace(Folder))
                throw new ArgumentNullException("文件夹名不能为空！");
            Folder = PathUtils.AddSlashSuffix(Folder);
            if (Folder.EndsWithF(@":\System Volume Information\", true) || Folder.EndsWithF(@":\$RECYCLE.BIN\", true))
                throw new UnauthorizedAccessException("没有对系统文件夹的权限！");
            if (!DirectoryUtils.Exists(Folder))
                throw new DirectoryNotFoundException("文件夹不存在！");
            string TestFilePath = $"{Folder}CheckPermission{GetUuid()}.txt";
            FileUtils.Write(TestFilePath, "临时文件，用于检测该文件夹的权限是否正常。");
            FileUtils.Delete(TestFilePath);
        }

        #endregion

        #region 文本
        public static char vbLQ = Convert.ToChar(8220);
        public static char vbRQ = Convert.ToChar(8221);

        /// <summary>
    /// 不会报错的 Val。
    /// 如果输入有误，返回 0。
    /// </summary>
        public static double Val(object Str)
        {
            if (Str is null)
                return 0d;
            try
            {
                return Str is string && Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(Str, "&", false)) ? 0d : Conversion.Val(Str);
            }
            catch
            {
                return 0d;
            }
        }

        #endregion

        #region 搜索

        /// <summary>
    /// 获取搜索文本的相似度。
    /// </summary>
    /// <param name="Source">被搜索的长内容。</param>
    /// <param name="Query">用户输入的搜索文本。</param>
        private static double SearchSimilarity(string Source, string Query)
        {
            if (string.IsNullOrEmpty(Source) || string.IsNullOrEmpty(Query))
                return 0d;
            int qp = 0;
            double lenSum = 0d;
            var str = new StringBuilder(Source.Length);
            str.Append(Source.Lower().Replace(" ", ""));
            Query = Query.Lower().Replace(" ", "");
            int sourceLength = str.Length;
            int queryLength = Query.Length; // 用于计算最后因数的长度缓存
            if (queryLength == 0)
                return 0d;
            while (qp < queryLength)
            {
                // 对 qp 作为开始位置计算
                int sp = 0;
                int lenMax = 0;
                int spMax = 0;
                int currentSourceLength = str.Length;
                while (sp < currentSourceLength)
                {
                    int len = 0;
                    while (qp + len < queryLength && sp + len < currentSourceLength && str[sp + len] == Query[qp + len])
                        len += 1;
                    // 存储 len
                    if (len > lenMax)
                    {
                        lenMax = len;
                        spMax = sp;
                    }
                    // 根据结果增加 sp
                    sp += len > 0 ? len : 1;
                }
                if (lenMax > 0)
                {
                    str.Remove(spMax, lenMax); // 将源中的对应字段移除
                                               // 存储 lenSum
                    double IncWeight = Math.Pow(1.4d, 3 + lenMax) - 3.6d; // 根据长度加成
                    IncWeight *= 1d + 0.3d * Math.Max(0, 3 - Math.Abs(qp - spMax)); // 根据位置加成
                    lenSum += IncWeight;
                }
                qp += lenMax > 0 ? lenMax : 1;
            }
            // 计算结果：重复字段量 × 源长度影响比例
            return lenSum / queryLength * (3d / Math.Sqrt(sourceLength + 15)) * (queryLength <= 2 ? 3 - queryLength : 1);
        }
        /// <summary>
    /// 获取多段文本加权后的相似度。
    /// </summary>
        private static double SearchSimilarityWeighted(List<SearchSource> Source, string Query)
        {
            double TotalWeight = 0d;
            double Sum = 0d;
            foreach (var Pair in Source)
            {
                if (Pair.Aliases.Any())
                {
                    Sum += Pair.Aliases.Max(a => SearchSimilarity(a, Query)) * Pair.Weight;
                }
                TotalWeight += Pair.Weight;
            }
            return Sum / TotalWeight;
        }
        /// <summary>
    /// 用于搜索的项目。
    /// </summary>
        public class SearchEntry<T>
        {
            /// <summary>
        /// 该项目对应的源数据。
        /// </summary>
            public T Item;
            /// <summary>
        /// 该项目用于搜索的文本源。
        /// 在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
        /// </summary>
            public List<SearchSource> SearchSource;
            /// <summary>
        /// 相似度。
        /// </summary>
            public double Similarity;
            /// <summary>
        /// 是否完全匹配。
        /// </summary>
            public bool AbsoluteRight;
            public override string ToString()
            {
                return Math.Round(Similarity, 3) + " - " + Item.ToString();
            }
        }
        /// <summary>
    /// 单个用于搜索的文本源。
    /// </summary>
        public class SearchSource
        {
            public string[] Aliases;
            public double Weight;
            public SearchSource(string[] Aliases, double Weight = 1d)
            {
                this.Aliases = Aliases;
                this.Weight = Weight;
            }
            public SearchSource(string Text, double Weight = 1d)
            {
                Aliases = new[] { Text };
                this.Weight = Weight;
            }
        }
        /// <summary>
    /// 进行多段文本加权搜索，获取相似度较高的数项结果。
    /// 在搜索时，会对每个文本源单独加权，但单个文本源内的多个别名只取最高的一个的相似度。
    /// 这会修改 Entries 中每项的 Similarity 与 AbsoluteRight 字段。
    /// </summary>
    /// <param name="MaxBlurCount">返回的最大模糊结果数。</param>
    /// <param name="MinBlurSimilarity">返回结果要求的最低相似度。</param>
        public static List<SearchEntry<T>> Search<T>(List<SearchEntry<T>> Entries, string Query, int MaxBlurCount = 5, double MinBlurSimilarity = 0.1d)
        {
            // 初始化
            var ResultList = new List<SearchEntry<T>>();
            if (!Entries.Any())
                return ResultList;
            // 进行搜索，获取相似信息
            string[] QueryParts = Query.Split(" ", true);
            var Candidates = new List<SearchEntry<T>>();
            foreach (var Entry in Entries)
            {
                Entry.Similarity = SearchSimilarityWeighted(Entry.SearchSource, Query);
                Entry.AbsoluteRight = QueryParts.All(QueryPart => Entry.SearchSource.Any(Source => Source.Aliases.Any(Alias => Alias.Replace(" ", "").ContainsIgnoreCase(QueryPart)))); // 对于按空格分割的每一段
                                                                                                                                                                                        // 若与任意一个搜索源完全匹配，则标记为完全匹配项
                if (Entry.AbsoluteRight || Entry.Similarity >= MinBlurSimilarity)
                    Candidates.Add(Entry);
            }
            // 按照相似度进行排序
            Candidates = Candidates.SortByComparison((Left, Right) => Left.AbsoluteRight != Right.AbsoluteRight ? Left.AbsoluteRight : Left.Similarity > Right.Similarity);
            // 返回结果
            int BlurCount = 0;
            foreach (var Entry in Candidates)
            {
                if (Entry.AbsoluteRight)
                {
                    ResultList.Add(Entry); // 完全匹配，直接加入
                }
                else
                {
                    if (BlurCount == MaxBlurCount)
                        break;
                    ResultList.Add(Entry);
                    BlurCount += 1;
                } // 模糊结果计数
            }
            return ResultList;
        }

        #endregion

        #region 系统

        /// <summary>
    /// 为 Task 设置超时，在超时时抛出 TimeoutException。
    /// </summary>
        public static T GetResultWithTimeout<T>(this Task<T> TargetTask, CancellationTokenSource TokenSource, int TimeoutMs)
        {
            var DelayTask = Task.Delay(TimeoutMs);
            if (ReferenceEquals(Task.WhenAny(TargetTask, DelayTask).ConfigureAwait(false).GetAwaiter().GetResult(), DelayTask))
            {
                TokenSource.Cancel();
                throw new TimeoutException($"任务超时（{TimeoutMs} ms）");
            }
            return TargetTask.Run();
        }
        /// <summary>
    /// 为 Task 设置超时，在超时时抛出 TimeoutException。
    /// </summary>
        public static void GetResultWithTimeout(this Task TargetTask, CancellationTokenSource TokenSource, int TimeoutMs)
        {
            var DelayTask = Task.Delay(TimeoutMs);
            if (ReferenceEquals(Task.WhenAny(TargetTask, DelayTask).ConfigureAwait(false).GetAwaiter().GetResult(), DelayTask))
            {
                TokenSource.Cancel();
                throw new TimeoutException($"任务超时（{TimeoutMs} ms）");
            }
            TargetTask.Run();
        }

        /// <summary>
    /// 可用于临时存放文件的，不含任何特殊字符的文件夹路径，以“\”结尾。
    /// </summary>
        public static Lazy<string> PathPure = new Lazy<string>(() => { if (Paths.Base.IsAsciiOnly()) { return Paths.Base + @"PCL\"; } else if (Paths.AppDataThenName.IsAsciiOnly()) { return Paths.AppDataThenName; } else if (PathTemp.IsAsciiOnly()) { return PathTemp; } else { return OsDrive + @"ProgramData\PCL\"; } });

        /// <summary>
    /// 指示接取到这个异常的函数进行重试。
    /// </summary>
        public class RestartException : Exception
        {
        }

        /// <summary>
    /// 以管理员权限运行当前程序，并等待程序运行结束。
    /// 返回程序的返回代码，如果运行失败将抛出异常。
    /// </summary>
        public static int RunAsAdmin(string Argument)
        {
            var NewProcess = StartProcess(new ProcessStartInfo(PathExe) { Verb = "runas", Arguments = Argument });
            NewProcess.WaitForExit();
            return NewProcess.ExitCode;
        }

        private static int Uuid = 1;
        private static object UuidLock;
        /// <summary>
    /// 获取一个全程序内不会重复的数字（伪 Uuid）。
    /// </summary>
        public static int GetUuid()
        {
            if (UuidLock is null)
                UuidLock = new object();
            lock (UuidLock)
            {
                Uuid += 1;
                return Uuid;
            }
        }

        // 时间相关
        /// <summary>
    /// 获取一个单调递增时间值（毫秒）。
    /// </summary>
        public static long GetTimeMs()
        {
            return Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000L);
        }
        /// <summary>
    /// 获取十进制 Unix 时间戳。
    /// </summary>
        public static long GetUnixTimestampUtc()
        {
            return (long)Math.Round((DateTime.Now.ToUniversalTime().Ticks - 621355968000000000L) / 10000000d);
        }
        /// <summary>
    /// 获取十进制 Unix 时间戳。
    /// </summary>
        public static long ToUnixTimestampUtc(DateTime Time)
        {
            return (long)Math.Round((Time.ToUniversalTime().Ticks - 621355968000000000L) / 10000000d);
        }
        /// <summary>
    /// 时间戳转化为日期。
    /// </summary>
        public static DateTime GetDate(int timeStamp)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timeStamp).LocalDateTime;
        }
        /// <summary>
    /// 将 UTC 时间转化为当前时区的时间。
    /// </summary>
        public static DateTime GetLocalTime(DateTime UtcDate)
        {
            return DateTime.SpecifyKind(UtcDate, DateTimeKind.Utc).ToLocalTime();
        }

        /// <summary>
    /// 用于储存 RaiseByMouse 的 EventArgs。
    /// </summary>
        public sealed class RouteEventArgs : EventArgs
        {
            public bool RaiseByMouse;
            public bool Handled = false;
            public RouteEventArgs(bool RaiseByMouse = false)
            {
                this.RaiseByMouse = RaiseByMouse;
            }
        }

        /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。
    /// 会自动进行长路径处理。
    /// </summary>
        public static Process StartProcess(string FileName, string Arguments = "")
        {
            return StartProcess(new ProcessStartInfo() { FileName = FileName, Arguments = Arguments });
        }
        /// <summary>
    /// 启动进程并返回进程实例，若失败则抛出异常。
    /// 会自动进行长路径处理。
    /// </summary>
        public static Process StartProcess(ProcessStartInfo StartInfo)
        {
            StartInfo.FileName = PathUtils.ToShortPath(StartInfo.FileName);
            if (!string.IsNullOrEmpty(StartInfo.WorkingDirectory))
                StartInfo.WorkingDirectory = PathUtils.ToShortPath(StartInfo.WorkingDirectory);
            Logger.Info($"启动进程：{StartInfo.FileName} {StartInfo.Arguments}");
            return Process.Start(StartInfo);
        }

        /// <summary>
    /// 在新的工作线程中执行代码。
    /// </summary>
        public static Thread RunInNewThread(Action Action, string Name = null, ThreadPriority Priority = ThreadPriority.Normal)
        {
            var th = new Thread(() => { try { Action(); } catch (Exception ex) { Logger.Error(ex, $"{Name}：线程执行失败"); } }) { Name = Name ?? "Runtime New Invoke " + GetUuid() + "#", Priority = Priority };
            th.Start();
            return th;
        }
        /// <summary>
    /// 确保在 UI 线程中执行代码。
    /// 如果当前并非 UI 线程，则会阻断当前线程，直至 UI 线程执行完毕。
    /// 为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！
    /// </summary>
        public static Output RunInUiWait<Output>(Func<Output> Action)
        {
            if (RunInUi())
            {
                return Action();
            }
            else
            {
                return Application.Current.Dispatcher.Invoke(Action);
            }
        }
        /// <summary>
    /// 确保在 UI 线程中执行代码。
    /// 如果当前并非 UI 线程，则会阻断当前线程，直至 UI 线程执行完毕。
    /// 为防止线程互锁，请仅在开始加载动画、从 UI 获取输入时使用！
    /// </summary>
        public static void RunInUiWait(Action Action)
        {
            if (RunInUi())
            {
                Action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(Action);
            }
        }
        /// <summary>
    /// 确保在 UI 线程中执行代码，代码按触发顺序执行。
    /// 如果当前并非 UI 线程，也不阻断当前线程的执行。
    /// </summary>
        public static void RunInUi(Action Action, bool ForceWaitUntilLoaded = false)
        {
            if (ForceWaitUntilLoaded)
            {
                Application.Current.Dispatcher.InvokeAsync(Action, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (RunInUi())
            {
                Action();
            }
            else
            {
                Application.Current.Dispatcher.InvokeAsync(Action);
            }
        }
        /// <summary>
    /// 确保在工作线程中执行代码。
    /// </summary>
        public static void RunInThread(Action Action)
        {
            if (RunInUi())
            {
                RunInNewThread(Action, "Invoke " + GetUuid());
            }
            else
            {
                Action();
            }
        }

        /// <summary>
    /// 按照既定的函数进行选择排序。
    /// 返回第一个对象是否应该排在前面（a > b）。
    /// </summary>
        public static List<T> SortByComparison<T>(this IList<T> List, Func<T, T, bool> SortRule)
        {
            var NewList = new List<T>();
            while (List.Any())
            {
                var Highest = List[0];
                for (int i = 1, loopTo = List.Count - 1; i <= loopTo; i++)
                {
                    if (SortRule(List[i], Highest))
                        Highest = List[i];
                }
                List.Remove(Highest);
                NewList.Add(Highest);
            }
            return NewList;
        }

        /// <summary>
    /// 获取程序启动参数。
    /// </summary>
    /// <param name="Name">参数名。</param>
    /// <param name="DefaultValue">默认值。</param>
        public static object GetProgramArgument(string Name) { return GetProgramArgument(Name, ""); }
        public static object GetProgramArgument(string Name, object DefaultValue)
        {
            string[] AllArguments = Interaction.Command().Split(" ");
            for (int i = 0, loopTo = AllArguments.Length - 1; i <= loopTo; i++)
            {
                if ((AllArguments[i] ?? "") == ("-" + Name ?? ""))
                {
                    if (AllArguments.Length == i + 1 || AllArguments[i + 1].StartsWithF("-"))
                        return true;
                    return AllArguments[i + 1];
                }
            }
            return DefaultValue;
        }

        /// <summary>
    /// 打开网页。
    /// </summary>
        public static void OpenWebsite(string Url)
        {
            try
            {
                if (!Url.StartsWithF("http", true) && !Url.StartsWithF("minecraft://", true) && !Url.StartsWithF("minecraft-preview://", true))
                {
                    throw new Exception(Url + " 不是一个有效的网址，它必须以 http 开头！");
                }
                Logger.Info($"正在打开网页：{Url}");
                StartProcess(Url);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"无法打开网页（{Url}）");
                ClipboardSet(Url, false);
                ModMain.MyMsgBox("可能由于浏览器未正确配置，PCL Next 无法为你打开网页。" + Constants.vbCrLf + "网址已经复制到剪贴板，若有需要可以手动粘贴访问。" + Constants.vbCrLf + $"网址：{Url}", "无法打开网页");
            }
        }
        /// <summary>
    /// 打开 explorer。
    /// 若不以 \ 结尾，则将视作文件路径，打开并选中此文件。
    /// </summary>
        public static void OpenExplorer(string Location)
        {
            try
            {
                Location = Location.Replace("/", @"\").Trim(' ', '"');
                Logger.Info($"正在打开资源管理器：{Location}");
                if (Location.EndsWithF(@"\"))
                {
                    StartProcess(Location);
                }
                else
                {
                    StartProcess("explorer", $"/select,\"{Location}\"");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "打开资源管理器失败，请尝试关闭安全软件（如 360 安全卫士）", LogBehavior.Alert);
            }
        }

        /// <summary>
    /// 设置剪贴板。将在另一线程运行，且不会抛出异常。
    /// </summary>
        public static void ClipboardSet(string Text, bool ShowSuccessHint = true)
        {
            ModBase.RunInThread(() =>
        {
            try
            {
                Retrier.Attempt(delay: Attempt => TimeSpan.FromMilliseconds(200L), maxAttempts: 4, isRetryAllowed: ex => true, action: (_) => RunInUiWait(() =>
{
                    My.Computer.Clipboard.Clear();
                    if (!string.IsNullOrEmpty(Text))
                        My.Computer.Clipboard.SetText(Text);
                }));
                if (ShowSuccessHint)
                    ModMain.Hint("已成功复制！", ModMain.HintType.Green);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "可能由于剪贴板被其他程序占用，文本复制失败", LogBehavior.Toast);
            }
        });
        }
        /// <summary>
    /// 获取剪贴板文本。将在 UI 线程运行，且不会抛出异常。
    /// </summary>
        public static string ClipboardGetText()
        {
            string Result = null;
            RunInUiWait(() => { try { if (My.Computer.Clipboard.ContainsText()) Result = My.Computer.Clipboard.GetText(); } catch (Exception ex) { Logger.Warn(ex, "获取剪贴板文本失败"); } });
            return Result;
        }

        /// <summary>
    /// 将程序中的资源释放到文件。
    /// </summary>
        public static void ExtractResources(string FilePath, string ResourceName)
        {
            var Resource = My.Resources.Resources.ResourceManager.GetObject(ResourceName);
            if (Resource is byte[])
            {
                byte[] Bytes = (byte[])Resource;
                if (FileUtils.Exists(FilePath) && (CryptographyUtils.ComputeFileHash(FilePath) ?? "") == (CryptographyUtils.ComputeHash(Bytes) ?? ""))
                    return;
                Logger.Info($"将资源写入到文件：{ResourceName} → {FilePath}");
                FileUtils.Write(FilePath, Bytes);
            }
            else if (Resource is string)
            {
                string Content = (string)Resource;
                if (FileUtils.Exists(FilePath) && (FileUtils.ReadAsString(FilePath) ?? "") == (Content ?? ""))
                    return;
                Logger.Info($"将资源写入到文件：{ResourceName} → {FilePath}");
                FileUtils.Write(FilePath, Content);
            }
            else
            {
                throw new Exception($"资源 {ResourceName} 的类型不支持：{Resource.GetType().Name}");
            }
        }

        #endregion

        #region UI

        // DPI 转换
        public readonly static int DPI = (int)Math.Round(System.Drawing.Graphics.FromHwnd(nint.Zero).DpiX);
        /// <summary>
    /// 将经过 DPI 缩放的 WPF 尺寸转化为实际的像素尺寸。
    /// </summary>
        public static double GetPixelSize(double WPFSize)
        {
            return WPFSize / 96d * DPI;
        }
        /// <summary>
    /// 将实际的像素尺寸转化为经过 DPI 缩放的 WPF 尺寸。
    /// </summary>
        public static double GetWPFSize(double PixelSize)
        {
            return PixelSize * 96d / DPI;
        }

        /// <summary>
    /// 将 XML 转换为对应 UI 对象。
    /// 注意：性能较差，不应大量使用。
    /// </summary>
        public static object GetObjectFromXML(XElement Str)
        {
            return GetObjectFromXML(Str.ToString());
        }
        /// <summary>
    /// 将 XML 转换为对应 UI 对象。
    /// 注意：性能较差，不应大量使用。
    /// </summary>
        public static object GetObjectFromXML(string Str)
        {
            Str = Str.Replace("EventType=\"", "local:CustomEventService.EventType=\"").Replace("EventData=\"", "local:CustomEventService.EventData=\"").Replace("EventType='", "local:CustomEventService.EventType='").Replace("EventData='", "local:CustomEventService.EventData='").Replace("Property=\"EventType\"", "Property=\"local:CustomEventService.EventType\"").Replace("Property=\"EventData\"", "Property=\"local:CustomEventService.EventData\"").Replace("Property='EventType'", "Property='local:CustomEventService.EventType'").Replace("Property='EventData'", "Property='local:CustomEventService.EventData'"); // 兼容旧版自定义事件写法
            using (var Stream = new MemoryStream(Encoding.UTF8.GetBytes(Str)))
            {
                // 类型检查
                using (var Reader = new XamlXmlReader(Stream))
                {
                    while (Reader.Read())
                    {
                        foreach (var BlackListType in new[] { typeof(WebBrowser), typeof(Frame), typeof(MediaElement), typeof(ObjectDataProvider), typeof(XamlReader), typeof(Window), typeof(XmlDataProvider), typeof(SettingService) })
                        {
                            if (Reader.Type is not null && BlackListType.IsAssignableFrom(Reader.Type.UnderlyingType))
                                throw new UnauthorizedAccessException($"基于安全考虑，不允许使用 {BlackListType.Name} 类型。");
                            if (Reader.Value is not null && Conversions.ToBoolean(Operators.ConditionalCompareObjectEqual(Reader.Value, BlackListType.Name, false)))
                                throw new UnauthorizedAccessException($"基于安全考虑，不允许使用 {BlackListType.Name} 值。");
                        }
                        foreach (var BlackListMember in new[] { "Code", "FactoryMethod", "Static" })
                        {
                            if (Reader.Member is not null && (Reader.Member.Name ?? "") == (BlackListMember ?? ""))
                                throw new UnauthorizedAccessException($"基于安全考虑，不允许使用 {BlackListMember} 成员。");
                        }
                    }
                }
                // 实际的加载
                Stream.Position = 0L;
                using (var Writer = new StreamWriter(Stream))
                {
                    Writer.Write(Str);
                    Writer.Flush();
                    Stream.Position = 0L;
                    return System.Windows.Markup.XamlReader.Load(Stream);
                }
            }
        }

        private readonly static int UiThreadId = Thread.CurrentThread.ManagedThreadId;
        /// <summary>
    /// 当前线程是否为主线程。
    /// </summary>
        public static bool RunInUi()
        {
            return Thread.CurrentThread.ManagedThreadId == UiThreadId;
        }

        /// <summary>
    /// 检查某个控件是否位于主窗口可视区域内，且控件本身可见。
    /// </summary>
        public static bool IsVisibleInForm(this FrameworkElement element)
        {
            if (!element.IsVisible)
                return false;
            var bounds = element.TransformToAncestor(ModMain.FrmMain).TransformBounds(new Rect(0d, 0d, element.ActualWidth, element.ActualHeight));
            var rect = new Rect(0d, 0d, ModMain.FrmMain.ActualWidth, ModMain.FrmMain.ActualHeight);
            return rect.Contains(bounds.TopLeft) || rect.Contains(bounds.BottomRight);
        }

        /// <summary>
    /// 控件是否受到 TextTrimming 属性影响，导致内容被截取。
    /// </summary>
        public static bool IsTextTrimmed(this TextBlock TextBlock)
        {
            var typeface = new Typeface(TextBlock.FontFamily, TextBlock.FontStyle, TextBlock.FontWeight, TextBlock.FontStretch);
            var formattedText = new FormattedText(TextBlock.Text, Thread.CurrentThread.CurrentCulture, TextBlock.FlowDirection, typeface, TextBlock.FontSize, TextBlock.Foreground, DPI);
            return formattedText.Width > TextBlock.ActualWidth;
        }

        /// <summary>
    /// 将布尔值转换为 Visibility。True 转换为 Visible，False 转换为 Collapsed。
    /// </summary>
        public static Visibility ToVisibility(this bool IsVisible)
        {
            return IsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region 随机

        private readonly static Random Random = new Random();

        /// <summary>
    /// 随机选择其一。
    /// </summary>
        public static T RandomOne<T>(ICollection<T> objects)
        {
            return objects.ElementAtOrDefault(RandomInteger(0, objects.Count - 1));
        }

        /// <summary>
    /// 取随机整数（包含）。
    /// </summary>
        public static int RandomInteger(int min, int max)
        {
            return (int)Math.Round(Math.Floor((max - min + 1) * Random.NextDouble()) + min);
        }

        /// <summary>
    /// 将数组随机打乱。
    /// </summary>
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> Raw)
        {
            var RawCopy = new List<T>(Raw);
            while (RawCopy.Any())
            {
                int i = RandomInteger(0, RawCopy.Count - 1);
                yield return RawCopy[i];
                RawCopy.RemoveAt(i);
            }
        }

        #endregion

    }
}