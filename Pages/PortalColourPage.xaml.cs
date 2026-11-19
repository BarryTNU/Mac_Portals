

using System;
using Microsoft.Maui.Controls;
using Portals.Models;
using Portals.Services;

namespace Portals.Pages;

public partial class PortalColourPage : ContentPage
{
    private readonly Portal _portal;
    private readonly PortalManager _portalManager;

    private readonly Action<string> _applyColour;

    private readonly string _originalColour;
    private string _selectedColour;

    public PortalColourPage(
        Portal portal,
        PortalManager portalManager,
        Action<string> applyColour)
    {
        InitializeComponent();

        _portal = portal;
        _portalManager = portalManager;
        _applyColour = applyColour;

        _originalColour = portal.Colour;
        _selectedColour = portal.Colour;

        colourPreview.BackgroundColor =
            Color.FromArgb(_selectedColour);
    }

    private void OnColourSelected(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            _selectedColour = button.BackgroundColor.ToArgbHex();

            colourPreview.BackgroundColor =
                Color.FromArgb(_selectedColour);

            // Apply colour immediately to the Portal.
            _applyColour(_selectedColour);
        }
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        _portal.Colour = _selectedColour;

        await _portalManager.SavePortalsAsync();

        if (Application.Current is { } app && this.Window is { } window)
        {
            app.CloseWindow(window);
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        // Restore the original colour.
        _applyColour(_originalColour);

        if (Application.Current is { } app && this.Window is { } window)
        {
            app.CloseWindow(window);
        }
    }
}

