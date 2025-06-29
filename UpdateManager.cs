using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CoreRCON;
using Newtonsoft.Json.Linq;

namespace BDSM
{
    public class UpdateCheckResult
    {
        public string InstalledBuild { get; set; } = "N/A";
        public string LatestBuild { get; set; } = "N/A";
        public bool IsUpdateAvailable { get; set; } = false;
    }

    public static class UpdateManager
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task PerformUpdateProcessAsync(List<ServerViewModel> allServersToUpdate, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                return;
            }
            TaskSchedulerService.SetOperationLock();

            var steamCmdSemaphore = new SemaphoreSlim(1, 1);

            try
            {
                var updatePipelineTasks = new List<Task>();

                foreach (var server in allServersToUpdate)
                {
                    var pipelineTask = Task.Run(async () =>
                    {
                        // Capture the server's state BEFORE the update process begins
                        bool shouldRestart = server.Status == "Running" || server.Status == "Starting";

                        // Part 1: Shutdown
                        if (server.Status == "Running")
                        {
                            await GracefulShutdownAsync(new List<ServerViewModel> { server }, config, "update", false);
                        }
                        else if (server.Status == "Starting")
                        {
                            await server.KillProcessAsync(force: true);
                        }

                        // Part 2: Final Process Verification
                        bool processIsGone = await EnsureProcessHasExited(server);
                        if (!processIsGone)
                        {
                            string errorMsg = $"CRITICAL: Update for {server.ServerName} aborted. The process did not exit after shutdown command.";
                            LoggingService.Log(errorMsg, LogLevel.Error);
                            NotificationService.ShowInfo(errorMsg);
                            server.Status = "Error";
                            return;
                        }

                        // Part 3: Enter the Update Queue and Execute
                        await steamCmdSemaphore.WaitAsync();
                        try
                        {
                            await RunSteamCmdUpdateForServerAsync(server, config);
                        }
                        finally
                        {
                            steamCmdSemaphore.Release();
                        }

                        // If SteamCMD itself reported an error, abort here.
                        if (server.Status == "Error")
                        {
                            return;
                        }

                        // --- Part 4: Post-Update Finalization (THE FIX) ---
                        // 1. Set status to Stopped to indicate the update operation is complete.
                        server.Status = "Stopped";

                        // 2. Re-check the server version to refresh the UI and remove the update icon.
                        await server.CheckForUpdate();

                        // --- Part 5: Restart (if needed) ---
                        // This will now work because the preconditions (Status is "Stopped") are met.
                        if (shouldRestart)
                        {
                            await SendDiscordMessageAsync(config, server, "Update complete. Server is restarting...");
                            server.StartServer();
                        }
                    });

                    updatePipelineTasks.Add(pipelineTask);
                }

                await Task.WhenAll(updatePipelineTasks);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private static async Task<bool> EnsureProcessHasExited(ServerViewModel server)
        {
            int checks = 0;
            while (checks < 10)
            {
                var process = Process.GetProcessesByName("ArkAscendedServer")
                                     .FirstOrDefault(p => {
                                         try { return p.MainModule?.FileName.StartsWith(server.InstallDir, StringComparison.OrdinalIgnoreCase) ?? false; }
                                         catch { return false; }
                                     });
                if (process == null)
                {
                    return true;
                }
                await Task.Delay(1000);
                checks++;
            }
            return false;
        }

        public static async Task PerformSimpleRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress) return;
            TaskSchedulerService.SetOperationLock();
            try
            {
                var runningServers = activeServers.Where(s => s.Status == "Running").ToList();
                if (runningServers.Any())
                {
                    await GracefulShutdownAsync(runningServers, config, "scheduled restart", false);
                }

                foreach (var server in activeServers)
                {
                    if (server.Status == "Stopped")
                    {
                        server.StartServer();
                        await Task.Delay(5000);
                    }
                }
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        public static async Task InstallServerAsync(ServerConfig serverConfig, GlobalConfig globalConfig)
        {
            string steamCmdArgs = $"+login anonymous +force_install_dir \"{serverConfig.InstallDir}\" +app_update {globalConfig.AppId} validate +quit";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = globalConfig.SteamCMDPath,
                Arguments = steamCmdArgs,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(processStartInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }

        public static async Task PerformMaintenanceShutdownAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress) return;
            TaskSchedulerService.SetOperationLock();
            try
            {
                var runningServers = activeServers.Where(s => s.Status == "Running").ToList();
                if (runningServers.Any())
                {
                    await GracefulShutdownAsync(runningServers, config, "maintenance", false);
                }
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        public static async Task PerformScheduledRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress) return;
            TaskSchedulerService.SetOperationLock();
            try
            {
                await Task.WhenAll(activeServers.Select(s => s.CheckForUpdate()));

                var serversToUpdate = activeServers.Where(s => s.IsUpdateAvailable).ToList();
                var serversToReboot = activeServers.Where(s => !s.IsUpdateAvailable).ToList();

                var updateTask = serversToUpdate.Any() ? PerformUpdateProcessAsync(serversToUpdate, config) : Task.CompletedTask;
                var rebootTask = serversToReboot.Any() ? PerformSimpleRebootAsync(serversToReboot, config) : Task.CompletedTask;

                await Task.WhenAll(updateTask, rebootTask);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private static async Task GracefulShutdownAsync(List<ServerViewModel> servers, GlobalConfig config, string reason, bool runUpdateAfter)
        {
            foreach (var server in servers)
            {
                server.Status = "Update Pending";
                string actionWord = reason == "maintenance" ? "shutting down" : "restarting";
                string initialMsg = $"Server {reason} initiated. Server {actionWord} in 15 minutes or when empty.";
                await server.SendRconCommandAsync($"ServerChat {initialMsg}");
                await SendDiscordMessageAsync(config, server, initialMsg);
            }

            var serverCheckTasks = servers.Select(s => WaitForServerEmptyOrTimeout(s, config.ShutdownTimeoutSeconds)).ToList();
            await Task.WhenAll(serverCheckTasks);

            foreach (var server in servers)
            {
                server.Status = "Shutting Down";
                string finalMsg = (server.CurrentPlayers == 0) ? $"Server is empty. Shutting down for {reason} now." : $"Final shutdown for {reason}. Goodbye!";
                await server.SendRconCommandAsync($"ServerChat {finalMsg}");
                await SendDiscordMessageAsync(config, server, finalMsg);
                await server.SendRconCommandAsync("DoExit");
            }

            await Task.WhenAll(servers.Select(s => WaitForExit(s, config)));
        }

        private static async Task WaitForServerEmptyOrTimeout(ServerViewModel server, int timeoutSeconds)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                if (server.CurrentPlayers == 0) break;
                await Task.Delay(10000);
            }
        }

