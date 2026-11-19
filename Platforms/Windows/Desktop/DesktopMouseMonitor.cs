
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Portals.Platforms.Windows.Desktop
{
    public class DesktopMouseMonitor : IDisposable
    {
        // Windows mouse hook constants
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;

        // Windows ListView constants
        private const uint LVM_HITTEST = 0x1000 + 18;

        private IntPtr _hookHandle = IntPtr.Zero;

        private readonly LowLevelMouseProc _hookCallback;

        private bool _isSelecting;
        private DesktopSelectionWindow? _selectionWindow;

      private int _startX;
        private int _startY;
       private int _currentX;
        private int _currentY;

        public event Action<double, double, double, double>?
            RectangleSelected;

        public DesktopMouseMonitor()
        {
            _hookCallback = MouseHookCallback;
        }



        // ==========================================
        // START AND STOP MONITOR
        // ==========================================

        public void Start()
        {
            if (_hookHandle != IntPtr.Zero)
                return;

            _hookHandle = SetWindowsHookEx(
                WH_MOUSE_LL,
                _hookCallback,
                GetModuleHandle(null),
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Unable to install the global mouse hook. " +
                    $"Windows error: {Marshal.GetLastWin32Error()}");
            }
        }

        public void Stop()
        {
            if (_hookHandle == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _isSelecting = false;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        // ==========================================
        // GLOBAL MOUSE HOOK
        // ==========================================


        private IntPtr MouseHookCallback(
            int nCode,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();

                var mouse =
                    Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                int x = mouse.pt.X;
                int y = mouse.pt.Y;

                // Start selection on empty desktop background.
                if (message == WM_LBUTTONDOWN && !_isSelecting)
                {
                    if (IsEmptyDesktopPoint(x, y))
                    {
                        try
                        {
                            _startX = x;
                            _startY = y;

                            _currentX = x;
                            _currentY = y;

                            _selectionWindow =
                                new DesktopSelectionWindow();

                            _selectionWindow.RectangleSelected +=
      (sx, sy, width, height) =>
      {
          // Raise the event outside the mouse-hook callback.
          ThreadPool.QueueUserWorkItem(_ =>
          {
              RectangleSelected?.Invoke(
                  sx, sy, width, height);
          });
      };

                            _isSelecting = true;

                            _selectionWindow.BeginSelection(x, y);

                           //return (IntPtr)1;
                        }
                        catch
                        {
                            _isSelecting = false;
                            _selectionWindow = null;
                        }
                    }
                }
                // Update rectangle while dragging.
                else if (message == WM_MOUSEMOVE && _isSelecting)
                {
                    _currentX = x;
                    _currentY = y;

                    _selectionWindow?.UpdateSelection(x, y);

                   // return (IntPtr)1;
                }
                // Finish selection on mouse release.
                else if (message == WM_LBUTTONUP && _isSelecting)
                {
                    _currentX = x;
                    _currentY = y;

                    _isSelecting = false;

                    var selection = _selectionWindow;
                    _selectionWindow = null;

                    selection?.CompleteSelection(x, y);

                   // return (IntPtr)1;
                }
            }

            return CallNextHookEx(
                _hookHandle,
                nCode,
                wParam,
                lParam);
        }


        // ==========================================
        // CHECK FOR EMPTY DESKTOP BACKGROUND
        // ==========================================


        private bool IsEmptyDesktopPoint(int x, int y)
        {
            POINT point = new POINT { X = x, Y = y };

            IntPtr hwnd = WindowFromPoint(point);

            if (hwnd == IntPtr.Zero)
                return false;

            // Find the desktop icon ListView.
            IntPtr listView = IntPtr.Zero;
            IntPtr current = hwnd;

            while (current != IntPtr.Zero)
            {
                string className = GetWindowClassName(current);

                if (className == "SysListView32")
                {
                    IntPtr parent = GetParent(current);

                    if (GetWindowClassName(parent) == "SHELLDLL_DefView")
                    {
                        listView = current;
                        break;
                    }
                }

                current = GetParent(current);
            }

            if (listView == IntPtr.Zero)
                return false;

            POINT clientPoint = point;

            if (!ScreenToClient(listView, ref clientPoint))
                return false;

            return IsEmptyListViewPoint(listView, clientPoint);
        }

        private bool IsEmptyListViewPoint(IntPtr listView, POINT point)
        {
            GetWindowThreadProcessId(listView, out uint processId);

            if (processId == 0)
                return false;

            const uint PROCESS_VM_OPERATION = 0x0008;
            const uint PROCESS_VM_READ = 0x0010;
            const uint PROCESS_VM_WRITE = 0x0020;
            const uint PROCESS_QUERY_INFORMATION = 0x0400;

            IntPtr process = OpenProcess(
                PROCESS_VM_OPERATION |
                PROCESS_VM_READ |
                PROCESS_VM_WRITE |
                PROCESS_QUERY_INFORMATION,
                false,
                processId);

            if (process == IntPtr.Zero)
                return false;

            IntPtr remoteMemory = IntPtr.Zero;
            IntPtr localMemory = IntPtr.Zero;

            try
            {
                int size = Marshal.SizeOf<LVHITTESTINFO>();

                remoteMemory = VirtualAllocEx(
                    process,
                    IntPtr.Zero,
                    (UIntPtr)size,
                    0x3000,       // MEM_COMMIT | MEM_RESERVE
                    0x04);        // PAGE_READWRITE

                if (remoteMemory == IntPtr.Zero)
                    return false;

                var hitTest = new LVHITTESTINFO
                {
                    pt = point,
                    flags = 0,
                    iItem = -1,
                    iSubItem = -1,
                    iGroup = -1
                };

                localMemory = Marshal.AllocHGlobal(size);

                Marshal.StructureToPtr(hitTest, localMemory, false);

                byte[] buffer = new byte[size];
                Marshal.Copy(localMemory, buffer, 0, size);

                if (!WriteProcessMemory(
                    process,
                    remoteMemory,
                    buffer,
                    (UIntPtr)size,
                    out _))
                    return false;

                // Ask Explorer to perform the hit test.
                IntPtr result = SendMessageTimeout(
                    listView,
                    LVM_HITTEST,
                    IntPtr.Zero,
                    remoteMemory,
                    0x0002,       // SMTO_ABORTIFHUNG
                    100,
                    out _);

                if (result == IntPtr.Zero)
                    return false;

                if (!ReadProcessMemory(
                    process,
                    remoteMemory,
                    buffer,
                    (UIntPtr)size,
                    out _))
                    return false;

                Marshal.Copy(buffer, 0, localMemory, size);

                hitTest = Marshal.PtrToStructure<LVHITTESTINFO>(
                    localMemory);

                // -1 means no icon is under the pointer.
                return hitTest.iItem == -1;
            }
            finally
            {
                if (localMemory != IntPtr.Zero)
                    Marshal.FreeHGlobal(localMemory);

                if (remoteMemory != IntPtr.Zero)
                {
                    VirtualFreeEx(
                        process,
                        remoteMemory,
                        UIntPtr.Zero,
                        0x8000);  // MEM_RELEASE
                }

                CloseHandle(process);
            }
        }


        private static string GetWindowClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            var builder = new StringBuilder(256);

            GetClassName(hwnd, builder, builder.Capacity);

            return builder.ToString();
        }

        // ==========================================
        // WINDOWS API STRUCTURES
        // ==========================================

        private delegate IntPtr LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVHITTESTINFO
        {
            public POINT pt;
            public uint flags;
            public int iItem;
            public int iSubItem;
            public int iGroup;
        }

        // ==========================================
        // WINDOWS API FUNCTIONS
        // ==========================================

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelMouseProc callback,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(
            IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(
            string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr hwnd,
            StringBuilder className,
            int maxCount);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(
            IntPtr hwnd,
            ref POINT point);

        [DllImport("user32.dll")]
        private static extern int SendMessage(
            IntPtr hwnd,
            uint msg,
            IntPtr wParam,
            ref LVHITTESTINFO lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hwnd,
            out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint access,
            bool inheritHandle,
            uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr process,
            IntPtr address,
            UIntPtr size,
            uint allocationType,
            uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(
            IntPtr process,
            IntPtr address,
            UIntPtr size,
            uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr process,
            IntPtr address,
            byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr address,
            byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hwnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);


    }
}

