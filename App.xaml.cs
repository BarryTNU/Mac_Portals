


#if WINDOWS
using Portals.Services;
using Portals.Platforms.Windows.Desktop;
using Portals.Pages;
using Microsoft.Maui.Storage;
using System.Runtime.InteropServices;
#endif

namespace Portals
{
    public partial class App : Application
    {
#if WINDOWS
        private readonly DesktopInteractionService _desktopInteractionService;
        private readonly PortalManager _portalManager;
        private readonly DesktopPortalManager _desktopPortalManager;
        private bool _portalsLoaded;

        // Windows extended window styles
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        // SetWindowPos flags
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(
            IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(
            IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X, int Y,
            int cx, int cy,
            uint uFlags);

        // Hide the MAUI host window from the taskbar and Alt+Tab.
        private static void HideHostWindow(
            Microsoft.UI.Xaml.Window nativeWindow)
        {
            IntPtr hwnd =
                WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

            int extendedStyle =
                GetWindowLong(hwnd, GWL_EXSTYLE);

            extendedStyle |= WS_EX_TOOLWINDOW;
            extendedStyle &= ~WS_EX_APPWINDOW;

            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                SWP_NOMOVE |
                SWP_NOSIZE |
                SWP_NOZORDER |
                SWP_NOACTIVATE |
                SWP_FRAMECHANGED);

            nativeWindow.AppWindow.Hide();
        }
#endif

        public App(
#if WINDOWS
            DesktopInteractionService desktopInteractionService,
            PortalManager portalManager,
            DesktopPortalManager desktopPortalManager
#endif
            )
        {
            InitializeComponent();

#if WINDOWS
            _desktopInteractionService = desktopInteractionService;
            _portalManager = portalManager;
            _desktopPortalManager = desktopPortalManager;

            _desktopInteractionService.Start();
#endif
        }

        protected override Window CreateWindow(
            IActivationState? activationState)
        {
            var window = new Window(new ContentPage
            {
                Title = "Portals"
            });

#if WINDOWS
            window.Created += async (sender, args) =>
            {
                if (window.Handler?.PlatformView
                    is Microsoft.UI.Xaml.Window nativeWindow)
                {
                    nativeWindow.DispatcherQueue.TryEnqueue(async () =>
                    {
                        // Remove the host window from Windows UI.
                        HideHostWindow(nativeWindow);

                        if (_portalsLoaded)
                            return;

                        _portalsLoaded = true;

                        try
                        {
                            await _portalManager.LoadPortalsAsync();

                            foreach (var portal in _portalManager.Portals)
                            {
                                _desktopPortalManager.ShowPortal(portal);
                            }

                            // Display manual on first run only.
                            ShowManualOnFirstRun();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"Portal startup error: {ex}");
                        }
                    });
                }
            };
#endif

            return window;
        }

        private void ShowManualOnFirstRun()
        {
            bool manualShown =
                Preferences.Get("ManualShown", false);

            if (!manualShown)
            {
                var manualWindow =
                    new Window(new ManualPage())
                    {
                        Title = "Portals - User Manual",
                        Width = 750,
                        Height = 650
                    };

                OpenWindow(manualWindow);

                Preferences.Set("ManualShown", true);
            }
        }
    }
}
