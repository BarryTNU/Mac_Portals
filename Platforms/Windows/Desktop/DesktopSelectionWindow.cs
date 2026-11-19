

using System;
using System.Runtime.InteropServices;

namespace Portals.Platforms.Windows.Desktop
{
    public class DesktopSelectionWindow
    {
        // ==========================================
        // WINDOWS CONSTANTS
        // ==========================================

        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;

       // private const uint LWA_COLORKEY = 0x00000001;
        private const uint LWA_ALPHA = 0x00000002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const uint WM_PAINT = 0x000F;
        private const uint WM_DESTROY = 0x0002;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_KEYDOWN = 0x0100;

        private const int VK_ESCAPE = 0x1B;
        private const int MK_LBUTTON = 0x0001;

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        private const int PS_SOLID = 0;
        private const int NULL_BRUSH = 5;
        private const int TRANSPARENT = 1;

        private static readonly IntPtr HWND_TOPMOST = new(-1);

        private const string WindowClassName = "PortalsDesktopSelection";

        // ==========================================
        // WINDOWS API DEFINITIONS
        // ==========================================

        private delegate IntPtr WndProcDelegate(
            IntPtr hwnd, uint msg,
            IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public int fErase;
            public RECT rcPaint;
            public int fRestore;
            public int fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        [DllImport("user32.dll",
       CharSet = CharSet.Unicode,
       SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int exStyle, string className, string windowName,
            int style, int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance,
            IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hwnd, IntPtr after,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(
            IntPtr hwnd, out PAINTSTRUCT ps);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(
            IntPtr hwnd, ref PAINTSTRUCT ps);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(
            IntPtr hwnd, IntPtr rect, bool erase);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint colour);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreatePen(
            int style, int width, uint colour);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int index);

        [DllImport("gdi32.dll", EntryPoint = "Rectangle")]
        private static extern bool DrawRectangle(
            IntPtr hdc, int left, int top, int right, int bottom);

        [DllImport("user32.dll", EntryPoint = "FillRect", SetLastError = true)]
        private static extern int FillRect(
     IntPtr hdc,
     ref RECT rect,
     IntPtr hbr);

        // ==========================================
        // INSTANCE DATA
        // ==========================================

        private static readonly WndProcDelegate WindowProcDelegate =
            WindowProc;

        private static DesktopSelectionWindow? _current;

        private IntPtr _hwnd;

        private int _screenX;
        private int _screenY;

        private int _startX;
        private int _startY;

        private int _currentX;
        private int _currentY;

        private bool _isDrawing;

        private bool _completed;

        public event Action<double, double, double, double>?
            RectangleSelected;

        public event Action? Cancelled;

        // ==========================================
        // START SELECTION
        // ==========================================

        public void Activate(bool takeFocus = true)
        {
            if (_current != null)
            {
                throw new InvalidOperationException(
                    $"A desktop selection is already active. " +
                    $"Existing HWND: {_current._hwnd}");
            }

            _current = this;

            try
            {
                RegisterWindowClass();

                _screenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
                _screenY = GetSystemMetrics(SM_YVIRTUALSCREEN);

                int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

                IntPtr instance = GetModuleHandle(null);

                _hwnd = CreateWindowEx(
                    WS_EX_LAYERED |
                    WS_EX_TOOLWINDOW |
                    WS_EX_TOPMOST,
                    WindowClassName,
                    "Create Portal",
                    WS_POPUP,
                    _screenX,
                    _screenY,
                    width,
                    height,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    instance,
                    IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "Unable to create the desktop selection window.");

                // Magenta is the transparent colour.
                SetLayeredWindowAttributes(
     _hwnd,
     0,
     100,
     LWA_ALPHA);

                SetWindowPos(
                    _hwnd,
                    HWND_TOPMOST,
                    _screenX,
                    _screenY,
                    width,
                    height,
                    SWP_SHOWWINDOW);

                ShowWindow(_hwnd, 5);
                UpdateWindow(_hwnd);

                if (takeFocus)
                {
                    SetForegroundWindow(_hwnd);
                    SetFocus(_hwnd);
                }
            }
            catch
            {
                _current = null;

                if (_hwnd != IntPtr.Zero)
                {
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }

                throw;
            }
        }


        // ==========================================
        // CONTROL SELECTION FROM GLOBAL MOUSE HOOK
        // ==========================================

        public void BeginSelection(int screenX, int screenY)
        {
            // Create and display the selection window.
            Activate(false);

            // Convert screen coordinates to window coordinates.
            _startX = screenX - _screenX;
            _startY = screenY - _screenY;

            _currentX = _startX;
            _currentY = _startY;

            _isDrawing = true;

            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }

        public void UpdateSelection(int screenX, int screenY)
        {
            if (!_isDrawing || _hwnd == IntPtr.Zero)
                return;

            _currentX = screenX - _screenX;
            _currentY = screenY - _screenY;

            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }

        public void CompleteSelection(int screenX, int screenY)
        {
            if (!_isDrawing || _hwnd == IntPtr.Zero)
                return;

            _currentX = screenX - _screenX;
            _currentY = screenY - _screenY;

            FinishSelection();
        }


        // ==========================================
        // REGISTER WINDOW CLASS
        // ==========================================

        private static void RegisterWindowClass()
        {
            IntPtr instance = GetModuleHandle(null);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = WindowProcDelegate,
                hInstance = instance,
                lpszClassName = WindowClassName
            };

            if (RegisterClassEx(ref wc) == 0)
            {
                int error = Marshal.GetLastWin32Error();

                // Class may already have been registered.
                if (error != 1410)
                    throw new InvalidOperationException(
                        $"Unable to register selection window. Error: {error}");
            }
        }

