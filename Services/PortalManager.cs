
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Portals.Models;
using System.Runtime.InteropServices;
using System.Drawing;

namespace Portals.Services
{
    public class PortalManager
    {
        private readonly string _portalsFile = AppGlobals.PortalsFile;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        // Main Portal collection
        public ObservableCollection<Portal> Portals { get; }
            = new();

        // ==========================================
        // LOAD PORTALS
        // ==========================================

        public async Task LoadPortalsAsync()
        {
            Portals.Clear();

            if (File.Exists(_portalsFile))
            {
                await using FileStream stream =
                    File.OpenRead(_portalsFile);

                var savedPortals =
                    await JsonSerializer.DeserializeAsync<
                        ObservableCollection<Portal>>(
                            stream, _jsonOptions);

                if (savedPortals != null)
                {
                    foreach (var portal in savedPortals)
                        Portals.Add(portal);
                }
            }
            else
            {
                // No saved portals yet. Start with an empty collection.
                await SavePortalsAsync();
            }
        }


        // ==========================================
        // Make sure naming is Consistant
        // ==========================================

        private string GetNextPortalName()
        {
            var usedNumbers = Portals
                .Select(p => p.Name)
                .Where(name =>
                    name.StartsWith("Portal",
                        StringComparison.OrdinalIgnoreCase))
                .Select(name =>
                {
                    string suffix = name.Substring(6);

                    return int.TryParse(suffix, out int number)
                        ? number
                        : 0;
                })
                .Where(number => number > 0)
                .ToHashSet();

            for (int number = 1; ; number++)
            {
                if (!usedNumbers.Contains(number))
                    return $"Portal{number}";
            }
        }


        // ==========================================
        // SAVE PORTALS
        // ==========================================

        public async Task SavePortalsAsync()
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(_portalsFile)!);

            await using FileStream stream =
                File.Create(_portalsFile);

            await JsonSerializer.SerializeAsync(
                stream, Portals, _jsonOptions);
        }

        // ==========================================
        // ADD PORTAL
        // ==========================================

        public async Task<Portal> AddPortalAsync(string name)
        {
            var portal = new Portal
            {
                Name = GetNextPortalName(),
                Title = name,
                SortOrder = Portals.Count
            };

            Portals.Add(portal);

            await SavePortalsAsync();

            return portal;
        }

        // ==========================================
        // ADD PORTAL FROM DESKTOP RECTANGLE
        // ==========================================

        public async Task<Portal> AddPortalFromRectangleAsync(
            string name,
            double x,
            double y,
            double width,
            double height)
        {
            var portal = new Portal
            {
                Name = GetNextPortalName(),
                Title = name,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                SortOrder = Portals.Count,
                IsCollapsed = false,
                IsVisible = true,
                KeepVisible = true
            };

            Portals.Add(portal);

            await SavePortalsAsync();

            return portal;
        }

        //XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
        // RESTORE DESKTOP ITEMS before deleting the portal
        //XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX



        private void RestoreDesktopItems(Portal portal)
        {
            foreach (var shortcut in portal.Shortcuts)
            {
                string backupPath = shortcut.DesktopBackupPath;
                string originalPath = shortcut.DesktopPath;

                System.Diagnostics.Debug.WriteLine(
                    $"RESTORE: {shortcut.Name}");

                System.Diagnostics.Debug.WriteLine(
                    $"BACKUP: {backupPath}");

                System.Diagnostics.Debug.WriteLine(
                    $"DESTINATION: {originalPath}");

                if (string.IsNullOrWhiteSpace(backupPath) ||
                    string.IsNullOrWhiteSpace(originalPath))
                {
                    System.Diagnostics.Debug.WriteLine(
                        "RESTORE SKIPPED: Missing path");
                    continue;
                }

                bool isFile = File.Exists(backupPath);
                bool isDirectory = Directory.Exists(backupPath);

                if (!isFile && !isDirectory)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "RESTORE SKIPPED: Backup not found");
                    continue;
                }

                string destination = originalPath;

                if (File.Exists(destination) ||
                    Directory.Exists(destination))
                {
                    string directory =
                        Path.GetDirectoryName(originalPath)!;

                    string name =
                        Path.GetFileNameWithoutExtension(originalPath);

                    string extension =
                        Path.GetExtension(originalPath);

                    int counter = 1;

                    do
                    {
                        destination = Path.Combine(
                            directory,
                            $"{name} (Restored {counter++}){extension}");

                    } while (File.Exists(destination) ||
                             Directory.Exists(destination));
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"RESTORING TO: {destination}");

                    if (isFile)
                        File.Move(backupPath, destination);
                    else
                        Directory.Move(backupPath, destination);

                    System.Diagnostics.Debug.WriteLine(
                        "RESTORE SUCCESSFUL");
                }
                catch (Exception ex)
                {
                    // Record the failure, but DO NOT terminate deletion.
                    // The original backup remains available for recovery.

                    System.Diagnostics.Debug.WriteLine(
                        $"RESTORE FAILED: {shortcut.Name}");

                    System.Diagnostics.Debug.WriteLine(
                        $"ERROR: {ex.Message}");

                    // Continue with the next shortcut.
                    continue;
                }
            }
        }



        //ZZZZZZZZZZZZZZZZZZZZ

        private void Restore_DesktopItems(Portal portal)
        {
            foreach (var shortcut in portal.Shortcuts)
            {
                string backupPath = shortcut.DesktopBackupPath;
                string originalPath = shortcut.DesktopPath;

                // Nothing to restore.
                if (string.IsNullOrWhiteSpace(backupPath) ||
                    string.IsNullOrWhiteSpace(originalPath))
                    continue;

                // Check that the backup still exists.
                bool isFile = File.Exists(backupPath);
                bool isDirectory = Directory.Exists(backupPath);

                if (!isFile && !isDirectory)
                    continue;

                // Never overwrite an existing desktop item.
                string destination = originalPath;

                if (File.Exists(destination) ||
                    Directory.Exists(destination))
                {
                    string directory = Path.GetDirectoryName(originalPath)!;
                    string name = Path.GetFileNameWithoutExtension(originalPath);
                    string extension = Path.GetExtension(originalPath);

                    int counter = 1;

                    do
                    {
                        destination = Path.Combine(
                            directory,
                            $"{name} (Restored {counter++}){extension}");

                    } while (File.Exists(destination) ||
                             Directory.Exists(destination));
                }

                // Restore the original item.
                if (isFile)
                    File.Move(backupPath, destination);
                else
                    Directory.Move(backupPath, destination);
            }
        }

        // ==========================================
        // REMOVE PORTAL
        // ==========================================

        public async Task RemovePortalAsync(Guid id)
        {
            var portal = Portals.FirstOrDefault(
                p => p.Id == id);

            if (portal == null)
                return;

            try
            {
                // Restore original desktop items first.
                RestoreDesktopItems(portal);

                // Only remove the Portal after restoration.
                Portals.Remove(portal);

                await SavePortalsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unable to delete Portal: {ex.Message}");

                throw;
            }
        }
    }
}

