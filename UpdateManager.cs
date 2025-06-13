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

        #region Public Methods (Initiators)

        public static async Task PerformUpdateProcessAsync(List<ServerViewModel> allServersToUpdate, GlobalConfig config)
        {
            var updateTasks = allServersToUpdate.Select(server => HandleSingleServerUpdate(server, config, shouldRestart: server.Status == "Running")).ToList();
            await Task.WhenAll(updateTasks);
            Debug.WriteLine("All manual update tasks have been processed.");
        }

        public static async Task PerformMaintenanceShutdownAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            var shutdownTasks = activeServers.Select(server => HandleSingleServerMaintenance(server, config)).ToList();
            await Task.WhenAll(shutdownTasks);
            Debug.WriteLine("All maintenance shutdown tasks have been processed.");
        }

        public static async Task PerformScheduledRebootAsync(List<ServerViewModel> activeServers, GlobalConfig config)
        {
            var rebootTasks = activeServers.Select(server => HandleSingleServerUpdate(server, config, shouldRestart: true)).ToList();
            await Task.WhenAll(rebootTasks);
            Debug.WriteLine("All scheduled reboot tasks have been processed.");
        }

        #endregion

        #region Core Server Handling Logic

        private static async Task HandleSingleServerUpdate(ServerViewModel server, GlobalConfig config, bool shouldRestart)
        {
            if (server.Status == "Running")
            {
                await GracefulShutdownAsync(server, config, "update");
            }
            await RunSteamCmdUpdateForServerAsync(server, config);
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

        #endregion

        #region Private Helper Methods (The Steps)

        // --- MODIFIED: This method now also sends the player list to in-game chat ---
        private static async Task GracefulShutdownAsync(ServerViewModel server, GlobalConfig config, string reason)
        {
            server.Status = "Update Pending";

            // Send initial 15-minute warning
            string initialMsg = $"Server {reason} initiated. Restarting in 15 minutes or when empty.";
            await SendRconCommandAsync(server, config, $"ServerChat {initialMsg}");
            await SendDiscordMessageAsync(config, server, initialMsg);

            // Send initial player list to game chat if players are online
            if (server.OnlinePlayers.Any())
            {
                string playerListMsg = $"Players online: {string.Join(", ", server.OnlinePlayers)}";
                await SendRconCommandAsync(server, config, $"ServerChat {playerListMsg}");
            }

            int totalCountdown = 900; // 15 minutes in seconds
            var warningTimes = new Dictionary<int, string>
            {
                { 600, "10 minutes" },
                { 300, "5 minutes" },
                { 60, "1 minute" }
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
                    // Send the timed warning message
                    string message = $"Server restarting for {reason} in {warningTimes[totalCountdown]}.";
                    await SendRconCommandAsync(server, config, $"ServerChat {message}");
                    await SendDiscordMessageAsync(config, server, message);

                    // Send the current player list to game chat
                    if (server.OnlinePlayers.Any())
                    {
                        string playerListMsg = $"Players online: {string.Join(", ", server.OnlinePlayers)}";
                        await SendRconCommandAsync(server, config, $"ServerChat {playerListMsg}");
                    }
                }

                await Task.Delay(10000);
                totalCountdown -= 10;
            }

            server.Status = "Shutting Down";
            string finalMsg = (server.CurrentPlayers == 0)
                ? $"Server is empty. Shutting down for {reason} now."
                : $"Final shutdown for {reason}. Goodbye!";

            await SendRconCommandAsync(server, config, $"ServerChat {finalMsg}");
            await SendDiscordMessageAsync(config, server, finalMsg);
            await SendRconCommandAsync(server, config, "DoExit");
            await Task.Delay(15000);
        }

        private static async Task RunSteamCmdUpdateForServerAsync(ServerViewModel server, GlobalConfig config)
        {
            server.Status = "Updating";
            await SendDiscordMessageAsync(config, server, "Server stopped. Beginning update via SteamCMD...");

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
                UseShellExecute = true,
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

        #endregion

        #region Update Checking & Communication

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(string installDir, string appId, string apiUrl)
        {
            var result = new UpdateCheckResult();
            result.InstalledBuild = await GetInstalledBuildIdAsync(installDir, appId);
            string? latestBuild = await GetLatestBuildIdAsync(apiUrl, appId);

            if (string.IsNullOrEmpty(latestBuild) || latestBuild == "0")
            {
                result.LatestBuild = "API Error";
                result.IsUpdateAvailable = false;
            }
            else
            {
                result.LatestBuild = latestBuild;
                result.IsUpdateAvailable = result.InstalledBuild != result.LatestBuild;
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

        private static async Task<string?> GetLatestBuildIdAsync(string apiUrl, string appId)
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

        private static async Task SendRconCommandAsync(ServerViewModel server, GlobalConfig config, string command)
        {
            if (server.Status == "Stopped") return;
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
                Debug.WriteLine($"Failed to send RCON command '{command}' to {server.ServerName}: {ex.Message}");
            }
        }

        private static async Task SendDiscordMessageAsync(GlobalConfig config, ServerViewModel server, string message)
        {
            if (server.DiscordNotificationsEnabled)
            {
                await DiscordNotifier.SendMessageAsync(config.discordWebhookUrl, server.ServerName, message, server.OnlinePlayers);
            }
        }
        #endregion
    }
}