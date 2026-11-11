using System;
// 来源：https://blog.csdn.net/simpleman2000/article/details/140952294

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace PCL
{

    public class DragHelper
    {

        public event EventHandler DragDrop;

        public string[] DropFilePaths
        {
            get
            {
                return _DropFilePathsBackingField;
            }
            private set
            {
                _DropFilePathsBackingField = value;
            }
        }
        private string[] _DropFilePathsBackingField;

        public POINT DropPoint
        {
            get
            {
                return _DropPointBackingField;
            }
            private set
            {
                _DropPointBackingField = value;
            }
        }
        private POINT _DropPointBackingField;

        public HwndSource HwndIntPtrSource { get; set; }

        public void AddHook()
        {
            RemoveDragHook();
            HwndIntPtrSource.AddHook(WndProc);
            var handle = HwndIntPtrSource.Handle;
            if (IsUserAnAdmin())
                RevokeDragDrop(handle);
            DragAcceptFiles(handle, true);
            ChangeMessageFilter(handle);
        }

        public void RemoveDragHook()
        {
            HwndIntPtrSource.RemoveHook(WndProc);
            DragAcceptFiles(HwndIntPtrSource.Handle, false);
        }

        private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            string[] filePaths = null;
            var point = new POINT();

            if (TryGetDropInfo(msg, wParam, ref filePaths, ref point))
            {
                DropPoint = point;
                DropFilePaths = filePaths;
                DragDrop?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            return nint.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(nint hWnd, uint msg, uint action, ref CHANGEFILTERSTRUCT pChangeFilterStruct);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint msg, uint flags);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(nint hWnd, bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(nint hWnd, uint iFile, StringBuilder lpszFile, int cch);

        [DllImport("shell32.dll")]
        private static extern bool DragQueryPoint(nint hDrop, ref POINT lppt);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(nint hDrop);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(nint hWnd);

        [DllImport("shell32.dll")]
        private static extern bool IsUserAnAdmin();

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CHANGEFILTERSTRUCT
        {
            public uint cbSize;
            public uint ExtStatus;
        }

        private const uint WM_COPYGLOBALDATA = 73U;
        private const uint WM_COPYDATA = 74U;
        private const uint WM_DROPFILES = 563U;
        private const uint MSGFLT_ALLOW = 1U;
        private const uint MSGFLT_ADD = 1U;
        private const int MAX_PATH = 260;

        private static void ChangeMessageFilter(nint handle)
        {
            var ver = Environment.OSVersion.Version;
            bool isVistaOrHigher = ver >= new Version(6, 0);
            bool isNt61OrHiger = ver >= new Version(6, 1);

            if (isVistaOrHigher)
            {
                var status = new CHANGEFILTERSTRUCT() { cbSize = 8U };
                foreach (uint msg in new uint[] { WM_DROPFILES, WM_COPYGLOBALDATA, WM_COPYDATA })
                {
                    bool error = false;
                    if (isNt61OrHiger)
                    {
                        error = !ChangeWindowMessageFilterEx(handle, msg, MSGFLT_ALLOW, ref status);
                    }
                    else
                    {
                        error = !ChangeWindowMessageFilter(msg, MSGFLT_ADD);
                    }

                    if (error)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        private static bool TryGetDropInfo(int msg, nint wParam, ref string[] dropFilePaths, ref POINT dropPoint)
        {
            dropFilePaths = null;
            dropPoint = new POINT();

            if (msg != WM_DROPFILES)
                return false;

            uint fileCount = DragQueryFile(wParam, uint.MaxValue, null, 0);
            dropFilePaths = new string[((int)fileCount)];

            for (uint i = 0U, loopTo = (uint)((int)fileCount - 1); i <= loopTo; i++)
            {
                var sb = new StringBuilder(MAX_PATH);
                uint result = DragQueryFile(wParam, i, sb, sb.Capacity);
                if (result > 0L)
                    dropFilePaths[(int)i] = sb.ToString();
            }

            DragQueryPoint(wParam, ref dropPoint);
            DragFinish(wParam);
            return true;
        }

    }
}