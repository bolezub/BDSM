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
                var runningServers = serversToBackup.Where(s => s.Status == "Running").ToList();
                if (runningServers.Any())
                {
                    var saveTasks = runningServers.Select(server => ForceSaveAndWaitAsync(server)).ToList();
                    await Task.WhenAll(saveTasks);
                }

                var backupTasks = serversToBackup.Select(server => CreateServerBackupAsync(server, config)).ToList();
                await Task.WhenAll(backupTasks);

                await CleanupOldBackupsAsync(config);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private static async Task ForceSaveAndWaitAsync(ServerViewModel server)
        {
            string worldFilePath = Path.Combine(server.InstallDir, "ShooterGame", "Saved", "SavedArks", $"{server.MapFolder}.ark");

            if (!File.Exists(worldFilePath))
            {
                Debug.WriteLine($"Backup: Could not find world file for {server.ServerName} at {worldFilePath}. Skipping save.");
                return;
            }

            try
            {
                DateTime originalWriteTime = File.GetLastWriteTimeUtc(worldFilePath);

                Debug.WriteLine($"Backup: Sending SaveWorld command to {server.ServerName}. Original save time: {originalWriteTime}");
                await server.SendRconCommandAsync("SaveWorld");

                Stopwatch timeout = Stopwatch.StartNew();
                while (timeout.Elapsed.TotalSeconds < 60)
                {
                    await Task.Delay(2000); // Check every 2 seconds
                    DateTime currentWriteTime = File.GetLastWriteTimeUtc(worldFilePath);

                    if (currentWriteTime > originalWriteTime)
                    {
                        Debug.WriteLine($"Backup: New save file detected for {server.ServerName} at {currentWriteTime}. Save complete.");
                        return;
                    }
                }
                Debug.WriteLine($"Backup WARNING: Timed out waiting for {server.ServerName} to save. Proceeding with backup anyway.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Backup ERROR: Failed to force-save or wait for {server.ServerName}. {ex.Message}");
            }
        }

        private static Task CreateServerBackupAsync(ServerViewModel server, GlobalConfig config)
        {
            return Task.Run(() =>
            {
                try
                {
                    string saveDir = Path.Combine(server.InstallDir, "ShooterGame", "Saved", "SavedArks");
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
                        var filesToBackup = Directory.EnumerateFiles(saveDir, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith("202_"));

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

        private static Task CleanupOldBackupsAsync(GlobalConfig config)
        {
            return Task.Run(() =>
            {
                try
                {
                    Debug.WriteLine("Backup: Cleaning up old backups...");
                    var allBackups = Directory.GetFiles(config.BackupPath, "*.zip", SearchOption.AllDirectories);
                    int filesDeleted = 0;
                    DateTime cutoff = DateTime.Now.AddDays(-30);

                    foreach (var file in allBackups)
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
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
    }
}