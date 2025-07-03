using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static async Task PerformUpdateProcessAsync(List<ServerViewModel> allServersToUpdate, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                return;
            }
            await WatchdogService.DisplayMaintenanceMessageAsync("SERVER UPDATE IN PROGRESS");
            TaskSchedulerService.SetOperationLock();

            var steamCmdSemaphore = new SemaphoreSlim(1, 1);

            try
            {
                var updatePipelineTasks = new List<Task>();

                foreach (var server in allServersToUpdate)
                {
                    var pipelineTask = Task.Run(async () =>
                    {
                        bool shouldRestart = server.Status == "Running" || server.Status == "Starting";

                        if (server.Status == "Running")
                        {
                            await GracefulShutdownAsync(new List<ServerViewModel> { server }, config, "update");
                        }
                        else if (server.Status == "Starting")
                        {
                            await server.KillProcessAsync(force: true);
                        }

                        bool processIsGone = await EnsureProcessHasExited(server);
                        if (!processIsGone)
                        {
                            string errorMsg = $"CRITICAL: Update for {server.ServerName} aborted. The process did not exit after shutdown command.";
                            LoggingService.Log(errorMsg, LogLevel.Error);
                            NotificationService.ShowInfo(errorMsg);
                            server.Status = "Error";
                            return;
                        }

                        await steamCmdSemaphore.WaitAsync();
                        try
                        {
                            await RunSteamCmdUpdateForServerAsync(server, config);
                        }
                        finally
                        {
                            steamCmdSemaphore.Release();
                        }

                        if (server.Status == "Error")
                        {
                            return;
                        }

                        server.Status = "Stopped";

                        string? latestBuild = await GetLatestBuildIdAsync(config.SteamApiUrl, config.AppId);
                        await server.CheckForUpdate(latestBuild);

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

        // --- THIS METHOD HAS BEEN RESTRUCTURED ---
        public static async Task PerformSimpleRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress) return;
            await WatchdogService.DisplayMaintenanceMessageAsync("DAILY RESTART IN PROGRESS");
            TaskSchedulerService.SetOperationLock();
            try
            {
                var rebootTasks = activeServers.Select(server => Task.Run(async () =>
                {
                    if (server.Status == "Running")
                    {
                        // If the server is running, perform the graceful shutdown and then restart.
                        await GracefulShutdownAsync(new List<ServerViewModel> { server }, config, "daily restart");
                        if (server.Status == "Stopped")
                        {
                            server.StartServer();
                        }
                    }
                    else if (server.Status == "Stopped")
                    {
                        // If the server is already stopped, just start it.
                        LoggingService.Log($"Server '{server.ServerName}' was stopped, starting it as part of daily restart.", LogLevel.Info);
                        server.StartServer();
                    }
                })).ToList();

                await Task.WhenAll(rebootTasks);
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
            await WatchdogService.DisplayMaintenanceMessageAsync("MAINTENANCE SHUTDOWN IN PROGRESS");
            TaskSchedulerService.SetOperationLock();
            try
            {
                var shutdownTasks = activeServers
                    .Where(s => s.Status == "Running")
                    .Select(server => GracefulShutdownAsync(new List<ServerViewModel> { server }, config, "maintenance"))
                    .ToList();
                
                await Task.WhenAll(shutdownTasks);
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        public static async Task PerformScheduledRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress) return;
            await WatchdogService.DisplayMaintenanceMessageAsync("SCHEDULED RESTART/UPDATE IN PROGRESS");
            TaskSchedulerService.SetOperationLock();
            try
            {
                string? latestBuild = await GetLatestBuildIdAsync(config.SteamApiUrl, config.AppId);
                
                await Task.WhenAll(activeServers.Select(s => s.CheckForUpdate(latestBuild)));

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

        private static async Task GracefulShutdownAsync(List<ServerViewModel> servers, GlobalConfig config, string reason)
        {
            foreach (var server in servers)
            {
                server.Status = "Update Pending";
                string actionWord = reason == "maintenance" ? "shutting down" : "restarting";

                int shutdownMinutes = config.ShutdownTimeoutSeconds > 0 ? config.ShutdownTimeoutSeconds / 60 : 15;
                string initialMsg = $"Server {reason} initiated. Server {actionWord} in {shutdownMinutes} minutes or when empty.";

                await server.SendRconCommandAsync($"ServerChat {initialMsg}");
                await SendDiscordMessageAsync(config, server, initialMsg);

                await WaitForServerEmptyOrTimeout(server, config, reason, actionWord);

                server.Status = "Shutting Down";
                string finalMsg = (server.CurrentPlayers == 0) ? $"Server is empty. Shutting down for {reason} now." : $"Final shutdown for {reason}. Goodbye!";
                await server.SendRconCommandAsync($"ServerChat {finalMsg}");
                await SendDiscordMessageAsync(config, server, finalMsg);
                await server.SendRconCommandAsync("DoExit");
            }

            await Task.WhenAll(servers.Select(s => WaitForExit(s, config)));
        }

        private static async Task WaitForServerEmptyOrTimeout(ServerViewModel server, GlobalConfig config, string reason, string actionWord)
        {
            var stopwatch = Stopwatch.StartNew();
            int effectiveTimeout = config.ShutdownTimeoutSeconds > 0 ? config.ShutdownTimeoutSeconds : 900;
            int lastMinuteAnnounced = (int)Math.Ceiling(effectiveTimeout / 60.0);

            while (stopwatch.Elapsed.TotalSeconds < effectiveTimeout)
            {
                await server.UpdateServerStatus();

                if (server.CurrentPlayers == 0) break;

                int minutesRemaining = (int)Math.Ceiling((effectiveTimeout - stopwatch.Elapsed.TotalSeconds) / 60);

                if (minutesRemaining < lastMinuteAnnounced)
                {
                    if (minutesRemaining > 0)
                    {
                        var sb = new StringBuilder();
                        sb.Append($"Server {actionWord} for {reason} in {minutesRemaining} minute(s).");
                        
                        if (server.OnlinePlayers.Any())
                        {
                            sb.Append($" Players online: {string.Join(", ", server.OnlinePlayers)}");
                        }

                        string warningMsg = sb.ToString();
                        await server.SendRconCommandAsync($"ServerChat {warningMsg}");
                        await SendDiscordMessageAsync(config, server, $"Server {actionWord} for {reason} in {minutesRemaining} minute(s).");
                    }
                    lastMinuteAnnounced = minutesRemaining;
                }

                await Task.Delay(10000);
            }
        }

        private static async Task WaitForExit(ServerViewModel server, GlobalConfig config)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < 60)
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

            try
            {
                string manifestPath = Path.Combine(server.InstallDir, "steamapps", $"appmanifest_{config.AppId}.acf");
                if (File.Exists(manifestPath))
                {
                    await File.WriteAllTextAsync(manifestPath, string.Empty);
                    LoggingService.Log($"Cleared manifest file for {server.ServerName} to force update.", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Could not clear manifest file for {server.ServerName}. Proceeding anyway. Error: {ex.Message}", LogLevel.Warning);
            }

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
            catch (HttpRequestException httpEx)
            {
                LoggingService.Log($"Steam API request failed. Status: {httpEx.StatusCode?.ToString() ?? "N/A"}. Message: {httpEx.Message}", LogLevel.Warning);
                return null; 
            }
            catch (Exception ex)
            {
                LoggingService.Log($"An unexpected error occurred while fetching latest build ID: {ex.Message}", LogLevel.Warning);
                return null;
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