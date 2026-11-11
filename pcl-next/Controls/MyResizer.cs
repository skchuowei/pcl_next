using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCL
{

    public class MyResizer
    {

        private delegate void RefreshDelegate();

        private struct POINT
        {
            public int x;
            public int y;
            public POINT(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }
        // P/Invoke 结构体：字段由 Windows 原生代码经 SendMessage/WmGetMinMaxInfo 填充，编译器看不到赋值路径，故豁免 CS0649
#pragma warning disable CS0649
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }
#pragma warning restore CS0649
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
            public static RECT Empty = default;

            public int Width
            {
                get
                {
                    return Math.Abs(right - left);
                }
            }
            public int Height
            {
                get
                {
                    return bottom - top;
                }
            }

            public RECT(int left, int top, int right, int bottom)
            {
                this.left = left;
                this.top = top;
                this.right = right;
                this.bottom = bottom;
            }
            public RECT(RECT rcSrc)
            {
                left = rcSrc.left;
                top = rcSrc.top;
                right = rcSrc.right;
                bottom = rcSrc.bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = default;
            public RECT rcWork = default;
            public int dwFlags = 0;
        }

        private struct PointAPI
        {
            public int X;
            public int Y;
        }

        private readonly Window target = null;

        private bool resizeRight = false;
        private bool resizeLeft = false;
        private bool resizeUp = false;
        private bool resizeDown = false;

        private readonly Dictionary<UIElement, short> leftElements = new Dictionary<UIElement, short>();
        private readonly Dictionary<UIElement, short> rightElements = new Dictionary<UIElement, short>();
        private readonly Dictionary<UIElement, short> upElements = new Dictionary<UIElement, short>();
        private readonly Dictionary<UIElement, short> downElements = new Dictionary<UIElement, short>();

        private PointAPI startMousePoint = default;
        private Size startWindowSize = default;
        private POINT startWindowLeftUpPoint = default;

        private static int workAreaMaxHeight = -1;

        private HwndSource hs;

        public MyResizer(Window target)
        {
            this.target = target;
            if (target == null)
                throw new Exception("Invalid Window handle");
            target.SourceInitialized += MyMacClass_SourceInitialized;
        }

        private void MyMacClass_SourceInitialized(object sender, EventArgs e)
        {
            hs = PresentationSource.FromVisual((Visual)sender) as HwndSource;
            hs.AddHook(WndProc);
        }

        private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            if (msg == 36)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return 0;
        }

        private static void WmGetMinMaxInfo(nint hwnd, nint lParam)
        {
            MINMAXINFO mINMAXINFO = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            int flags = 2;
            var intPtr = MonitorFromWindow(hwnd, flags);
            bool flag = intPtr != nint.Zero;
            if (flag)
            {
                var mONITORINFO = new MONITORINFO();
                GetMonitorInfo(intPtr, mONITORINFO);
                var rcWork = mONITORINFO.rcWork;
                var rcMonitor = mONITORINFO.rcMonitor;
                mINMAXINFO.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left);
                mINMAXINFO.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top);
                mINMAXINFO.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top);
                workAreaMaxHeight = mINMAXINFO.ptMaxSize.y;
                if (rcWork.Height == rcMonitor.Height)
                {
                    mINMAXINFO.ptMaxSize.y -= 2;
                }
            }
            Marshal.StructureToPtr(mINMAXINFO, lParam, true);
        }

        [DllImport("user32")]
        private static extern bool GetMonitorInfo(nint hMonitor, MONITORINFO lpmi);

        [DllImport("User32")]
        private static extern nint MonitorFromWindow(nint handle, int flags);

        private void connectMouseHandlers(UIElement element)
        {
            element.MouseLeftButtonDown += element_MouseLeftButtonDown;
        }

        public void addResizerRight(UIElement element)
        {
            connectMouseHandlers(element);
            rightElements.Add(element, 0);
        }

        public void addResizerLeft(UIElement element)
        {
            connectMouseHandlers(element);
            leftElements.Add(element, 0);
        }

        public void addResizerUp(UIElement element)
        {
            connectMouseHandlers(element);
            upElements.Add(element, 0);
        }

        public void addResizerDown(UIElement element)
        {
            connectMouseHandlers(element);
            downElements.Add(element, 0);
        }

        public void addResizerRightDown(UIElement element)
        {
            connectMouseHandlers(element);
            rightElements.Add(element, 0);
            downElements.Add(element, 0);
        }

        public void addResizerLeftDown(UIElement element)
        {
            connectMouseHandlers(element);
            leftElements.Add(element, 0);
            downElements.Add(element, 0);
        }

        public void addResizerRightUp(UIElement element)
        {
            connectMouseHandlers(element);
            rightElements.Add(element, 0);
            upElements.Add(element, 0);
        }

        public void addResizerLeftUp(UIElement element)
        {
            connectMouseHandlers(element);
            leftElements.Add(element, 0);
            upElements.Add(element, 0);
        }

        private void element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            GetCursorPos(out startMousePoint);
            startMousePoint.X = (int)Math.Round(ModBase.GetWPFSize(startMousePoint.X));
            startMousePoint.Y = (int)Math.Round(ModBase.GetWPFSize(startMousePoint.Y));
            startWindowSize = new Size(target.Width, target.Height);
            startWindowLeftUpPoint = new POINT((int)Math.Round(target.Left), (int)Math.Round(target.Top));
            UIElement key = (UIElement)sender;
            if (leftElements.ContainsKey(key))
                resizeLeft = true;
            if (rightElements.ContainsKey(key))
                resizeRight = true;
            if (upElements.ContainsKey(key))
                resizeUp = true;
            if (downElements.ContainsKey(key))
                resizeDown = true;
            ModBase.RunInNewThread(updateSizeLoop, "窗口大小调整检测");
        }

        private void updateSizeLoop()
        {
            try
            {
                while (resizeDown || resizeLeft || resizeRight || resizeUp)
                {
                    target.Dispatcher.Invoke(updateSize, DispatcherPriority.Render);
                    target.Dispatcher.Invoke(updateMouseDown, DispatcherPriority.Render);
                    Thread.Sleep(0);
                }
            }
            catch
            {
            }
        }

        private void updateSize()
        {
            PointAPI pointAPI = default;
            GetCursorPos(out pointAPI);
            pointAPI.X = (int)Math.Round(ModBase.GetWPFSize(pointAPI.X));
            pointAPI.Y = (int)Math.Round(ModBase.GetWPFSize(pointAPI.Y));
            try
            {

                double NewWidth = -1;
                double NewHeight = -1;
                double NewLeft = -10000;
                double NewTop = -10000;

                if (resizeRight)
                {
                    if (target.Width == target.MinWidth)
                    {
                        if (startMousePoint.X < pointAPI.X)
                        {
                            NewWidth = startWindowSize.Width - (startMousePoint.X - pointAPI.X);
                        }
                    }
                    else if (startWindowSize.Width - (startMousePoint.X - pointAPI.X) >= target.MinWidth)
                    {
                        NewWidth = startWindowSize.Width - (startMousePoint.X - pointAPI.X);
                    }
                    else
                    {
                        NewWidth = target.MinWidth;
                    }
                }

                if (resizeDown)
                {
                    if (target.Height == target.MinHeight)
                    {
                        if (startMousePoint.Y < pointAPI.Y)
                        {
                            if (workAreaMaxHeight > 0)
                            {
                                NewHeight = startWindowSize.Height - (startMousePoint.Y - pointAPI.Y) + target.Top <= workAreaMaxHeight ? startWindowSize.Height - (startMousePoint.Y - pointAPI.Y) : workAreaMaxHeight - target.Top;
                            }
                            else
                            {
                                NewHeight = startWindowSize.Height - (startMousePoint.Y - pointAPI.Y);
                            }
                        }
                    }
                    else if (startWindowSize.Height - (startMousePoint.Y - pointAPI.Y) >= target.MinHeight)
                    {
                        if (workAreaMaxHeight > 0)
                        {
                            NewHeight = startWindowSize.Height - (startMousePoint.Y - pointAPI.Y) + target.Top <= workAreaMaxHeight ? startWindowSize.Height - (startMousePoint.Y - pointAPI.Y) : workAreaMaxHeight - target.Top;
                        }
                        else
                        {
                            NewHeight = startWindowSize.Height - (startMousePoint.Y - pointAPI.Y);
                        }
                    }
                    else
                    {
                        NewHeight = target.MinHeight;
                    }
                }

                if (resizeLeft)
                {
                    if (target.Width == target.MinWidth)
                    {
                        if (startMousePoint.X > pointAPI.X)
                        {
                            NewWidth = startWindowSize.Width + startMousePoint.X - pointAPI.X;
                            NewLeft = startWindowLeftUpPoint.x - (startMousePoint.X - pointAPI.X);
                        }
                        else
                        {
                            NewWidth = target.MinWidth;
                            NewLeft = startWindowLeftUpPoint.x + startWindowSize.Width - target.Width;
                        }
                    }
                    else if (startWindowSize.Width + (startMousePoint.X - pointAPI.X) >= target.MinWidth)
                    {
                        NewWidth = startWindowSize.Width + (startMousePoint.X - pointAPI.X);
                        NewLeft = startWindowLeftUpPoint.x - (startMousePoint.X - pointAPI.X);
                    }
                    else
                    {
                        NewWidth = target.MinWidth;
                        NewLeft = startWindowLeftUpPoint.x + startWindowSize.Width - target.Width;
                    }
                }

                if (resizeUp)
                {
                    if (target.Height == target.MinHeight)
                    {
                        if (startMousePoint.Y > pointAPI.Y)
                        {
                            NewHeight = startWindowSize.Height + startMousePoint.Y - pointAPI.Y;
                            NewTop = startWindowLeftUpPoint.y - (startMousePoint.Y - pointAPI.Y);
                        }
                        else
                        {
                            NewHeight = target.MinHeight;
                            NewTop = startWindowLeftUpPoint.y + startWindowSize.Height - target.Height;
                        }
                    }
                    else if (startWindowSize.Height + (startMousePoint.Y - pointAPI.Y) >= target.MinHeight)
                    {
                        NewHeight = startWindowSize.Height + startMousePoint.Y - pointAPI.Y;
                        NewTop = startWindowLeftUpPoint.y - (startMousePoint.Y - pointAPI.Y);
                    }
                    else
                    {
                        NewHeight = target.MinHeight;
                        NewTop = startWindowLeftUpPoint.y + startWindowSize.Height - target.Height;
                    }
                }

                if (NewWidth > 10d && Math.Abs(NewWidth - target.Width) > 0.7d)
                    target.Width = NewWidth;
                if (NewHeight > 10d && Math.Abs(NewHeight - target.Height) > 0.7d)
                    target.Height = NewHeight;
                if (NewLeft > -9999 && Math.Abs(NewLeft - target.Left) > 0.7d)
                    target.Left = NewLeft;
                if (NewTop > -9999 && Math.Abs(NewTop - target.Top) > 0.7d)
                    target.Top = NewTop;
            }
            catch
            {
            }
        }

        private void updateMouseDown()
        {
            bool flag = (GetAsyncKeyState(0x1) & 0x8000) == 0; // 调用原生API判断鼠标是否抬起，如果使用WPF的API的话鼠标不在窗口上时不会更新状态 (#5655)
            if (flag)
            {
                resizeRight = false;
                resizeLeft = false;
                resizeUp = false;
                resizeDown = false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out PointAPI lpPoint);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}