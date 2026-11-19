


using System.Threading;
using Microsoft.UI.Xaml;

namespace Portals.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        private static Mutex? _singleInstanceMutex;

        public App()
        {
            // Create or check the application-wide mutex.
            _singleInstanceMutex = new Mutex(
                false,
                @"Local\Portals_SingleInstance",
                out bool createdNew);

            // If another instance already exists, exit.
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                Environment.Exit(0);
                return;
            }

            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp()
            => MauiProgram.CreateMauiApp();
    }
}

