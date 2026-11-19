
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.System;
using Portals.Models;
using Portals.Services;
using System.Threading;
using Portals.Pages;


namespace Portals.Platforms.Windows.Desktop
{
    public class DesktopPortalManager(PortalManager portalManager)
    {
        // Tracks a shortcut while it is being moved between Portal windows.
        private sealed class PendingShortcutTransfer
        {
            public Portal SourcePortal { get; set; } = null!;
            public PortalShortcut Shortcut { get; set; } = null!;
            public Microsoft.UI.Xaml.Controls.Canvas SourceCanvas { get; set; } = null!;
            public Microsoft.UI.Xaml.Controls.Border SourceTile { get; set; } = null!;
            public int SourceIndex { get; set; }
            public bool Completed { get; set; }
        }

        private const string ShortcutTransferFormat =
            "application/x-portals-shortcut";

        private readonly Dictionary<string, PendingShortcutTransfer>
            _pendingShortcutTransfers = new();

        // Prevent the source Portal from auto-collapsing during a native drag.
        private bool _activeShortcutDrag;

        private readonly Dictionary<Guid, Microsoft.UI.Xaml.Window>
            _windows = new();

        private readonly Dictionary<Guid, CancellationTokenSource>
    _saveTokens = new();



        // Windows extended window styles
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        // Window positioning flags
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X, int Y,
            int cx, int cy,
            uint uFlags);

