using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Maui.Storage;

namespace Portals
    {
        internal static class AppGlobals
        {
            // ==========================================
            // APPLICATION DETAILS
            // ==========================================

            public const string AppName = "Portals";
            public const string AppVersion = "1.0.0";


            // ==========================================
            // USER SYSTEM FOLDERS
            // ==========================================

            public static readonly string UserProfile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

            public static readonly string DesktopPath =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);

            public static readonly string DocumentsPath =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);

            public static readonly string DownloadsPath =
                Path.Combine(UserProfile, "Downloads");

            public static readonly string PicturesPath =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures);

            public static readonly string MusicPath =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyMusic);

            public static readonly string VideosPath =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyVideos);

      static AppGlobals()
        {
            Directory.CreateDirectory(BasePath);
        }


        // ==========================================
        // APPLICATION BASE FOLDER
        // ==========================================

        public static readonly string BasePath =
#if WINDOWS
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);
#else
 Path.Combine(
     FileSystem.AppDataDirectory,
     AppName);
#endif


        // ==========================================
        // APPLICATION SUBFOLDERS
        // ==========================================

        public static readonly string DataPath =
                Path.Combine(BasePath, "Data");

            public static readonly string PortalsPath =
                Path.Combine(BasePath, "Portals");

            public static readonly string IconsPath =
                Path.Combine(BasePath, "Icons");

            public static readonly string ImagesPath =
                Path.Combine(BasePath, "Images");

            public static readonly string BackupsPath =
                Path.Combine(BasePath, "Backups");

            public static readonly string LogsPath =
                Path.Combine(BasePath, "Logs");


            // ==========================================
            // DATA FILES
            // ==========================================

            public static readonly string PortalsFile =
                Path.Combine(DataPath, "Portals.json");

            public static readonly string ShortcutsFile =
                Path.Combine(DataPath, "Shortcuts.json");

            public static readonly string SettingsFile =
                Path.Combine(DataPath, "Settings.json");


            // ==========================================
            // DEFAULT PORTALS
            // ==========================================

            public static readonly List<string> DefaultPortals = new()
        {
            "Work",
            "Finance",
            "Personal",
            "General"
        };


            // ==========================================
            // INITIALISE APPLICATION FOLDERS
            // ==========================================

            public static void Initialize()
            {
                // Main folders

                Directory.CreateDirectory(BasePath);
                Directory.CreateDirectory(DataPath);
                Directory.CreateDirectory(PortalsPath);

                Directory.CreateDirectory(IconsPath);
                Directory.CreateDirectory(
                    Path.Combine(IconsPath, "Applications"));

                Directory.CreateDirectory(
                    Path.Combine(IconsPath, "Folders"));

                Directory.CreateDirectory(
                    Path.Combine(IconsPath, "Websites"));

                Directory.CreateDirectory(ImagesPath);
                Directory.CreateDirectory(BackupsPath);
                Directory.CreateDirectory(LogsPath);

                // Create default Portal folders

                foreach (string portal in DefaultPortals)
                {
                    Directory.CreateDirectory(
                        Path.Combine(PortalsPath, portal));
                }
            }
        }
    }


