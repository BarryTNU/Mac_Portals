

using Portals.Models;

namespace Portals.Pages
{
    public partial class MainPage : ContentPage
    {




        private readonly MainPageModel _model;

        public MainPage(MainPageModel model)
        {
            InitializeComponent();

            _model = model;
            BindingContext = _model;
        }


        private async void RenamePortal_Clicked(object sender, EventArgs e)
        {
          

            if (sender is Button button &&
                button.CommandParameter is Portal portal)
            {
                await _model.RenamePortalAsync(this, portal);
            }
        }

        private async void DeletePortal_Clicked(object sender, EventArgs e)
        {
           
            if (sender is Button button &&
                button.CommandParameter is Portal portal)
            {
                await _model.DeletePortalAsync(this, portal);
            }
        }

        private async void AddShortcut_Clicked(object sender, EventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is Portal portal)
            {
                await _model.AddShortcutAsync(this, portal);
            }
        }

        private async void OpenShortcut_Clicked(object sender, EventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is PortalShortcut shortcut)
            {
                await _model.OpenShortcutAsync(this, shortcut);
            }
        }

        private async void RenameShortcut_Clicked(object sender, EventArgs e)
        {                   
        
            if (sender is Button button &&
                button.CommandParameter is PortalShortcut shortcut)
            {
                var portal = _model.Portals.FirstOrDefault(
                    p => p.Shortcuts.Contains(shortcut));

                if (portal != null)
                {
                    await _model.RenameShortcutAsync(this, portal, shortcut);
                }
            }
        }

        private async void EditShortcutTarget_Clicked(object sender, EventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is PortalShortcut shortcut)
            {
                var portal = _model.Portals.FirstOrDefault(
                    p => p.Shortcuts.Contains(shortcut));

                if (portal != null)
                {
                    await _model.EditShortcutTargetAsync(
                        this, portal, shortcut);
                }
            }
        }

        private async void DeleteShortcut_Clicked(object sender, EventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is PortalShortcut shortcut)
            {
                if (button.BindingContext is not PortalShortcut)
                    return;

                var portal = _model.Portals.FirstOrDefault(
                    p => p.Shortcuts.Contains(shortcut));

                if (portal != null)
                {
                    await _model.DeleteShortcutAsync(this, portal, shortcut);
                }
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _model.LoadPortalsAsync();
        }
    }
}


