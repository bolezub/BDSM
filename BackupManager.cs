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
                return;
            }

            try
            {
                DateTime originalWriteTime = File.GetLastWriteTimeUtc(worldFilePath);

                await server.SendRconCommandAsync("SaveWorld");

                Stopwatch timeout = Stopwatch.StartNew();
                while (timeout.Elapsed.TotalSeconds < 60)
                {
                    await Task.Delay(2000);
                    DateTime currentWriteTime = File.GetLastWriteTimeUtc(worldFilePath);

                    if (currentWriteTime > originalWriteTime)
                    {
                        return;
                    }
                }
            }
            catch (Exception)
            {
                // Error logging removed
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
                        return;
                    }

                    string serverBackupDir = Path.Combine(config.BackupPath, server.ServerName);
                    Directory.CreateDirectory(serverBackupDir);

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                    string archivePath = Path.Combine(serverBackupDir, $"{timestamp}.zip");

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
                }
                catch (Exception)
                {
                    // Error logging removed
                }
            });
        }

        private static Task CleanupOldBackupsAsync(GlobalConfig config)
        {
            return Task.Run(() =>
            {
                try
                {
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
                }
                catch (Exception)
                {
                    // Error logging removed
                }
            });
        }
    }
}