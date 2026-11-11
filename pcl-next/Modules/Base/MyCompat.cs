using System;
// pcl-next 新增：.NET 10 不再提供 Microsoft.VisualBasic 的 My 命名空间（那是桌面框架专属的生成代码），
// 这里按 .NET Framework 中 My.Computer / My.Application 的语义补齐 PCL 实际用到的那部分成员，
// 以便其余业务代码保持零改动。

using System.Runtime.InteropServices;
using System.Windows;

namespace PCL.My
{

    /// <summary>
    /// 等价于 My.Application 中被使用的成员。
    /// </summary>
    internal static class Application
    {

        /// <summary>
        /// 在当前应用的资源字典中查找资源，等价于 Application.TryFindResource。
        /// </summary>
        internal static object TryFindResource(object ResourceKey)
        {
            var CurrentApplication = System.Windows.Application.Current;
            if (CurrentApplication is null)
                return null;
            return CurrentApplication.TryFindResource(ResourceKey);
        }

    }

    /// <summary>
    /// 等价于 My.Computer 中被使用的成员。
    /// </summary>
    internal static class Computer
    {

        internal static ComputerInfo Info { get; private set; } = new ComputerInfo();
        internal static ComputerClipboard Clipboard { get; private set; } = new ComputerClipboard();
        internal static ComputerScreen Screen { get; private set; } = new ComputerScreen();
        internal static ComputerKeyboard Keyboard { get; private set; } = new ComputerKeyboard();
        internal static ComputerClock Clock { get; private set; } = new ComputerClock();

    }

    /// <summary>
    /// 系统信息，对应 My.Computer.Info。
    /// </summary>
    internal sealed class ComputerInfo
    {

        /// <summary>
        /// 当前可用的物理内存（字节）。
        /// </summary>
        internal ulong AvailablePhysicalMemory
        {
            get
            {
                return QueryMemory().ullAvailPhys;
            }
        }

        /// <summary>
        /// 物理内存总量（字节）。
        /// </summary>
        internal ulong TotalPhysicalMemory
        {
            get
            {
                return QueryMemory().ullTotalPhys;
            }
        }

        /// <summary>
        /// 系统版本字符串，对应 Environment.OSVersion.VersionString。
        /// </summary>
        internal string OSVersion
        {
            get
            {
                return Environment.OSVersion.VersionString;
            }
        }

        /// <summary>
        /// 系统完整名称，用于反馈信息展示。
        /// </summary>
        internal string OSFullName
        {
            get
            {
                return RuntimeInformation.OSDescription ?? OSVersion;
            }
        }

        private static MEMORYSTATUSEX QueryMemory()
        {
            var Status = new MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!MyNative.GlobalMemoryStatusEx(ref Status))
                return default;
            return Status;
        }

    }

    /// <summary>
    /// 剪贴板，对应 My.Computer.Clipboard（WPF 实现）。
    /// </summary>
    internal sealed class ComputerClipboard
    {

        internal void Clear()
        {
            Clipboard.Clear();
        }

        internal void SetText(string Text)
        {
            Clipboard.SetText(Text);
        }

        internal bool ContainsText()
        {
            return Clipboard.ContainsText();
        }

        internal string GetText()
        {
            return Clipboard.GetText();
        }

    }

    /// <summary>
    /// 主屏幕，对应 My.Computer.Screen。单位为物理像素，与 .NET Framework 的实现一致。
    /// </summary>
    internal sealed class ComputerScreen
    {

        private const uint SPI_GETWORKAREA = 48U;

        internal sealed class ScreenInfo
        {
            internal System.Drawing.Rectangle WorkingArea
            {
                get
                {
                    var Area = default(RECT);
                    if (!MyNative.SystemParametersInfo(SPI_GETWORKAREA, 0U, ref Area, 0U))
                        return System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                    return new System.Drawing.Rectangle(Area.Left, Area.Top, Area.Right - Area.Left, Area.Bottom - Area.Top);
                }
            }
        }

        private readonly ScreenInfo Primary = new ScreenInfo();

        /// <summary>
        /// 主屏幕的工作区（物理像素）。
        /// </summary>
        internal System.Drawing.Rectangle WorkingArea
        {
            get
            {
                return Primary.WorkingArea;
            }
        }

    }

    /// <summary>
    /// 键盘状态，对应 My.Computer.Keyboard。
    /// </summary>
    internal sealed class ComputerKeyboard
    {

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;

        internal bool CtrlKeyDown
        {
            get
            {
                return IsKeyDown(VK_CONTROL);
            }
        }

        internal bool ShiftKeyDown
        {
            get
            {
                return IsKeyDown(VK_SHIFT);
            }
        }

        private static bool IsKeyDown(int VirtualKey)
        {
            return MyNative.GetKeyState(VirtualKey) < 0;
        }

    }

    /// <summary>
    /// 时钟，对应 My.Computer.Clock。
    /// </summary>
    internal sealed class ComputerClock
    {

        /// <summary>
        /// 系统启动以来的毫秒数。
        /// </summary>
        internal int TickCount
        {
            get
            {
                return Environment.TickCount;
            }
        }

    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        internal uint dwLength;
        internal uint dwMemoryLoad;
        internal ulong ullTotalPhys;
        internal ulong ullAvailPhys;
        internal ulong ullTotalPageFile;
        internal ulong ullAvailPageFile;
        internal ulong ullTotalVirtual;
        internal ulong ullAvailVirtual;
        internal ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    /// <summary>
    /// 供 MyCompat 使用的 Win32 调用。
    /// </summary>
    internal static class MyNative
    {

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX Buffer);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SystemParametersInfo(uint Action, uint Param, ref RECT Data, uint WinIni);

        [DllImport("user32.dll")]
        internal static extern short GetKeyState(int VirtualKey);

    }

}