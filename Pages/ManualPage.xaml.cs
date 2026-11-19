
namespace Portals.Pages;

public partial class ManualPage : ContentPage
{
    public ManualPage()
    {
        InitializeComponent();
        LoadManual();
    }

    private async void LoadManual()
    {
        try
        {
            using var stream =
                await FileSystem.OpenAppPackageFileAsync("Manual.txt");

            using var reader = new StreamReader(stream);

            ManualContent.Text = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            ManualContent.Text =
                $"Unable to load the manual.\n\n{ex.Message}";
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        if (Window != null)
            Application.Current?.CloseWindow(Window);
    }
}
