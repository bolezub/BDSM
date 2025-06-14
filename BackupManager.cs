using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CoreRCON;

namespace BDSM
{
    public static class BackupManager
    {
        public static async Task PerformBackupAsync(IEnumerable<ServerViewModel> serversToBackup, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                Debug.WriteLine("Backup skipped: A major operation is already in progress.");
                return;
            }

            TaskSchedulerService.SetOperationLock();
            try
            {
                // First, force-save any running servers
                foreach (var server in serversToBackup)
                {
                    if (server.Status == "Running")
                    {
                        await ForceSaveAndWaitAsync(server, config);
                    }
                }

                // Now, perform the backup for each server
                foreach (var server in serversToBackup)
                {
                    await CreateServerBackupAsync(server, config);
                }

                // Finally, clean up old backups
                await CleanupOldBackupsAsync(config);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private static async Task ForceSaveAndWaitAsync(ServerViewModel server, GlobalConfig config)
        {
            // This path needs to be constructed carefully based on your config structure
            string worldFilePath = Path.Combine(server.InstallDir, "ShooterGame", "Saved", "SavedArks", $"{server.MapFolder}.ark");

            if (!File.Exists(worldFilePath))
            {
                Debug.WriteLine($"Backup: Could not find world file for {server.ServerName} at {worldFilePath}. Skipping save.");
                return;
            }

            try
            {
                Debug.WriteLine($"Backup: Sending SaveWorld command to {server.ServerName}.");
                await SendRconCommandAsync(server, config, "SaveWorld");

                DateTime originalWriteTime = File.GetLastWriteTimeUtc(worldFilePath);
                Debug.WriteLine($"Backup: Original save file timestamp for {server.ServerName}: {originalWriteTime}");

                // The "Smarter Wait" loop
                Stopwatch timeout = Stopwatch.StartNew();
                while (timeout.Elapsed.TotalSeconds < 60) // 60-second timeout
                {
                    await Task.Delay(1000); // Check every second
                    DateTime currentWriteTime = File.GetLastWriteTimeUtc(worldFilePath);

                    if (currentWriteTime > originalWriteTime)
                    {
                        Debug.WriteLine($"Backup: New save file detected for {server.ServerName} at {currentWriteTime}. Save complete.");
                        return; // Success
                    }
                }
                Debug.WriteLine($"Backup WARNING: Timed out waiting for {server.ServerName} to save. Proceeding with backup anyway.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Backup ERROR: Failed to force-save or wait for {server.ServerName}. {ex.Message}");
            }
        }

        private static async Task CreateServerBackupAsync(ServerViewModel server, GlobalConfig config)
        {
            await Task.Run(() =>
            {
                try
                {
                    // This path also needs to align with your config/server properties
                    string saveDir = Path.Combine(server.InstallDir, "ShooterGame", "Saved", "SavedArks", server.MapFolder);
                    if (!Directory.Exists(saveDir))
                    {
                        Debug.WriteLine($"Backup: Save directory not found for {server.ServerName} at {saveDir}. Skipping backup.");
                        return;
                    }

                    string serverBackupDir = Path.Combine(config.BackupPath, server.ServerName);
                    Directory.CreateDirectory(serverBackupDir);

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                    string archivePath = Path.Combine(serverBackupDir, $"{timestamp}.zip");

                    Debug.WriteLine($"Backup: Creating archive for {server.ServerName} at {archivePath}");

                    if (File.Exists(archivePath)) File.Delete(archivePath);

                    using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                    {
                        var filesToBackup = Directory.GetFiles(saveDir, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => !f.EndsWith(".bak") && !Path.GetFileName(f).StartsWith("202_"));

                        foreach (var filePath in filesToBackup)
                        {
                            archive.CreateEntryFromFile(filePath, Path.GetFileName(filePath), CompressionLevel.Optimal);
                        }
                    }
                    Debug.WriteLine($"Backup: Successfully created archive for {server.ServerName}.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Backup ERROR: Failed to create backup archive for {server.ServerName}. {ex.Message}");
                }
            });
        }

        private static async Task CleanupOldBackupsAsync(GlobalConfig config)
        {
            await Task.Run(() =>
            {
                try
                {
                    Debug.WriteLine("Backup: Cleaning up old backups...");
                    var allBackups = Directory.GetFiles(config.BackupPath, "*.zip", SearchOption.AllDirectories);
                    int filesDeleted = 0;
                    foreach (var file in allBackups)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-8))
                        {
                            fileInfo.Delete();
                            filesDeleted++;
                        }
                    }
                    Debug.WriteLine($"Backup: Cleanup complete. Deleted {filesDeleted} old backup files.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Backup ERROR: Failed during old backup cleanup. {ex.Message}");
                }
            });
        }

        private static async Task SendRconCommandAsync(ServerViewModel server, GlobalConfig config, string command)
        {
            try
            {
                using (var rcon = new RCON(System.Net.IPAddress.Parse(config.ServerIP), (ushort)server.RconPort, config.RconPassword))
                {
                    await rcon.ConnectAsync();
                    await rcon.SendCommandAsync(command);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Backup RCON ERROR for {server.ServerName}: {ex.Message}");
            }
        }
    }
}