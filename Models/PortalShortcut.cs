

using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Portals.Models
{
    public partial class PortalShortcut : ObservableObject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [ObservableProperty]
        private string _name = "New Shortcut";

        // Application, File, Folder, Website or WindowsShortcut
        public string ShortcutType { get; set; } = "Application";

        public string TargetPath { get; set; } = string.Empty;

        public string IconPath { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public double PositionX { get; set; } = -1;
        public double PositionY { get; set; } = -1;

        public string DesktopPath { get; set; } = string.Empty;

        public string DesktopBackupPath { get; set; } = string.Empty;
    }
}

