using System;
using System.Collections.ObjectModel;

namespace Portals.Models
    {
        public class Portal
        {
            public Guid Id { get; set; } = Guid.NewGuid();

            public string Name { get; set; } = "New Portal";

            public string Colour { get; set; } = "#404040";

            // Desktop position
            public double X { get; set; }
            public double Y { get; set; }

            // Portal dimensions
            public double Width { get; set; } = 300;
            public double Height { get; set; } = 250;

            // Appearance
            public double Opacity { get; set; } = 0.90;

            public bool IsCollapsed { get; set; }

            public bool IsVisible { get; set; } = true;

        public bool KeepVisible { get; set; } = false;

        public int SortOrder { get; set; }

        public string Title { get; set; } = string.Empty;

        // Shortcuts contained in this Portal
        public ObservableCollection<PortalShortcut> Shortcuts
            { get; set; } = new();
        }
    }

