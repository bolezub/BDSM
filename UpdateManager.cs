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

            try
            {
                var shutdownTasks = new List<Task>();
                var serversToRestartLater = new HashSet<Guid>();

                foreach (var server in allServersToUpdate)
                {
                    if (server.Status == "Running" || server.Status == "Starting")
                    {
                        serversToRestartLater.Add(server.ServerId);
                    }

                    if (server.Status == "Running")
                    {
                        shutdownTasks.Add(GracefulShutdownAsync(new List<ServerViewModel> { server }, config, "update", false));
                    }
                    else if (server.Status == "Starting")
                    {
                        shutdownTasks.Add(server.KillProcessAsync(force: true));
                    }
                }

                if (shutdownTasks.Any())
                {
                    await Task.WhenAll(shutdownTasks);
                }

                foreach (var server in allServersToUpdate)
                {
                    await RunSteamCmdUpdateForServerAsync(server, config);

                    if (server.Status != "Error")
                    {
                        server.Status = "Stopped";

                        if (serversToRestartLater.Contains(server.ServerId))
                        {
                            await SendDiscordMessageAsync(config, server, "Update complete. Server is restarting...");
                            server.StartServer();
                        }
                    }
                }
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        public static async Task PerformSimpleRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                return;
            }
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
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                return;
            }
            TaskSchedulerService.SetOperationLock();
            try
            {
                var shutdownTasks = activeServers
                    .Where(s => s.Status == "Running")
                    .ToList();

                if (shutdownTasks.Any())
                {
                    await GracefulShutdownAsync(shutdownTasks, config, "maintenance", false);
                }
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        public static async Task PerformScheduledRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                return;
            }
            TaskSchedulerService.SetOperationLock();

            try
            {
                foreach (var server in activeServers)
                {
                    await server.CheckForUpdate();
                }

                var shutdownTasks = new List<Task>();
                foreach (var server in activeServers.Where(s => s.Status == "Running"))
                {
                    shutdownTasks.Add(GracefulShutdownAsync(new List<ServerViewModel> { server }, config, "restart", false));
                }

                if (shutdownTasks.Any())
                {
                    await Task.WhenAll(shutdownTasks);
                }

                foreach (var server in activeServers)
                {
                    if (server.IsUpdateAvailable)
                    {
                        await RunSteamCmdUpdateForServerAsync(server, config);
                    }

                    if (server.Status != "Error")
                    {
                        server.Status = "Stopped";
                        await SendDiscordMessageAsync(config, server, "Server is restarting...");
                        server.StartServer();
                    }
                }
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

            int totalCountdown = 900;
            while (totalCountdown > 0)
            {
                if (!servers.Any(s => s.CurrentPlayers > 0))
                {
                    break;
                }

                var warningTimes = new Dictionary<int, string> { { 600, "10 minutes" }, { 300, "5 minutes" }, { 60, "1 minute" } };
                if (warningTimes.ContainsKey(totalCountdown))
                {
                    string message = $"All servers {reason} in {warningTimes[totalCountdown]}.";
                    foreach (var server in servers)
                    {
                        await server.SendRconCommandAsync($"ServerChat {message}");
                        await SendDiscordMessageAsync(config, server, message);
                    }
                }
                await Task.Delay(10000);
                totalCountdown -= 10;
            }

            foreach (var server in servers)
            {
                server.Status = "Shutting Down";
                string finalMsg = (server.CurrentPlayers == 0) ? $"Server is empty. Shutting down for {reason} now." : $"Final shutdown for {reason}. Goodbye!";
                await server.SendRconCommandAsync($"ServerChat {finalMsg}");
                await SendDiscordMessageAsync(config, server, finalMsg);
                await server.SendRconCommandAsync("DoExit");
            }

            var shutdownTasks = servers.Select(s => WaitForExit(s, config)).ToList();
            await Task.WhenAll(shutdownTasks);

            if (runUpdateAfter)
            {
                var updateTasks = servers.Select(s => RunSteamCmdUpdateForServerAsync(s, config)).ToList();
                await Task.WhenAll(updateTasks);
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
            var processName = "ArkAscendedServer";
            var runningProcesses = Process.GetProcessesByName(processName);
            string serverExePath = Path.Combine(server.InstallDir, "ShooterGame", "Binaries", "Win64");

            foreach (var runningProcess in runningProcesses)
            {
                try
                {
                    if (runningProcess.MainModule != null && Path.GetDirectoryName(runningProcess.MainModule.FileName).Equals(serverExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        string criticalError = $"CRITICAL ERROR: Update for {server.ServerName} aborted. The server process was detected as still running during the update phase. Manifest file was NOT touched.";
                        LoggingService.Log(criticalError, LogLevel.Error);
                        NotificationService.ShowInfo($"Update for {server.ServerName} aborted. See Event Log.");
                        await SendDiscordMessageAsync(config, server, criticalError);
                        server.Status = "Error";
                        return;
                    }
                }
                catch
                {
                    // Ignore processes we can't access
                }
            }

            server.Status = "Updating";
            await SendDiscordMessageAsync(config, server, "Server files are being verified/repaired via SteamCMD...");
            string manifestPath = Path.Combine(server.InstallDir, "steamapps", $"appmanifest_{config.AppId}.acf");
            if (File.Exists(manifestPath))
            {
                try
                {
                    await File.WriteAllTextAsync(manifestPath, "");
                }
                catch (Exception)
                {
                    // Error logging removed
                }
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
                    // Ensure a minimum timeout of 5 minutes.
                    var timeoutMinutes = Math.Max(5, config.SteamCmdTimeoutMinutes);
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes)))
                    {
                        await process.WaitForExitAsync(cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // This block executes if the timeout is reached.
                    try { process.Kill(true); } catch { /* Ignore errors trying to kill */ }

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