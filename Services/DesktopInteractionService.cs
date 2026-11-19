
#if WINDOWS

using Portals.Platforms.Windows.Desktop;
using System.Runtime.InteropServices;

namespace Portals.Services
{
    public class DesktopInteractionService
    {
        private readonly DesktopMouseMonitor _mouseMonitor;
        private readonly PortalManager _portalManager;
        private readonly DesktopPortalManager _desktopPortalManager;

        private bool _started;

        public DesktopInteractionService(
            DesktopMouseMonitor mouseMonitor,
            PortalManager portalManager,
            DesktopPortalManager desktopPortalManager)
        {
            _mouseMonitor = mouseMonitor;
            _portalManager = portalManager;
            _desktopPortalManager = desktopPortalManager;

            _mouseMonitor.RectangleSelected += OnRectangleSelected;
        }

        // Windows API declarations

              [DllImport("user32.dll", EntryPoint = "MessageBoxW",
            CharSet = CharSet.Unicode, ExactSpelling = true)]
                private static extern int NativeMessageBox(
            IntPtr hWnd,
            string text,
            string caption,
            uint type);

    private const uint MB_DEFBUTTON2 = 0x00000100;

        

        private const uint MB_YESNO = 0x00000004;
        private const uint MB_ICONQUESTION = 0x00000020;
        private const uint MB_TOPMOST = 0x00040000;
        private const uint MB_SETFOREGROUND = 0x00010000;

        private const int IDYES = 6;

        // Start monitoring

        public void Start()
        {
            if (_started)
                return;

            _mouseMonitor.Start();
            _started = true;
        }

        // Stop monitoring

        public void Stop()
        {
            if (!_started)
                return;

            _mouseMonitor.Stop();
            _started = false;
        }
// Handle completed desktop rectangle selection

private void OnRectangleSelected(
    double x,
    double y,
    double width,
    double height)
{
    MainThread.BeginInvokeOnMainThread(async () =>
    {
        try
        {
            // Create the Portal with an empty title.
            var portal = await _portalManager
                .AddPortalFromRectangleAsync(
                    "",
                    x,
                    y,
                    width,
                    height);

            // Display the new Portal.
            _desktopPortalManager.ShowPortal(portal);
        }
        catch (Exception ex)
        {
            var app = Application.Current;

        var page = app?.Windows.Count > 0
            ? app.Windows[0].Page
            : null;

            if (page != null)
            {
                await page.DisplayAlertAsync(
                    "Portal Error",
                    ex.Message,
                    "OK");
            }
        }
    });
}
                   
                }
            }

#endif