        private static async Task WaitForExit(ServerViewModel server, GlobalConfig config)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < config.ShutdownTimeoutSeconds)
            {
                if (server.ServerProcess == null || server.ServerProcess.HasExited)
                {
                    server.Status = "Stopped";
                    return;
                }
                await Task.Delay(1000);
            }
            stopwatch.Stop();
            await server.KillProcessAsync(force: true);
            server.Status = "Stopped";
        }

        public static async Task RunSteamCmdUpdateForServerAsync(ServerViewModel server, GlobalConfig config)
        {
            server.Status = "Updating";
            await SendDiscordMessageAsync(config, server, "Server files are being updated/repaired via SteamCMD...");

            // --- NEW LOGIC: Clear the .acf manifest file to force an update ---
            try
            {
                // Construct the path to the appmanifest file.
                string manifestPath = Path.Combine(server.InstallDir, "steamapps", $"appmanifest_{config.AppId}.acf");
                if (File.Exists(manifestPath))
                {
                    // By writing an empty string, we corrupt the manifest, forcing SteamCMD to
                    // re-validate and download the latest version from scratch.
                    await File.WriteAllTextAsync(manifestPath, string.Empty);
                    LoggingService.Log($"Cleared manifest file for {server.ServerName} to force update.", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Could not clear manifest file for {server.ServerName}. Proceeding anyway. Error: {ex.Message}", LogLevel.Warning);
            }
            // --- END OF NEW LOGIC ---

            string steamCmdArgs = $"+login anonymous +force_install_dir \"{server.InstallDir}\" +app_update {config.AppId} validate +quit";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = config.SteamCMDPath,
                Arguments = steamCmdArgs,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            var process = Process.Start(processStartInfo);
            if (process != null)
            {
                try
                {
                    var timeoutMinutes = Math.Max(5, config.SteamCmdTimeoutMinutes);
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { /* Ignore */ }
                    string timeoutError = $"ERROR: SteamCMD update for {server.ServerName} timed out after {config.SteamCmdTimeoutMinutes} minutes and was aborted.";
                    LoggingService.Log(timeoutError, LogLevel.Error);
                    NotificationService.ShowInfo($"Update for {server.ServerName} timed out. See Event Log.");
                    await SendDiscordMessageAsync(config, server, timeoutError);
                    server.Status = "Error";
                }
            }
        }
        public static async Task<UpdateCheckResult> CheckForUpdateAsync(string installDir, string appId, string apiUrl)
        {
            var result = new UpdateCheckResult();
            result.InstalledBuild = await GetInstalledBuildIdAsync(installDir, appId);
            string? latestBuild = await GetLatestBuildIdAsync(apiUrl, appId);

            bool canTrustInstalledVersion = long.TryParse(result.InstalledBuild, out _);
            bool canTrustLatestVersion = !string.IsNullOrEmpty(latestBuild) && latestBuild != "0";

            if (canTrustInstalledVersion && canTrustLatestVersion)
            {
                result.LatestBuild = latestBuild;
                result.IsUpdateAvailable = result.InstalledBuild != result.LatestBuild;
            }
            else
            {
                result.LatestBuild = canTrustLatestVersion ? latestBuild : "API Error";
                result.IsUpdateAvailable = false;
            }
            return result;
        }

        public static async Task<string> GetInstalledBuildIdAsync(string installDir, string appId)
        {
            try
            {
                string manifestPath = Path.Combine(installDir, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(manifestPath)) return "Not Found";
                string content = await File.ReadAllTextAsync(manifestPath);
                Match match = Regex.Match(content, @"""buildid""\s+""(\d+)""");
                return match.Success ? match.Groups[1].Value : "Unknown";
            }
            catch (Exception)
            {
                return "Error";
            }
        }

        public static async Task<string?> GetLatestBuildIdAsync(string apiUrl, string appId)
        {
            try
            {
                string responseString = await httpClient.GetStringAsync(apiUrl);
                JObject jsonResponse = JObject.Parse(responseString);
                string? buildId = jsonResponse?["data"]?[appId]?["depots"]?["branches"]?["public"]?["buildid"]?.ToString();
                return buildId;
            }
            catch (Exception)
            {
                return "Error";
            }
        }

        private static async Task SendDiscordMessageAsync(GlobalConfig config, ServerViewModel server, string message)
        {
            if (server.DiscordNotificationsEnabled)
            {
                await DiscordNotifier.SendMessageAsync(config.discordWebhookUrl, server.ServerName, message, server.OnlinePlayers);
            }
        }
    }
}