        // Remove a Portal window from the taskbar and Alt+Tab.
        private static void HideFromTaskbar(
            Microsoft.UI.Xaml.Window window)
        {
            IntPtr hwnd =
                WinRT.Interop.WindowNative.GetWindowHandle(window);

            int style = GetWindowLong(hwnd, GWL_EXSTYLE);

            style |= WS_EX_TOOLWINDOW;
            style &= ~WS_EX_APPWINDOW;

            SetWindowLong(hwnd, GWL_EXSTYLE, style);

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                SWP_NOMOVE |
                SWP_NOSIZE |
                SWP_NOZORDER |
                SWP_NOACTIVATE |
                SWP_FRAMECHANGED);
        }



        private readonly PortalManager _portalManager = portalManager;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const uint MB_YESNO = 0x00000004;
        private const uint MB_ICONQUESTION = 0x00000020;
        private const uint MB_ICONINFORMATION = 0x00000040;
        private const uint MB_OK = 0x00000000;
        private const uint MB_DEFBUTTON2 = 0x00000100;
        private const uint MB_TOPMOST = 0x00040000;
        private const uint MB_SETFOREGROUND = 0x00010000;

        private const int IDYES = 6;

        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();



        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr hWnd,
            System.Text.StringBuilder className,
            int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(
    IntPtr hWnd,
    int msg,
    IntPtr wParam,
    IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "MessageBoxW",
         CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int NativeMessageBox(
            IntPtr hWnd,
            string text,
            string caption,
            uint type);

        [System.Runtime.InteropServices.StructLayout(
    System.Runtime.InteropServices.LayoutKind.Sequential,
    CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.ByValTStr,
                SizeConst = 260)]
            public string szDisplayName;

            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.ByValTStr,
                SizeConst = 80)]
            public string szTypeName;
        }

        [System.Runtime.InteropServices.DllImport(
            "shell32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            out SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        [System.Runtime.InteropServices.StructLayout(
    System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        public bool KeepVisible { get; set; } = false;

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc,
            ref BITMAPINFOHEADER info,
            uint usage,
            out IntPtr bits,
            IntPtr section,
            uint offset);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr hdc, IntPtr obj);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DrawIconEx(
            IntPtr hdc,
            int x,
            int y,
            IntPtr hIcon,
            int width,
            int height,
            uint step,
            IntPtr flickerFreeDraw,
            uint flags);


        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ICONINFO
        {
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.Bool)]
            public bool fIcon;

            public uint xHotspot;
            public uint yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }


        [System.Runtime.InteropServices.StructLayout(
            System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }


        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetIconInfo(
            IntPtr hIcon,
            out ICONINFO piconinfo);


        [System.Runtime.InteropServices.DllImport(
            "gdi32.dll",
            EntryPoint = "GetObjectW",
            SetLastError = true)]
        private static extern int GetObject(
            IntPtr h,
            int c,
            out BITMAP pv);




        public void ShowPortal(Portal portal)
        {
            if (_windows.ContainsKey(portal.Id))
                return;


            var window = new Microsoft.UI.Xaml.Window
            {
                Title = portal.Title
            };

            window.Content = CreatePortalContent(portal, window);

            window.ExtendsContentIntoTitleBar = true;


            var appWindow = window.AppWindow;

            if (appWindow.Presenter
                is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(true, false);
            }


            appWindow.TitleBar.ButtonBackgroundColor =
                Microsoft.UI.Colors.Transparent;

            appWindow.Resize(
    new global::Windows.Graphics.SizeInt32(
        (int)portal.Width,
        portal.IsCollapsed ? 38 : (int)portal.Height));

            appWindow.Move(new PointInt32(
                (int)portal.X,
                (int)portal.Y));

            // Monitor Portal movement and resizing.
            appWindow.Changed += (sender, args) =>
            {
                if (args.DidPositionChange || args.DidSizeChange)
                {
                    ScheduleGeometrySave(portal, sender);
                }
            };

            window.Closed += async (s, e) =>
            {
                if (_saveTokens.TryGetValue(portal.Id, out var pending))
                {
                    pending.Cancel();
                    _saveTokens.Remove(portal.Id);
                }

                // Capture the final window dimensions and position.
                portal.X = appWindow.Position.X;
                portal.Y = appWindow.Position.Y;
                portal.Width = appWindow.Size.Width;

                // Preserve the expanded height while the Portal is rolled up.
                if (!portal.IsCollapsed)
                    portal.Height = appWindow.Size.Height;

                await _portalManager.SavePortalsAsync();

                _windows.Remove(portal.Id);
            };

            _windows.Add(portal.Id, window);

            // Remove this Portal from the Windows taskbar.
            HideFromTaskbar(window);

            // Display the Portal normally.
            window.Activate();
        }

        private void ScheduleGeometrySave(
    Portal portal,
    AppWindow appWindow)
        {
            if (_saveTokens.TryGetValue(portal.Id, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            var cts = new CancellationTokenSource();
            _saveTokens[portal.Id] = cts;

            _ = SaveGeometryAfterDelayAsync(portal, appWindow, cts);
        }

        private async Task SaveGeometryAfterDelayAsync(
            Portal portal,
            AppWindow appWindow,
            CancellationTokenSource cts)
        {
            try
            {
                // Wait until the user has stopped moving or resizing.
                await Task.Delay(500, cts.Token);

                portal.X = appWindow.Position.X;
                portal.Y = appWindow.Position.Y;
                portal.Width = appWindow.Size.Width;

                // Preserve the expanded height while the Portal is rolled up.
                if (!portal.IsCollapsed)
                    portal.Height = appWindow.Size.Height;

                await _portalManager.SavePortalsAsync();
            }
            catch (OperationCanceledException)
            {
                // Another movement event has scheduled a newer save.
            }
            finally
            {
                if (_saveTokens.TryGetValue(portal.Id, out var current)
                    && ReferenceEquals(current, cts))
                {
                    _saveTokens.Remove(portal.Id);
                }

                cts.Dispose();
            }
        }

        private Microsoft.UI.Xaml.Controls.Grid CreatePortalContent(
            Portal portal,
            Microsoft.UI.Xaml.Window window)
        {
            var root = new Microsoft.UI.Xaml.Controls.Grid();
            bool activeShortcutContextMenu = false;

            SetPortalBackgroundColour(root, portal.Colour);

            root.RowDefinitions.Add(
                new Microsoft.UI.Xaml.Controls.RowDefinition
                {
                    Height = new Microsoft.UI.Xaml.GridLength(38)
                });

            root.RowDefinitions.Add(
                new Microsoft.UI.Xaml.Controls.RowDefinition
                {
                    Height = new Microsoft.UI.Xaml.GridLength(
                        portal.IsCollapsed ? 0 : 1,
                        portal.IsCollapsed
                            ? Microsoft.UI.Xaml.GridUnitType.Pixel
                            : Microsoft.UI.Xaml.GridUnitType.Star)
                });

            // Title bar
            var titleBar = new Microsoft.UI.Xaml.Controls.Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    ColorHelper.FromArgb(255, 45, 45, 45))
            };

            titleBar.ColumnDefinitions.Add(
                new Microsoft.UI.Xaml.Controls.ColumnDefinition
                {
                    Width = new Microsoft.UI.Xaml.GridLength(
                        1, Microsoft.UI.Xaml.GridUnitType.Star)
                });

            titleBar.ColumnDefinitions.Add(
                new Microsoft.UI.Xaml.Controls.ColumnDefinition
                {
                    Width = new Microsoft.UI.Xaml.GridLength(40)
                });

            // Portal name
            var titleText = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(portal.Title)
                    ? "Enter Title"
                    : portal.Title,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.White),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment =
                    Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment =
                    Microsoft.UI.Xaml.VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            Microsoft.UI.Xaml.Controls.Grid.SetColumnSpan(titleText, 2);

            // Three-dot menu button
            var menuButton = new Microsoft.UI.Xaml.Controls.Button
            {
                Content = "⋮",
                FontSize = 20,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.White),
                Background =
                    new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Transparent),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                HorizontalAlignment =
                    Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment =
                    Microsoft.UI.Xaml.VerticalAlignment.Center,
                Width = 36,
                Height = 34,
                Padding = new Microsoft.UI.Xaml.Thickness(0)
            };

            Microsoft.UI.Xaml.Controls.Grid.SetColumn(menuButton, 1);

            // Start editing the Portal title.
            void BeginTitleEdit(string initialText)
            {
                var editor = new Microsoft.UI.Xaml.Controls.TextBox
                {
                    Text = initialText,
                    FontSize = 16,
                    HorizontalAlignment =
                        Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                    VerticalAlignment =
                        Microsoft.UI.Xaml.VerticalAlignment.Center,
                    TextAlignment =
                        Microsoft.UI.Xaml.TextAlignment.Center,
                    Background =
                        new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            ColorHelper.FromArgb(255, 65, 65, 65)),
                    Foreground =
                        new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.White),
                    BorderThickness =
                        new Microsoft.UI.Xaml.Thickness(0)
                };

                bool finished = false;




                void RestoreTitle()
                {
                    titleBar.Children.Clear();
                    titleBar.Children.Add(titleText);
                    titleBar.Children.Add(menuButton);
                }

                async Task SaveNameAsync()
                {
                    if (finished)
                        return;

                    finished = true;

                    if (!string.IsNullOrWhiteSpace(editor.Text))
                    {
                        portal.Title = editor.Text.Trim();
                        window.Title = portal.Title;

                        await _portalManager.SavePortalsAsync();
                    }

                    titleText.Text = string.IsNullOrWhiteSpace(portal.Title)
                        ? "Enter Title"
                        : portal.Title;

                    RestoreTitle();
                }

                editor.KeyDown += async (s, args) =>
                {
                    if (args.Key == VirtualKey.Enter)
                    {
                        args.Handled = true;
                        await SaveNameAsync();
                    }
                    else if (args.Key == VirtualKey.Escape)
                    {
                        args.Handled = true;

                        if (!finished)
                        {
                            finished = true;
                            RestoreTitle();
                        }
                    }
                };

                editor.LostFocus += async (s, args) =>
                {
                    await SaveNameAsync();
                };

                titleBar.Children.Clear();
                titleBar.Children.Add(editor);

                editor.Focus(
                    Microsoft.UI.Xaml.FocusState.Programmatic);

                editor.SelectAll();
            }

            // Double-click the title bar to rename the Portal.
            titleBar.DoubleTapped += (sender, e) =>
            {
                var point = e.GetPosition(titleBar);

                // Ignore double-clicks in the menu button area.
                if (point.X >= titleBar.ActualWidth - 40)
                    return;

                BeginTitleEdit(portal.Title);
            };

            // Automatically open the title editor for a new Portal.
            if (string.IsNullOrWhiteSpace(portal.Title))
            {
                titleBar.Loaded += (sender, e) =>
                {
                    BeginTitleEdit("");
                };
            }

            // Menu button
            // Menu button action.
            menuButton.Click += (sender, e) =>
            {
                var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();


                var keepVisibleItem =
    new Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem
    {
        Text = "Keep Portal Visible",
        IsChecked = portal.KeepVisible
    };

                keepVisibleItem.Click += async (s, args) =>
                {
                    portal.KeepVisible = keepVisibleItem.IsChecked;

                    if (portal.KeepVisible && portal.IsCollapsed)
                        SetPortalExpanded(true);

                    await _portalManager.SavePortalsAsync();
                };

                menu.Items.Add(keepVisibleItem);



                var transparencyItem =
    new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
    {
        Text = "Change Portal Transparency"
    };

                transparencyItem.Click += (s, args) =>
                {
                    IntPtr hwnd =
                        WinRT.Interop.WindowNative.GetWindowHandle(window);

                    var appearanceManager = new PortalAppearanceManager();

                    var page = new PortalTransparencyPage(
      portal,
      hwnd,
      appearanceManager);


                    var transparencyWindow =
                        new Microsoft.Maui.Controls.Window(page)
                        {
                            Title = "Portal Transparency",
                            Width = 400,
                            Height = 350
                        };

                    if (Application.Current is { } app)
                    {
                        app.OpenWindow(transparencyWindow);
                    }
                };

                menu.Items.Add(transparencyItem);


                // Change Portal Colour
                var colourItem =
                    new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                    {
                        Text = "Change Portal Colour"
                    };

                colourItem.Click += (s, args) =>
                {
                    var page = new PortalColourPage(
                        portal,
                        _portalManager,
                        colour => SetPortalBackgroundColour(root, colour));

                    var colourWindow =
                        new Microsoft.Maui.Controls.Window(page)
                        {
                            Title = "Portal Colour",
                            Width = 400,
                            Height = 430,
                        };

                    Application.Current?.OpenWindow(colourWindow);
                };

                menu.Items.Add(colourItem);


                // User Manual
                var manualItem =
                    new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                    {
                        Text = "User Manual"
                    };

                manualItem.Click += (s, args) =>
                {
                    var manualPage = new ManualPage();

                    var manualWindow =
                        new Microsoft.Maui.Controls.Window(manualPage)
                        {
                            Title = "Portals - User Manual",
                            Width = 750,
                            Height = 650
                        };

                    Application.Current?.OpenWindow(manualWindow);
                };

                menu.Items.Add(manualItem);

                menu.Items.Add(
                    new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());



                //delete portal menu item

                var deleteItem =
                    new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
                    {
                        Text = "Delete Portal"
                    };

                deleteItem.Click += async (s, args) =>
                {



                    IntPtr hwnd =
                        WinRT.Interop.WindowNative.GetWindowHandle(window);

                    int result = NativeMessageBox(
                        hwnd,
                        $"Are you sure you want to delete '{portal.Title}'?",
                        "Delete Portal",
                        MB_YESNO |
                        MB_ICONQUESTION |
                        MB_DEFBUTTON2 |
                        MB_TOPMOST |
                        MB_SETFOREGROUND);

                    if (result == IDYES)
                    {
                        try
                        {
                            await _portalManager.RemovePortalAsync(portal.Id);

                            window.Close();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"PORTAL DELETION FAILED: {ex}");

                            var errorDialog =
                                new Microsoft.UI.Xaml.Controls.ContentDialog
                                {
                                    Title = "Unable to Delete Portal",
                                    Content = ex.Message,
                                    CloseButtonText = "OK",
                                    XamlRoot = root.XamlRoot
                                };

                            await errorDialog.ShowAsync();
                        }
                    }
                };

                menu.Items.Add(deleteItem);
                menu.ShowAt(menuButton);
            };

            // Add title and menu button.
            titleBar.Children.Add(titleText);
            titleBar.Children.Add(menuButton);

            // Drag the Portal using the title bar.
            titleBar.PointerPressed += (sender, e) =>
            {
                var point = e.GetCurrentPoint(titleBar);

                // Do not start dragging when clicking the menu button.
                if (point.Position.X >= titleBar.ActualWidth - 40)
                    return;

                if (point.Properties.IsLeftButtonPressed)
                {
                    IntPtr hwnd =
                        WinRT.Interop.WindowNative.GetWindowHandle(window);

                    ReleaseCapture();

                    SendMessage(
                        hwnd,
                        WM_NCLBUTTONDOWN,
                        (IntPtr)HTCAPTION,
                        IntPtr.Zero);
                }
            };

            root.Children.Add(titleBar);

            // Portal body
            // Portal body
            var body = new Microsoft.UI.Xaml.Controls.Grid();

            Microsoft.UI.Xaml.Controls.Grid.SetRow(body, 1);

            // List displaying shortcuts
            var shortcutCanvas =
    new Microsoft.UI.Xaml.Controls.Canvas
    {
        Margin = new Microsoft.UI.Xaml.Thickness(10)
    };

            body.Children.Add(shortcutCanvas);


            async Task ShowShortcutContextMenuAsync(
          PortalShortcut shortcut,
          Microsoft.UI.Xaml.Controls.Border shortcutTile,
          Microsoft.UI.Xaml.Controls.TextBlock shortcutText)
            {
                string shellPath =
                    !string.IsNullOrWhiteSpace(shortcut.DesktopBackupPath) &&
                    (File.Exists(shortcut.DesktopBackupPath) ||
                     Directory.Exists(shortcut.DesktopBackupPath))
                        ? shortcut.DesktopBackupPath
                        : shortcut.TargetPath;

                if (!File.Exists(shellPath) && !Directory.Exists(shellPath))
                {
                    IntPtr missingHwnd =
                        WinRT.Interop.WindowNative.GetWindowHandle(window);
                    NativeMessageBox(
                        missingHwnd,
                        "The file or folder for this shortcut could not be found.",
                        "Portals",
                        MB_OK | MB_ICONINFORMATION | MB_TOPMOST | MB_SETFOREGROUND);
                    return;
                }

                string? parentDirectory = Path.GetDirectoryName(shellPath);
                if (string.IsNullOrWhiteSpace(parentDirectory) ||
                    !Directory.Exists(parentDirectory))
                    return;

                var pathChange = new TaskCompletionSource<(bool Renamed, string NewPath)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                using var watcher = new FileSystemWatcher(parentDirectory)
                {
                    Filter = "*",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = false
                };

                watcher.Renamed += (s, args) =>
                {
                    if (string.Equals(args.OldFullPath, shellPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        pathChange.TrySetResult((true, args.FullPath));
                    }
                };

                watcher.Deleted += (s, args) =>
                {
                    if (string.Equals(args.FullPath, shellPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        pathChange.TrySetResult((false, string.Empty));
                    }
                };

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                activeShortcutContextMenu = true;

                try
                {
                    watcher.EnableRaisingEvents = true;

                    if (!GetCursorPos(out POINT cursor))
                        return;
                                     

                    // Allow FileSystemWatcher to report Rename/Delete/Move changes.
                    await Task.WhenAny(pathChange.Task, Task.Delay(700));

                    bool itemDeleted = false;

                    if (pathChange.Task.IsCompletedSuccessfully)
                    {
                        var change = await pathChange.Task;

                        if (change.Renamed)
                        {
                            string newPath = change.NewPath;
                            bool wasManagedBackup = string.Equals(
                                shortcut.DesktopBackupPath, shellPath,
                                StringComparison.OrdinalIgnoreCase);

                            shortcut.TargetPath = newPath;

                            if (wasManagedBackup)
                            {
                                bool remainsInBackupFolder = string.Equals(
                                    Path.GetDirectoryName(newPath),
                                    Path.GetDirectoryName(shellPath),
                                    StringComparison.OrdinalIgnoreCase);

                                if (remainsInBackupFolder)
                                {
                                    shortcut.DesktopBackupPath = newPath;

                                    // Keep the eventual desktop-restoration filename in sync.
                                    if (!string.IsNullOrWhiteSpace(shortcut.DesktopPath))
                                    {
                                        shortcut.DesktopPath = Path.Combine(
                                            Path.GetDirectoryName(shortcut.DesktopPath)!,
                                            Path.GetFileName(newPath));
                                    }
                                }
                                else
                                {
                                    // It has been moved out of Portals' managed backup folder.
                                    shortcut.DesktopBackupPath = string.Empty;
                                    shortcut.DesktopPath = string.Empty;
                                }
                            }

                            shortcut.Name = Directory.Exists(newPath)
                                ? Path.GetFileName(newPath)
                                : Path.GetFileNameWithoutExtension(newPath);

                            shortcutText.Text = shortcut.Name;
                            await _portalManager.SavePortalsAsync();
                        }
                        else
                        {
                            itemDeleted = true;
                        }
                    }
                    else
                    {
                        itemDeleted = !File.Exists(shellPath) &&
                                      !Directory.Exists(shellPath);
                    }

                    if (itemDeleted)
                    {
                        portal.Shortcuts.Remove(shortcut);
                        shortcutCanvas.Children.Remove(shortcutTile);
                        ReindexShortcuts(portal);
                        await _portalManager.SavePortalsAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Windows shortcut context menu failed: {ex}");

                    NativeMessageBox(
                        hwnd,
                        ex.Message,
                        "Windows Context Menu",
                        MB_OK | MB_ICONINFORMATION | MB_TOPMOST | MB_SETFOREGROUND);
                }
                finally
                {
                    activeShortcutContextMenu = false;
                }
            }


            // Add a shortcut icon and name to the Portal.
            async Task AddShortcutToListAsync(
                PortalShortcut shortcut,
               global::Windows.Foundation.Point? dropPosition = null)
            {
                int desktopIconSize =
                    GetDesktopIconSize(shortcut.TargetPath);

                int tileWidth = Math.Max(100, desktopIconSize + 24);

                double verticalSpacing =
                    Math.Max(110, desktopIconSize + 70);

                var shortcutRow =
                    new Microsoft.UI.Xaml.Controls.StackPanel
                    {
                        Orientation =
                            Microsoft.UI.Xaml.Controls.Orientation.Vertical,
                        Spacing = 5,
                        HorizontalAlignment =
                            Microsoft.UI.Xaml.HorizontalAlignment.Center
                    };

                var shortcutContainer =
                    new Microsoft.UI.Xaml.Controls.Border
                    {
                        Tag = shortcut,
                        Width = tileWidth,
                        MinHeight = Math.Max(85, desktopIconSize + 53),
                        Padding = new Microsoft.UI.Xaml.Thickness(6),
                        CornerRadius = new Microsoft.UI.Xaml.CornerRadius(5),
                        Background =
                            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                Microsoft.UI.Colors.Transparent),
                        Child = shortcutRow
                    };


                desktopIconSize = 48;

                var shortcutIcon =
                    new Microsoft.UI.Xaml.Controls.Image
                    {
                        Width = desktopIconSize,
                        Height = desktopIconSize,
                        Stretch =
                            Microsoft.UI.Xaml.Media.Stretch.Uniform,
                        IsHitTestVisible = true
                    };

                try
                {
                    // First try retrieving the actual Windows Shell icon.
                    var windowsIcon =
                        await GetWindowsShortcutIconAsync(shortcut.TargetPath);

                    if (windowsIcon != null)
                    {
                        shortcutIcon.Source = windowsIcon;
                    }
                    else
                    {
                        // Fall back to the existing thumbnail method.
                        var storageFile =
                            await global::Windows.Storage.StorageFile
                                .GetFileFromPathAsync(shortcut.TargetPath);



                        var thumbnail =
                            await storageFile.GetThumbnailAsync(
                                global::Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                                (uint)desktopIconSize);

                        if (thumbnail != null)
                        {
                            var bitmap =
                                new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();

                            await bitmap.SetSourceAsync(thumbnail);

                            shortcutIcon.Source = bitmap;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Unable to retrieve shortcut icon: {ex.Message}");
                }

                var shortcutText =
                    new Microsoft.UI.Xaml.Controls.TextBlock
                    {
                        Text = shortcut.Name,
                        Foreground =
                            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                Microsoft.UI.Colors.White),
                        FontSize = 10,

                        Width = tileWidth - 14,
                        TextWrapping =
                            Microsoft.UI.Xaml.TextWrapping.Wrap,
                        TextAlignment =
                            Microsoft.UI.Xaml.TextAlignment.Center,
                        HorizontalAlignment =
                            Microsoft.UI.Xaml.HorizontalAlignment.Center
                    };

                // Drag handling for the entire shortcut tile.
                bool isDragging = false;
                bool hasMoved = false;
                bool nativeDragStarted = false;

                global::Windows.Foundation.Point dragStart = new();

                double originalX = 0;
                double originalY = 0;

                string? activeTransferMarker = null;
                string? dragPreparationError = null;

                // Prepare a native drag package. Desktop-origin items expose
                // their backed-up file/folder to Explorer; all items carry a
                // private format so another Portal can recognise an internal move.
                shortcutContainer.DragStarting += async (sender, args) =>
                {

                    var deferral = args.GetDeferral();

                    try
                    {
                        if (string.IsNullOrWhiteSpace(activeTransferMarker))
                        {
                            args.Cancel = true;
                            return;
                        }

                       args.Data.Properties.Title = "Portals shortcut";
                        args.Data.Properties.Description = activeTransferMarker;

                        // The destination Portal checks for this format. The
                        // stream itself need not contain data; the unique token
                        // is carried in the package description.
                        args.Data.SetData(
                            ShortcutTransferFormat,
                            new global::Windows.Storage.Streams.InMemoryRandomAccessStream());

                        // Only desktop-origin items have a real backup item to
                        // move into Windows Explorer.
                        if (!string.IsNullOrWhiteSpace(shortcut.DesktopBackupPath))
                        {
                            string backupPath = shortcut.DesktopBackupPath;
                            global::Windows.Storage.IStorageItem? storageItem = null;

                            if (File.Exists(backupPath))
                            {
                                storageItem = await
                                    global::Windows.Storage.StorageFile
                                        .GetFileFromPathAsync(backupPath);
                            }
                            else if (Directory.Exists(backupPath))
                            {
                                storageItem = await
                                    global::Windows.Storage.StorageFolder
                                        .GetFolderFromPathAsync(backupPath);
                            }

                            if (storageItem == null)
                            {
                                dragPreparationError =
                                    "The stored file or folder could not be found. " +
                                    "The Portal shortcut has been left unchanged.";

                                args.Cancel = true;
                                return;
                            }

                            args.Data.SetStorageItems(new[] { storageItem }, false);

                      
                        }

                        args.Data.RequestedOperation =
                            global::Windows.ApplicationModel.DataTransfer
                                .DataPackageOperation.Move;

                     
                    }
                    catch (Exception ex)
                    {
                       

                        dragPreparationError = ex.Message;
                        args.Cancel = true;
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                };

                              

                shortcutContainer.PointerPressed += (sender, e) =>
                {
                    if (!e.GetCurrentPoint(shortcutContainer)
                        .Properties.IsLeftButtonPressed)
                        return;

                    isDragging = true;
                    hasMoved = false;
                    nativeDragStarted = false;

                    dragStart = e.GetCurrentPoint(shortcutCanvas).Position;

                    originalX = shortcut.PositionX;
                    originalY = shortcut.PositionY;

                    shortcutContainer.CapturePointer(e.Pointer);
                    e.Handled = true;
                };

                shortcutContainer.PointerMoved += async (sender, e) =>
                {
                    if (!isDragging || nativeDragStarted)
                        return;

                    var current =
                        e.GetCurrentPoint(shortcutCanvas).Position;

                    double deltaX = current.X - dragStart.X;
                    double deltaY = current.Y - dragStart.Y;

                    // Ignore very small movements.
                    if (Math.Abs(deltaX) > 4 || Math.Abs(deltaY) > 4)
                        hasMoved = true;

                    if (!hasMoved)
                        return;

                    // Leaving the Portal starts a native Windows drag. Movement
                    // inside the Portal continues to reposition the tile.
                    var rootPoint = e.GetCurrentPoint(root).Position;

                    bool outsidePortal =
                        rootPoint.X < 0 ||
                        rootPoint.Y < 0 ||
                        rootPoint.X > root.ActualWidth ||
                        rootPoint.Y > root.ActualHeight;

                    if (outsidePortal)
                    {
                        e.Handled = true;
                        nativeDragStarted = true;
                        isDragging = false;

                        var dragPoint = e.GetCurrentPoint(shortcutContainer);

                        // A cancelled drag must not leave the tile displaced.
                        shortcut.PositionX = originalX;
                        shortcut.PositionY = originalY;

                        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(
                            shortcutContainer, originalX);

                        Microsoft.UI.Xaml.Controls.Canvas.SetTop(
                            shortcutContainer, originalY);

                        shortcutContainer.ReleasePointerCapture(e.Pointer);

                        string marker = Guid.NewGuid().ToString("N");

                        var transfer = new PendingShortcutTransfer
                        {
                            SourcePortal = portal,
                            Shortcut = shortcut,
                            SourceCanvas = shortcutCanvas,
                            SourceTile = shortcutContainer,
                            SourceIndex = portal.Shortcuts.IndexOf(shortcut)
                        };

                        _pendingShortcutTransfers[marker] = transfer;
                        activeTransferMarker = marker;
                        dragPreparationError = null;
                        _activeShortcutDrag = true;

                        IntPtr hwnd =
                            WinRT.Interop.WindowNative.GetWindowHandle(window);

                        try
                        {
                            var dragResult =
                                await shortcutContainer.StartDragAsync(dragPoint);
                      

                            // The destination Portal has already completed the move.
                            if (transfer.Completed)
                                return;

                            if (!string.IsNullOrWhiteSpace(dragPreparationError))
                            {
                                NativeMessageBox(
                                    hwnd,
                                    dragPreparationError,
                                    "Unable to Drag Item",
                                    MB_OK | MB_ICONINFORMATION |
                                    MB_TOPMOST | MB_SETFOREGROUND);

                                return;
                            }

                            if (!string.IsNullOrWhiteSpace(shortcut.DesktopBackupPath))
                            {
                                // Explorer has moved the backed-up item to the
                                // drop location. Remove the Portal record only
                                // when the backup really no longer exists.
                                string backupPath = shortcut.DesktopBackupPath;

                                bool backupStillExists =
                                    File.Exists(backupPath) ||
                                    Directory.Exists(backupPath);

                                // Allow Windows Explorer time to complete the move.
                                // Check every 100 milliseconds, for up to 3 seconds.
                                for (int attempt = 0;
                                     backupStillExists && attempt < 30;
                                     attempt++)
                                {
                                    await Task.Delay(100);

                                    backupStillExists =
                                        File.Exists(backupPath) ||
                                        Directory.Exists(backupPath);
                                }                              

                                if (!backupStillExists)                                   
                                {
                                    portal.Shortcuts.Remove(shortcut);
                                    shortcutCanvas.Children.Remove(shortcutContainer);
                                    ReindexShortcuts(portal);
                                  
                                    try
                                    {
                                        await _portalManager.SavePortalsAsync();
                                    }
                                    catch (Exception saveEx)
                                    {
                                        System.Diagnostics.Debug.WriteLine(
                                            $"Unable to save restored shortcut removal: {saveEx.Message}");

                                        NativeMessageBox(
                                            hwnd,
                                            "The item was restored, but Portals could not save the updated list. " +
                                            "Please restart Portals and check the Portal contents.",
                                            "Portals Save Warning",
                                            MB_OK | MB_ICONINFORMATION |
                                            MB_TOPMOST | MB_SETFOREGROUND);
                                    }
                                }
                            }
                            else if (IsCursorOnWindowsDesktop())
                            {
                                // A link-only entry has no file to restore. Ask
                                // before removing it, leaving its original item untouched.
                                int result = NativeMessageBox(
                                    hwnd,
                                    "This Portal entry is only a link to its original location.\n\n" +
                                    "Remove it from this Portal? The original file or folder " +
                                    "will remain untouched.",
                                    "Remove Portal Link",
                                    MB_YESNO | MB_ICONQUESTION |
                                    MB_DEFBUTTON2 | MB_TOPMOST |
                                    MB_SETFOREGROUND);

                                if (result == IDYES)
                                {
                                    portal.Shortcuts.Remove(shortcut);
                                    shortcutCanvas.Children.Remove(shortcutContainer);
                                    ReindexShortcuts(portal);

                                    try
                                    {
                                        await _portalManager.SavePortalsAsync();
                                    }
                                    catch (Exception saveEx)
                                    {
                                        // Restore the link in memory if the
                                        // removal could not be persisted.
                                        int restoreIndex = Math.Clamp(
                                            transfer.SourceIndex,
                                            0,
                                            portal.Shortcuts.Count);

                                        portal.Shortcuts.Insert(restoreIndex, shortcut);
                                        shortcutCanvas.Children.Add(shortcutContainer);

                                        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(
                                            shortcutContainer, originalX);

                                        Microsoft.UI.Xaml.Controls.Canvas.SetTop(
                                            shortcutContainer, originalY);

                                        ReindexShortcuts(portal);

                                        System.Diagnostics.Debug.WriteLine(
                                            $"Unable to save Portal link removal: {saveEx.Message}");

                                        NativeMessageBox(
                                            hwnd,
                                            "Portals could not save the change. The link has been kept in the Portal.",
                                            "Portals Save Warning",
                                            MB_OK | MB_ICONINFORMATION |
                                            MB_TOPMOST | MB_SETFOREGROUND);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                                NativeMessageBox(
                                hwnd,
                                ex.Message,
                                "Unable to Drag Item",
                                MB_OK | MB_ICONINFORMATION |
                                MB_TOPMOST | MB_SETFOREGROUND);
                        }
                        finally
                        {
                            activeTransferMarker = null;
                            _pendingShortcutTransfers.Remove(marker);
                            _activeShortcutDrag = false;

                            // Once the drag ends, collapse the source Portal if
                            // the pointer is still outside it.
                            if (!portal.KeepVisible &&
                                !portal.IsCollapsed &&
                                IsCursorOutsideWindow(hwnd))
                            {
                                SetPortalExpanded(false);
                            }
                        }

                        return;
                    }

                    // Existing behaviour: reposition the tile within the Portal.
                    double maxX = Math.Max(
                        10,
                        shortcutCanvas.ActualWidth
                            - shortcutContainer.ActualWidth - 10);

                    double maxY = Math.Max(
                        10,
                        shortcutCanvas.ActualHeight
                            - shortcutContainer.ActualHeight - 10);

                    shortcut.PositionX = Math.Clamp(
                        originalX + deltaX, 10, maxX);

                    shortcut.PositionY = Math.Clamp(
                        originalY + deltaY, 10, maxY);

                    Microsoft.UI.Xaml.Controls.Canvas.SetLeft(
                        shortcutContainer, shortcut.PositionX);

                    Microsoft.UI.Xaml.Controls.Canvas.SetTop(
                        shortcutContainer, shortcut.PositionY);

                    e.Handled = true;
                };

                shortcutContainer.PointerReleased += async (sender, e) =>
                {
                    if (!isDragging)
                        return;

                    isDragging = false;

                    shortcutContainer.ReleasePointerCapture(e.Pointer);

                    if (hasMoved)
                        await _portalManager.SavePortalsAsync();

                    e.Handled = true;
                };

                shortcutContainer.DoubleTapped += (sender, e) =>
                {
                    OpenShortcut(shortcut);
                    e.Handled = true;
                };

                shortcutContainer.RightTapped += async (sender, e) =>
                {
                    e.Handled = true;

                    if (isDragging)
                        return;

                    await ShowShortcutContextMenuAsync(
                        shortcut, shortcutContainer, shortcutText);
                };

                shortcutContainer.PointerCanceled += (sender, e) =>
                {
                    isDragging = false;
                };

                shortcutRow.Children.Add(shortcutIcon);
                shortcutRow.Children.Add(shortcutText);

                //xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx


                // Assign a position to a new or dropped shortcut.
                if (dropPosition.HasValue ||
                    shortcut.PositionX < 0 || shortcut.PositionY < 0)
                {
                    double availableWidth = shortcutCanvas.ActualWidth;
                    double availableHeight = shortcutCanvas.ActualHeight;

                    if (availableWidth <= 0)
                        availableWidth = portal.Width;

                    if (availableHeight <= 0)
                        availableHeight = portal.Height;

                    int columns = Math.Max(1,
                        (int)Math.Floor(
                            (availableWidth - 10) / (tileWidth + 10)));

                    var existingShortcuts = portal.Shortcuts
                        .Where(s => !ReferenceEquals(s, shortcut)
                                 && s.PositionX >= 0
                                 && s.PositionY >= 0)
                        .ToList();

                    // Check whether a proposed position is occupied.
                    bool IsOccupied(double x, double y)
                    {
                        return existingShortcuts.Any(s =>
                            x < s.PositionX + tileWidth &&
                            x + tileWidth > s.PositionX &&
                            y < s.PositionY + verticalSpacing &&
                            y + verticalSpacing > s.PositionY);
                    }

                    bool found = false;

                    if (dropPosition.HasValue)
                    {
                        // Centre the tile approximately under the mouse pointer.
                        double maxX = Math.Max(10,
                            availableWidth - tileWidth - 10);

                        double maxY = Math.Max(10,
                            availableHeight - verticalSpacing - 10);

                        double desiredX = Math.Clamp(
                            dropPosition.Value.X - tileWidth / 2,
                            10, maxX);

                        double desiredY = Math.Clamp(
                            dropPosition.Value.Y - verticalSpacing / 2,
                            10, maxY);

                        // First try the actual drop position.
                        if (!IsOccupied(desiredX, desiredY))
                        {
                            shortcut.PositionX = desiredX;
                            shortcut.PositionY = desiredY;
                            found = true;
                        }
                        else
                        {
                            // If occupied, find the nearest available grid position.
                            int visibleRows = Math.Max(1,
                                (int)Math.Floor(
                                    (availableHeight - 20) / verticalSpacing));

                            double bestDistance = double.MaxValue;

                            for (int row = 0; row < visibleRows; row++)
                            {
                                for (int col = 0; col < columns; col++)
                                {
                                    double x = 10 + col * (tileWidth + 10);
                                    double y = 10 + row * verticalSpacing;

                                    if (IsOccupied(x, y))
                                        continue;

                                    double distance =
                                        Math.Pow(x - desiredX, 2) +
                                        Math.Pow(y - desiredY, 2);

                                    if (distance < bestDistance)
                                    {
                                        bestDistance = distance;
                                        shortcut.PositionX = x;
                                        shortcut.PositionY = y;
                                        found = true;
                                    }
                                }
                            }
                        }
                    }

                    // Fallback: find the first free position if necessary.
                    if (!found)
                    {
                        for (int row = 0; ; row++)
                        {
                            for (int col = 0; col < columns; col++)
                            {
                                double x = 10 + col * (tileWidth + 10);
                                double y = 10 + row * verticalSpacing;

                                if (!IsOccupied(x, y))
                                {
                                    shortcut.PositionX = x;
                                    shortcut.PositionY = y;
                                    found = true;
                                    break;
                                }
                            }

                            if (found)
                                break;
                        }
                    }
                }


                //xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

                // Position the shortcut on the Canvas.
                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(
                    shortcutContainer, shortcut.PositionX);

                Microsoft.UI.Xaml.Controls.Canvas.SetTop(
                    shortcutContainer, shortcut.PositionY);

                shortcutCanvas.Children.Add(shortcutContainer);
            }


            // Restore shortcuts already saved in this Portal.
            foreach (var shortcut in portal.Shortcuts
        .OrderBy(s => s.SortOrder))
            {
                _ = AddShortcutToListAsync(shortcut);
            }

            // Enable drag and drop from Windows Explorer and other Portals.
            root.AllowDrop = true;

            root.DragOver += (sender, e) =>
            {
                string? marker =
                    GetPendingShortcutTransferMarker(e.DataView);

                if (marker != null)
                {
                    e.AcceptedOperation =
                        global::Windows.ApplicationModel.DataTransfer
                            .DataPackageOperation.Move;
                }
                else if (e.DataView.Contains(
                    global::Windows.ApplicationModel.DataTransfer
                        .StandardDataFormats.StorageItems))
                {
                    e.AcceptedOperation =
                        global::Windows.ApplicationModel.DataTransfer
                            .DataPackageOperation.Copy;
                }
                else
                {
                    e.AcceptedOperation =
                        global::Windows.ApplicationModel.DataTransfer
                            .DataPackageOperation.None;
                }
            };

            root.Drop += async (sender, e) =>
            {
                // Handle an internal Portal-to-Portal move before the normal
                // Explorer StorageItems path, so we do not create a duplicate.
                // Capture the drop position relative to the Portal canvas.
             
                var dropPoint = e.GetPosition(shortcutCanvas);
                string? marker =
              GetPendingShortcutTransferMarker(e.DataView);

                if (marker != null &&
                    _pendingShortcutTransfers.TryGetValue(marker, out var transfer))
                {
                    var dropDeferral = e.GetDeferral();

                    try
                    {
                        double originalX = transfer.Shortcut.PositionX;
                        double originalY = transfer.Shortcut.PositionY;

                        if (!ReferenceEquals(transfer.SourcePortal, portal) &&
                            transfer.SourcePortal.Shortcuts.Contains(transfer.Shortcut) &&
                            !portal.Shortcuts.Contains(transfer.Shortcut))
                        {
                            transfer.SourcePortal.Shortcuts.Remove(transfer.Shortcut);
                            portal.Shortcuts.Add(transfer.Shortcut);

                            ReindexShortcuts(transfer.SourcePortal);
                            ReindexShortcuts(portal);

                            try
                            {
                                // Add the destination tile, then persist both
                                // Portal collections before removing the source tile.
                                await AddShortcutToListAsync(
                                transfer.Shortcut, dropPoint);
                                await _portalManager.SavePortalsAsync();

                                transfer.SourceCanvas.Children.Remove(
                                    transfer.SourceTile);

                                transfer.Completed = true;
                            }
                            catch (Exception transferEx)
                            {
                                portal.Shortcuts.Remove(transfer.Shortcut);
                                transfer.Shortcut.PositionX = originalX;
                                transfer.Shortcut.PositionY = originalY;

                                // Remove any destination tile that was created
                                // before the failure.
                                for (int i = shortcutCanvas.Children.Count - 1; i >= 0; i--)
                                {
                                    if (shortcutCanvas.Children[i] is
                                            Microsoft.UI.Xaml.Controls.Border tile &&
                                        ReferenceEquals(tile.Tag, transfer.Shortcut))
                                    {
                                        shortcutCanvas.Children.RemoveAt(i);
                                    }
                                }

                                int restoreIndex = Math.Clamp(
                                    transfer.SourceIndex,
                                    0,
                                    transfer.SourcePortal.Shortcuts.Count);

                                transfer.SourcePortal.Shortcuts.Insert(
                                    restoreIndex,
                                    transfer.Shortcut);

                                ReindexShortcuts(transfer.SourcePortal);
                                ReindexShortcuts(portal);

                                try
                                {
                                    await _portalManager.SavePortalsAsync();
                                }
                                catch (Exception rollbackEx)
                                {
                                    System.Diagnostics.Debug.WriteLine(
                                        $"Portal transfer rollback save failed: {rollbackEx.Message}");
                                }

                                System.Diagnostics.Debug.WriteLine(
                                    $"Portal-to-Portal transfer failed: {transferEx.Message}");

                                IntPtr hwnd =
                                    WinRT.Interop.WindowNative.GetWindowHandle(window);

                                NativeMessageBox(
                                    hwnd,
                                    "The shortcut could not be moved to the destination Portal. " +
                                    "It has been returned to its original Portal.",
                                    "Portal Transfer Failed",
                                    MB_OK | MB_ICONINFORMATION |
                                    MB_TOPMOST | MB_SETFOREGROUND);
                            }
                        }
                    }
                    finally
                    {
                        dropDeferral.Complete();
                    }

                    return;
                }

                if (!e.DataView.Contains(
                    global::Windows.ApplicationModel.DataTransfer
                        .StandardDataFormats.StorageItems))
                    return;

                var items = await e.DataView.GetStorageItemsAsync();

                // Portal-specific storage.
                string dataFolder =
                 Path.GetDirectoryName(AppGlobals.PortalsFile)!;

                string backupFolder =
                    Path.Combine(dataFolder, "DesktopBackup");

                Directory.CreateDirectory(backupFolder);


                // Identify the Windows desktop locations.
                string userDesktop =
                    Path.GetFullPath(AppGlobals.DesktopPath);

                string commonDesktop = Path.GetFullPath(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonDesktopDirectory));

                foreach (var item in items)
                {
                    string sourcePath = item.Path;

                    if (string.IsNullOrWhiteSpace(sourcePath))
                        continue;

                    sourcePath = Path.GetFullPath(sourcePath);

                    bool isFile = item is global::Windows.Storage.StorageFile;
                    bool isFolder = item is global::Windows.Storage.StorageFolder;

                    if (!isFile && !isFolder)
                        continue;

                    // Check that the original item still exists.
                    if ((isFile && !File.Exists(sourcePath)) ||
                        (isFolder && !Directory.Exists(sourcePath)))
                        continue;

                    // Avoid adding the same desktop item twice.
                    bool alreadyExists = portal.Shortcuts.Any(s =>
                        string.Equals(s.DesktopPath, sourcePath,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.TargetPath, sourcePath,
                            StringComparison.OrdinalIgnoreCase));

                    if (alreadyExists)
                        continue;

                    string? sourceDirectory =
                        Path.GetDirectoryName(sourcePath);

                    // Identify items originating directly from the desktop.
                    bool isDesktopItem =
                        string.Equals(sourceDirectory, userDesktop,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(sourceDirectory, commonDesktop,
                            StringComparison.OrdinalIgnoreCase);

                    // Also store Windows shortcuts (.lnk) dragged in from other folders.
                    // This allows them to be dragged back out of Portals later.
                    bool isShortcutFile =
                        isFile &&
                        string.Equals(
                            Path.GetExtension(sourcePath),
                            ".lnk",
                            StringComparison.OrdinalIgnoreCase);

                    // Desktop items and .lnk shortcuts are stored in Portal backup.
                    bool shouldStoreInBackup = isDesktopItem || isShortcutFile;

                    string targetPath = sourcePath;
                    string desktopPath = string.Empty;
                    string desktopBackupPath = string.Empty;

                    bool itemMoved = false;

                    PortalShortcut? shortcut = null;

                    try
                    {
                        if (shouldStoreInBackup)
                        {
                            // Generate a unique backup filename.
                            string originalName = Path.GetFileName(sourcePath);

                            string fileName = isFile
                                ? Path.GetFileNameWithoutExtension(originalName)
                                : originalName;

                            string extension = isFile
                                ? Path.GetExtension(originalName)
                                : string.Empty;

                            string backupPath = Path.Combine(
                                backupFolder, originalName);

                            int counter = 1;

                            while (File.Exists(backupPath) ||
                                   Directory.Exists(backupPath))
                            {
                                backupPath = Path.Combine(
                                    backupFolder,
                                    $"{fileName} ({counter++}){extension}");
                            }

                            // Move the original item into Portals storage.
                            if (isFile)
                                File.Move(sourcePath, backupPath);
                            else
                                Directory.Move(sourcePath, backupPath);

                            itemMoved = true;

                            desktopPath = sourcePath;
                            desktopBackupPath = backupPath;
                            targetPath = backupPath;
                        }

                        // Create the Portal shortcut record.
                        string displayName = isFile
                            ? Path.GetFileNameWithoutExtension(item.Name)
                            : item.Name;

                        shortcut = new PortalShortcut
                        {
                            Name = displayName,
                            ShortcutType = isFolder ? "Folder" : "Item",
                            TargetPath = targetPath,
                            IconPath = string.Empty,
                            SortOrder = portal.Shortcuts.Count,
                            PositionX = -1,
                            PositionY = -1,
                            DesktopPath = desktopPath,
                            DesktopBackupPath = desktopBackupPath
                        };

                        portal.Shortcuts.Add(shortcut);

                        // Save before displaying the item.
                        await _portalManager.SavePortalsAsync();

                        await AddShortcutToListAsync(shortcut, dropPoint);

                        // Save the newly assigned coordinates.
                        await _portalManager.SavePortalsAsync();
                    }
                    catch (Exception ex)
                    {
                        // Remove the incomplete Portal entry.
                        if (shortcut != null)
                            portal.Shortcuts.Remove(shortcut);

                        // If saving or displaying failed, restore the original.
                        if (itemMoved &&
                            !string.IsNullOrWhiteSpace(desktopBackupPath))
                        {
                            try
                            {
                                if (File.Exists(desktopBackupPath) &&
                                    !File.Exists(desktopPath))
                                {
                                    File.Move(desktopBackupPath, desktopPath);
                                }
                                else if (Directory.Exists(desktopBackupPath) &&
                                         !Directory.Exists(desktopPath))
                                {
                                    Directory.Move(desktopBackupPath, desktopPath);
                                }
                            }
                            catch (Exception restoreEx)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"Rollback failed: {restoreEx.Message}");
                            }
                        }

                        await _portalManager.SavePortalsAsync();

                        System.Diagnostics.Debug.WriteLine(
                            $"Unable to add desktop item: {ex.Message}");
                    }
                }
            };
            //xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
            root.Children.Add(body);

            // Expand when the pointer enters the title bar. Collapse only
            // after it leaves the entire Portal.
            void SetPortalExpanded(bool expanded)
            {
                if (!expanded && portal.KeepVisible)
                    return;

                if (portal.IsCollapsed == !expanded)
                    return;

                portal.IsCollapsed = !expanded;

                root.RowDefinitions[1].Height =
                    new Microsoft.UI.Xaml.GridLength(
                        expanded ? 1 : 0,
                        expanded
                            ? Microsoft.UI.Xaml.GridUnitType.Star
                            : Microsoft.UI.Xaml.GridUnitType.Pixel);

                window.AppWindow.Resize(
                    new global::Windows.Graphics.SizeInt32(
                        (int)portal.Width,
                        expanded ? (int)portal.Height : 38));

                // Bring the expanded Portal to the foreground.
                if (expanded)
                {
                    window.Activate();
                }

                _ = _portalManager.SavePortalsAsync();
            }

            titleBar.PointerEntered += (sender, e) =>
            {
                if (portal.IsCollapsed)
                    SetPortalExpanded(true);
            };

            root.PointerExited += async (sender, e) =>
            {
                if (portal.IsCollapsed || _activeShortcutDrag)
                    return;

                if (portal.IsCollapsed || _activeShortcutDrag ||
                 activeShortcutContextMenu)
                    return;

                // Allow time for the pointer to move between child elements.
                await Task.Delay(150);

                if (portal.IsCollapsed || _activeShortcutDrag)
                    return;

                if (portal.IsCollapsed || _activeShortcutDrag ||
                 activeShortcutContextMenu)
                    return;

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

                if (GetCursorPos(out POINT cursor) &&
                    GetWindowRect(hwnd, out RECT rect))
                {
                    bool pointerInside =
                        cursor.X >= rect.Left &&
                        cursor.X < rect.Right &&
                        cursor.Y >= rect.Top &&
                        cursor.Y < rect.Bottom;

                    if (!pointerInside)
                        SetPortalExpanded(false);
                }
            };

            return root;
        }

        private static bool IsCursorOutsideWindow(IntPtr hwnd)
        {
            if (!GetCursorPos(out POINT cursor) ||
                !GetWindowRect(hwnd, out RECT rect))
            {
                return false;
            }

            return cursor.X < rect.Left ||
                   cursor.X >= rect.Right ||
                   cursor.Y < rect.Top ||
                   cursor.Y >= rect.Bottom;
        }

        private static bool IsCursorOnWindowsDesktop()
        {
            if (!GetCursorPos(out POINT point))
                return false;

            IntPtr hwnd = WindowFromPoint(point);

            // Desktop icons are hosted by SysListView32 beneath
            // SHELLDLL_DefView. Blank desktop areas are hosted by Progman
            // or WorkerW. Walk the parent chain to cover both cases.
            for (int i = 0; hwnd != IntPtr.Zero && i < 8; i++)
            {
                var className = new System.Text.StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);

                string name = className.ToString();

                if (name.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("SysListView32", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                hwnd = GetParent(hwnd);
            }

            return false;

        }

        private string? GetPendingShortcutTransferMarker(
    global::Windows.ApplicationModel.DataTransfer.DataPackageView dataView)
        {
            // First try the marker carried in the data package.
            string? marker = dataView.Properties.Description;

            if (!string.IsNullOrWhiteSpace(marker) &&
                _pendingShortcutTransfers.ContainsKey(marker))
            {
                return marker;
            }

            // Fallback: only one native drag can be active at a time.
            // Use its pending transfer marker if the description is unavailable.
            if (_pendingShortcutTransfers.Count == 1)
            {
                foreach (var pendingMarker in _pendingShortcutTransfers.Keys)
                    return pendingMarker;
            }

            return null;
        }


        private static void ReindexShortcuts(Portal portal)
        {
            for (int i = 0; i < portal.Shortcuts.Count; i++)
                portal.Shortcuts[i].SortOrder = i;
        }

        private static void OpenShortcut(PortalShortcut shortcut)
        {
            try
            {
                string path = shortcut.TargetPath;

                if (!File.Exists(path) && !Directory.Exists(path))
                    return;

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unable to open shortcut: {ex.Message}");
            }
        }

        // Apply colour to the Portal's background.
        private static void SetPortalBackgroundColour(
            Microsoft.UI.Xaml.Controls.Grid root,
            string colour)
        {
            var mauiColour =
                Microsoft.Maui.Graphics.Color.FromArgb(colour);

            root.Background =
                new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    ColorHelper.FromArgb(
                        (byte)(mauiColour.Alpha * 255),
                        (byte)(mauiColour.Red * 255),
                        (byte)(mauiColour.Green * 255),
                        (byte)(mauiColour.Blue * 255)));
        }


        private static async Task<Microsoft.UI.Xaml.Media.ImageSource?>
    GetWindowsShortcutIconAsync(string shortcutPath)
        {
            IntPtr hIcon = IntPtr.Zero;
            IntPtr screenDC = IntPtr.Zero;
            IntPtr memoryDC = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                var info = new SHFILEINFO();

                IntPtr result = SHGetFileInfo(
                    shortcutPath,
                    0,
                    out info,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
                    SHGFI_ICON | SHGFI_LARGEICON);

                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                    return null;

                hIcon = info.hIcon;

                const int width = 48;
                const int height = 48;

                var bitmapInfo = new BITMAPINFOHEADER
                {
                    biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };

                screenDC = GetDC(IntPtr.Zero);
                memoryDC = CreateCompatibleDC(screenDC);

                hBitmap = CreateDIBSection(
                    screenDC,
                    ref bitmapInfo,
                    0,
                    out IntPtr bits,
                    IntPtr.Zero,
                    0);

                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    return null;

                oldBitmap = SelectObject(memoryDC, hBitmap);

                if (!DrawIconEx(
                    memoryDC, 0, 0, hIcon,
                    width, height, 0, IntPtr.Zero, 3))
                    return null;

                byte[] pixels = new byte[width * height * 4];

                System.Runtime.InteropServices.Marshal.Copy(
                    bits, pixels, 0, pixels.Length);

                var buffer =
                    global::Windows.Security.Cryptography
                        .CryptographicBuffer.CreateFromByteArray(pixels);

                var softwareBitmap =
                    global::Windows.Graphics.Imaging.SoftwareBitmap
                        .CreateCopyFromBuffer(
                            buffer,
                            global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                            width,
                            height,
                            global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

                var imageSource =
                    new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();

                await imageSource.SetBitmapAsync(softwareBitmap);

                softwareBitmap.Dispose();

                return imageSource;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Windows icon retrieval failed: {ex.Message}");

                return null;
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memoryDC != IntPtr.Zero)
                    SelectObject(memoryDC, oldBitmap);

                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);

                if (memoryDC != IntPtr.Zero)
                    DeleteDC(memoryDC);

                if (screenDC != IntPtr.Zero)
                {
                    int releaseResult = ReleaseDC(IntPtr.Zero, screenDC);

                    if (releaseResult == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "ReleaseDC failed to release the screen device context.");
                    }
                }

                if (hIcon != IntPtr.Zero)
                    DestroyIcon(hIcon);
            }
        }


        private static (int Width, int Height) GetIconDimensions(IntPtr hIcon)
        {
            if (!GetIconInfo(hIcon, out ICONINFO iconInfo))
                return (48, 48);

            try
            {
                IntPtr bitmapHandle =
                    iconInfo.hbmColor != IntPtr.Zero
                        ? iconInfo.hbmColor
                        : iconInfo.hbmMask;

                if (bitmapHandle == IntPtr.Zero ||
                    GetObject(
                        bitmapHandle,
                        System.Runtime.InteropServices.Marshal.SizeOf<BITMAP>(),
                        out BITMAP bitmap) == 0)
                {
                    return (48, 48);
                }

                int height = bitmap.bmHeight;

                // Monochrome icons store both masks in one bitmap.
                if (iconInfo.hbmColor == IntPtr.Zero)
                    height /= 2;

                return (
                    Math.Clamp(bitmap.bmWidth, 16, 256),
                    Math.Clamp(height, 16, 256)
                );
            }
            finally
            {
                if (iconInfo.hbmColor != IntPtr.Zero)
                    DeleteObject(iconInfo.hbmColor);

                if (iconInfo.hbmMask != IntPtr.Zero)
                    DeleteObject(iconInfo.hbmMask);
            }
        }


        private static int GetDesktopIconSize(string path) 
        {
            var info = new SHFILEINFO();

            IntPtr result = SHGetFileInfo(
                path,
                0,
                out info,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
                0x00000100 | 0x00000004);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return 48;

            try
            {
                var dimensions = GetIconDimensions(info.hIcon);

                return Math.Max(
                    dimensions.Width,
                    dimensions.Height);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
    }

}