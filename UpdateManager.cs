using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
                Debug.WriteLine("Update Process skipped: A major operation is already in progress.");
                return;
            }

            TaskSchedulerService.SetOperationLock();
            try
            {
                var updateTasks = allServersToUpdate.Select(server => HandleSingleServerUpdate(server, config, shouldRestart: server.Status == "Running")).ToList();
                await Task.WhenAll(updateTasks);
                Debug.WriteLine("All update tasks have been processed.");
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        public static async Task InstallServerAsync(ServerConfig serverConfig, GlobalConfig globalConfig)
        {
            Debug.WriteLine($"Starting installation for {serverConfig.Name} in {serverConfig.InstallDir}");
            string steamCmdArgs = $"+login anonymous +force_install_dir \"{serverConfig.InstallDir}\" +app_update {globalConfig.AppId} validate +quit";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = globalConfig.SteamCMDPath,
                Arguments = steamCmdArgs,
                UseShellExecute = true,
                CreateNoWindow = false
            };
            Debug.WriteLine($"Starting SteamCMD for {serverConfig.Name}");
            var process = Process.Start(processStartInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                Debug.WriteLine($"SteamCMD installation finished for {serverConfig.Name}.");
            }
        }

        public static async Task PerformMaintenanceShutdownAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            if (TaskSchedulerService.IsMajorOperationInProgress)
            {
                Debug.WriteLine("Maintenance shutdown skipped: A major operation is already in progress.");
                return;
            }
            TaskSchedulerService.SetOperationLock();
            try
            {
                var shutdownTasks = activeServers
                    .Where(s => s.Status == "Running")
                    .Select(server => HandleSingleServerMaintenance(server, config))
                    .ToList();
                if (shutdownTasks.Any())
                {
                    await Task.WhenAll(shutdownTasks);
                    Debug.WriteLine("All maintenance shutdown tasks have been processed.");
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
                Debug.WriteLine("Scheduled reboot skipped: A major operation is already in progress.");
                return;
            }
            TaskSchedulerService.SetOperationLock();
            try
            {
                var rebootTasks = activeServers.Select(server => HandleSingleServerUpdate(server, config, shouldRestart: true)).ToList();
                await Task.WhenAll(rebootTasks);
                Debug.WriteLine("All scheduled reboot tasks have been processed.");
            }
            finally
            {
                TaskSchedulerService.ReleaseOperationLock();
            }
        }

        private static async Task HandleSingleServerUpdate(ServerViewModel server, GlobalConfig config, bool shouldRestart)
        {
            if (server.Status == "Starting")
            {
                await SendDiscordMessageAsync(config, server, "Server was in a 'Starting' state. Killing process to ensure a clean update.");
                await server.KillProcessAsync(force: true);
                await Task.Delay(2000);
            }
            else if (server.Status == "Running")
            {
                await GracefulShutdownAsync(server, config, "update");
            }
            await RunSteamCmdUpdateForServerAsync(server, config);
            server.Status = "Stopped";
            if (shouldRestart)
            {
                await SendDiscordMessageAsync(config, server, "Update complete. Server is restarting...");
                server.StartServer();
            }
            else
            {
                server.Status = "Stopped";
                await SendDiscordMessageAsync(config, server, "Update complete. Server remains stopped.");
            }
            await server.CheckForUpdate();
        }

        private static async Task HandleSingleServerMaintenance(ServerViewModel server, GlobalConfig config)
        {
            if (server.Status == "Running")
            {
                await GracefulShutdownAsync(server, config, "maintenance");
            }
            server.Status = "Stopped";
        }

        private static async Task GracefulShutdownAsync(ServerViewModel server, GlobalConfig config, string reason)
        {
            server.Status = "Update Pending";
            string actionWord = reason == "maintenance" ? "shutting down" : "restarting";
            string initialMsg = $"Server {reason} initiated. Server {actionWord} in 15 minutes or when empty.";
            await server.SendRconCommandAsync($"ServerChat {initialMsg}");
            await SendDiscordMessageAsync(config, server, initialMsg);
            if (server.OnlinePlayers.Any())
            {
                string playerListMsg = $"Players online: {string.Join(", ", server.OnlinePlayers)}";
                await server.SendRconCommandAsync($"ServerChat {playerListMsg}");
            }
            int totalCountdown = 900;
            var warningTimes = new Dictionary<int, string>
            {
                { 600, "10 minutes" }, { 300, "5 minutes" }, { 60, "1 minute" }
            };
            while (totalCountdown > 0)
            {
                if (server.CurrentPlayers == 0)
                {
                    Debug.WriteLine($"Server {server.ServerName} is empty. Shutting down early.");
                    break;
                }
                if (warningTimes.ContainsKey(totalCountdown))
                {
                    string message = $"Server {actionWord} for {reason} in {warningTimes[totalCountdown]}.";
                    await server.SendRconCommandAsync($"ServerChat {message}");
                    await SendDiscordMessageAsync(config, server, message);
                    if (server.OnlinePlayers.Any())
                    {
                        string playerListMsg = $"Players online: {string.Join(", ", server.OnlinePlayers)}";
                        await server.SendRconCommandAsync($"ServerChat {playerListMsg}");
                    }
                }
                await Task.Delay(10000);
                totalCountdown -= 10;
            }
            server.Status = "Shutting Down";
            string finalMsg = (server.CurrentPlayers == 0) ? $"Server is empty. Shutting down for {reason} now." : $"Final shutdown for {reason}. Goodbye!";
            await server.SendRconCommandAsync($"ServerChat {finalMsg}");
            await SendDiscordMessageAsync(config, server, finalMsg);
            await server.SendRconCommandAsync("DoExit");
            Debug.WriteLine($"Waiting for server {server.ServerName} process to exit...");
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < config.ShutdownTimeoutSeconds)
            {
                if (server.ServerProcess == null || server.ServerProcess.HasExited)
                {
                    Debug.WriteLine($"Server {server.ServerName} process has exited gracefully.");
                    stopwatch.Stop();
                    server.Status = "Stopped";
                    return;
                }
                await Task.Delay(1000);
            }
            stopwatch.Stop();
            Debug.WriteLine($"WARNING: Server {server.ServerName} did not shut down within {config.ShutdownTimeoutSeconds} seconds. Forcing kill.");
            await server.KillProcessAsync(force: true);
            server.Status = "Stopped";
        }

        public static async Task RunSteamCmdUpdateForServerAsync(ServerViewModel server, GlobalConfig config)
        {
            server.Status = "Updating";
            await SendDiscordMessageAsync(config, server, "Server files are being verified/repaired via SteamCMD...");
            string manifestPath = Path.Combine(server.InstallDir, "steamapps", $"appmanifest_{config.AppId}.acf");
            if (File.Exists(manifestPath))
            {
                try
                {
                    Debug.WriteLine($"Clearing manifest for {server.ServerName}");
                    await File.WriteAllTextAsync(manifestPath, "");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not clear manifest file for {server.ServerName}: {ex.Message}");
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
            Debug.WriteLine($"Starting SteamCMD for {server.ServerName}");
            var process = Process.Start(processStartInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                Debug.WriteLine($"SteamCMD update finished for {server.ServerName}.");
            }
        }

        // THIS METHOD CONTAINS THE CRITICAL FIX
        public static async Task<UpdateCheckResult> CheckForUpdateAsync(string installDir, string appId, string apiUrl)
        {
            var result = new UpdateCheckResult();
            result.InstalledBuild = await GetInstalledBuildIdAsync(installDir, appId);
            string? latestBuild = await GetLatestBuildIdAsync(apiUrl, appId);

            // NEW DEFENSIVE LOGIC: Only flag an update if we have valid data for BOTH installed and latest versions.
            bool canTrustInstalledVersion = long.TryParse(result.InstalledBuild, out _);
            bool canTrustLatestVersion = !string.IsNullOrEmpty(latestBuild) && latestBuild != "0";

            if (canTrustInstalledVersion && canTrustLatestVersion)
            {
                // We have two valid numbers to compare.
                result.LatestBuild = latestBuild;
                result.IsUpdateAvailable = result.InstalledBuild != result.LatestBuild;
            }
            else
            {
                // If we can't determine either version, we FAIL SAFE and assume no update is available.
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read buildid: {ex.Message}");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get latest buildid from Steam API: {ex.Message}");
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