        // ==========================================
        // WINDOW MESSAGE HANDLER
        // ==========================================

        private static IntPtr WindowProc(
            IntPtr hwnd, uint msg,
            IntPtr wParam, IntPtr lParam)
        {
            var instance = _current;

            if (instance == null)
                return IntPtr.Zero;

            switch (msg)
            {
                case WM_LBUTTONDOWN:
                    {
                        int x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                        int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                        instance._startX = x;
                        instance._startY = y;

                        instance._currentX = x;
                        instance._currentY = y;

                        instance._isDrawing = true;

                        SetCapture(hwnd);
                        InvalidateRect(hwnd, IntPtr.Zero, false);

                        return IntPtr.Zero;
                    }

                case WM_MOUSEMOVE:
                    {
                        if (instance._isDrawing)
                        {
                            long value = lParam.ToInt64();

                            instance._currentX =
                                unchecked((short)(value & 0xFFFF));

                            instance._currentY =
                                unchecked((short)((value >> 16) & 0xFFFF));

                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }

                        return IntPtr.Zero;
                    }

                case WM_LBUTTONUP:
                    {
                        if (instance._isDrawing)
                        {
                            long value = lParam.ToInt64();

                            instance._currentX =
                                unchecked((short)(value & 0xFFFF));

                            instance._currentY =
                                unchecked((short)((value >> 16) & 0xFFFF));

                            instance.FinishSelection();
                        }

                        return IntPtr.Zero;
                    }

                case WM_KEYDOWN:
                    {
                        if (wParam.ToInt32() == VK_ESCAPE)
                        {
                            instance.CancelSelection();
                        }

                        return IntPtr.Zero;
                    }

                case WM_PAINT:
                    instance.Paint(hwnd);
                    return IntPtr.Zero;

                case WM_DESTROY:
                    _current = null;
                    instance._hwnd = IntPtr.Zero;
                    return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(
            IntPtr hwnd, uint msg,
            IntPtr wParam, IntPtr lParam);

        // ==========================================
        // DRAW SELECTION
        // ==========================================

        private void Paint(IntPtr hwnd)
        {
            IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);

            // Fill background with transparent colour.
            var rect = new RECT
            {
                left = 0,
                top = 0,
                right = GetSystemMetrics(SM_CXVIRTUALSCREEN),
                bottom = GetSystemMetrics(SM_CYVIRTUALSCREEN)
            };

            //  IntPtr background = CreateSolidBrush(0x00FF00FF);

            IntPtr background = CreateSolidBrush(0x00202020);

            int fillResult = FillRect(hdc, ref rect, background);

            if (fillResult == 0)
            {
                System.Diagnostics.Debug.WriteLine("FillRect failed.");
            }

            DeleteObject(background);

            DeleteObject(background);

            if (_isDrawing)
            {
                int left = Math.Min(_startX, _currentX);
                int top = Math.Min(_startY, _currentY);
                int right = Math.Max(_startX, _currentX);
                int bottom = Math.Max(_startY, _currentY);

                IntPtr pen = CreatePen(
                    PS_SOLID, 3, 0x00FF901E);

                IntPtr oldPen = SelectObject(hdc, pen);
                IntPtr oldBrush = SelectObject(
                    hdc, GetStockObject(NULL_BRUSH));

                int previousMode = SetBkMode(hdc, TRANSPARENT);

                if (previousMode == 0)
                {
                    System.Diagnostics.Debug.WriteLine("SetBkMode failed.");
                }

                DrawRectangle(
                    hdc, left, top, right, bottom);

                SelectObject(hdc, oldBrush);
                SelectObject(hdc, oldPen);

                DeleteObject(pen);
            }

            EndPaint(hwnd, ref ps);
        }

        // ==========================================
        // COMPLETE SELECTION
        // ==========================================

        private void FinishSelection()
        {
            if (_completed)
                return;

            _isDrawing = false;

            ReleaseCapture();

            int left = Math.Min(_startX, _currentX);
            int top = Math.Min(_startY, _currentY);

            int width = Math.Abs(_currentX - _startX);
            int height = Math.Abs(_currentY - _startY);

            if (width < 50 || height < 50)
            {
                CancelSelection();
                return;
            }

            _completed = true;

            double x = _screenX + left;
            double y = _screenY + top;

            var callback = RectangleSelected;

            CloseWindow();

            callback?.Invoke(x, y, width, height);
        }

        // ==========================================
        // CANCEL SELECTION
        // ==========================================

        private void CancelSelection()
        {
            if (_completed)
                return;

            _completed = true;

            var callback = Cancelled;

            CloseWindow();

            callback?.Invoke();
        }

        // ==========================================
        // CLOSE WINDOW
        // ==========================================

        private void CloseWindow()
        {
            if (_hwnd != IntPtr.Zero)
            {
                IntPtr hwnd = _hwnd;
                _hwnd = IntPtr.Zero;

                ReleaseCapture();
                DestroyWindow(hwnd);
            }

            if (_current == this)
                _current = null;
        }
    }
}

