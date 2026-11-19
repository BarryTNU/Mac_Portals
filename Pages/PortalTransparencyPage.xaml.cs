using System;
using Portals.Models;
using Portals.Services;

namespace Portals.Pages;

public partial class PortalTransparencyPage : ContentPage
{
    private readonly Portal _portal;
    private readonly IntPtr _hwnd;
    private readonly double _originalOpacity;
    private readonly PortalAppearanceManager _appearanceManager;
    private readonly PortalManager _portalManager = new();



    public PortalTransparencyPage(
        Portal portal,
        IntPtr hwnd,
        PortalAppearanceManager appearanceManager)
       {
        InitializeComponent();

        _portal = portal;
        _hwnd = hwnd;
        _appearanceManager = appearanceManager;

        _originalOpacity = portal.Opacity;

        sldOpacity.Value = Math.Max(50, portal.Opacity * 100);
        lblOpacity.Text = $"{(int)Math.Round(portal.Opacity * 100)}%";
        lblPortalName.Text = portal.Title;
    }

    private void OnOpacityChanged(object? sender, ValueChangedEventArgs e)
    {
        lblOpacity.Text = $"{(int)Math.Round(e.NewValue)}%";

       _appearanceManager.ApplyOpacity(
           _hwnd,
           e.NewValue / 100.0);
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        _portal.Opacity = sldOpacity.Value / 100.0;

        await _portalManager.SavePortalsAsync();

        if (Application.Current is { } app &&
     this.Window is { } window)
        {
            app.CloseWindow(window);
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
       _appearanceManager.ApplyOpacity(_hwnd, _originalOpacity);

        if (Application.Current is { } app &&
     this.Window is { } window)
        {
            app.CloseWindow(window);
        }
    }
}
