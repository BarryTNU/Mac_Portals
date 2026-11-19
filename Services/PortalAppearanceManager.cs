using System;
using System.Runtime.InteropServices;

namespace Portals.Services;

public class PortalAppearanceManager
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(
        IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(
        IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags);

   
    public void ApplyOpacity(IntPtr hwnd, double opacity)
    {
        if (hwnd == IntPtr.Zero)
            return;

        // Ensure the window supports layered transparency.
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if ((extendedStyle & WS_EX_LAYERED) == 0)
        {
            SetWindowLong(
                hwnd,
                GWL_EXSTYLE,
                extendedStyle | WS_EX_LAYERED);
        }

        // Convert 0-1 opacity to Windows alpha (0-255).
        byte alpha = (byte)Math.Clamp(
            (int)Math.Round(opacity * 255), 0, 255);

        SetLayeredWindowAttributes(
            hwnd, 0, alpha, LWA_ALPHA);
    }
     


}
