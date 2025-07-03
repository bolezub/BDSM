using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BDSM
{
    public static class WatchdogService
    {
        private static Timer? _watchdogTimer;
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;
        private static readonly HttpClient _httpClient = new();

        public static DateTime NextScanTime { get; private set; }
        public static DateTime NextGraphPostTime { get; private set; }

        public static async Task InitializeAndStart(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;

            if (!_config.Watchdog.IsEnabled)
            {
                return;
            }

            await DeleteOldMessage(isGraphMessage: false);
            await DeleteOldMessage(isGraphMessage: true);

            RestartTimer();
        }

        // --- THIS METHOD HAS BEEN CORRECTED ---
        /// <summary>
        /// Posts a persistent message to the watchdog channel, used for maintenance or update notifications.
        /// </summary>
        public static async Task DisplayMaintenanceMessageAsync(string message)
        {
            if (_config == null || !_config.Watchdog.IsEnabled) return;

            string content = "```" +
                             $"\n{message}\n\nPlease wait until the operation is complete.\n" +
                             "```";

            // This will edit the existing message or post a new one.
            await UpdateDiscordMessageAsync(content, isGraphMessage: false);
        }

        public static void RestartTimer()
        {
            if (_config == null || !_config.Watchdog.IsEnabled)
            {
                _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var interval = TimeSpan.FromSeconds(Math.Max(5, _config.Watchdog.ScanIntervalSeconds));
            NextScanTime = DateTime.Now.Add(interval);

            if (NextGraphPostTime == default || DateTime.Now > NextGraphPostTime)
            {
                NextGraphPostTime = DateTime.Now.AddMinutes(Math.Max(1, _config.Watchdog.GraphPostIntervalMinutes));
            }

            if (_watchdogTimer == null)
            {
                _watchdogTimer = new Timer(OnTimerTick, null, interval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _watchdogTimer.Change(interval, Timeout.InfiniteTimeSpan);
            }
        }

        private static void OnTimerTick(object? state)
        {
            _ = Task.Run(async () =>
            {
                if (_config == null || _appViewModel == null || TaskSchedulerService.IsMajorOperationInProgress)
                {
                    RescheduleTimer();
                    return;
                }
                await PostStatusTableAsync();
                if (DateTime.Now >= NextGraphPostTime)
                {
                    await PostGraphsAsync();
                }
                RescheduleTimer();
            });
        }

        private static string GetStatusEmoji(string status)
        {
            return status switch
            {
                "Running" => "🟢",
                "Starting" => "🟡",
                "Stopped" => "🔴",
                "Error" => "🔴",
                "Stopping" => "🟠",
                "Update Pending" => "🟠",
                "Shutting Down" => "🟠",
                "Updating" => "🔵",
                "Not Installed" => "⚪",
                _ => "⚫",
            };
        }

        private static async Task PostStatusTableAsync()
        {
            if (_appViewModel == null || _config == null) return;
            var servers = _appViewModel.Clusters.SelectMany(c => c.Servers).Where(s => s.IsActive && s.IsInstalled).ToList();
            var statusLines = new List<string>();
            foreach (var server in servers)
            {
                string statusEmoji = GetStatusEmoji(server.Status);
                string cpuBar = GetTextBar(server.CpuUsage);
                double ramUsageGB = server.RamUsage;
                double maxRamGB = server.MaxRam > 0 ? server.MaxRam : 35;
                double ramPercent = maxRamGB > 0 ? (ramUsageGB / maxRamGB) * 100 : 0;
                string memBar = GetTextBar(ramPercent);

                string players = $"P:{server.CurrentPlayers}/{server.MaxPlayers}".PadRight(8);
                string cpu = $"{server.CpuUsage:F1}%".PadLeft(6);
                string mem = $"{server.RamUsage}GB".PadLeft(7);
                statusLines.Add($"{statusEmoji} {server.ServerName,-15} {players} CPU:{cpu}[{cpuBar}] Mem:{mem}[{memBar}]");
            }
            var sb = new StringBuilder();
            sb.AppendLine("```");
            foreach (var line in statusLines) sb.AppendLine(line);
            sb.AppendLine("```");

            sb.AppendLine("```");
            foreach (var server in servers)
            {
                string playerListString;
                if (server.OnlinePlayers.Any())
                {
                    playerListString = string.Join(", ", server.OnlinePlayers);
                }
                else
                {
                    playerListString = "no players connected";
                }
                sb.AppendLine($"{server.ServerName}: {playerListString}");
            }
            sb.AppendLine("```");

            await UpdateDiscordMessageAsync(sb.ToString(), isGraphMessage: false);
        }

        private static async Task PostGraphsAsync()
        {
            if (_appViewModel == null) return;
            var imagePaths = new List<string>();
            var servers = _appViewModel.Clusters.SelectMany(c => c.Servers).Where(s => s.IsActive && s.IsInstalled).ToList();
            foreach (var server in servers)
            {
                var imagePath = await GraphGenerator.CreateGraphImageAsync(server);
                if (!string.IsNullOrWhiteSpace(imagePath)) imagePaths.Add(imagePath);
            }
            if (!imagePaths.Any()) return;
            await UpdateDiscordMessageAsync("24-Hour Performance Graphs:", isGraphMessage: true, imagePaths: imagePaths);
        }

        private static async Task UpdateDiscordMessageAsync(string content, bool isGraphMessage, List<string>? imagePaths = null)
        {
            if (_config == null) return;
            string? webhookUrl = _config.WatchdogDiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;
            string? messageId = isGraphMessage ? _config.Watchdog.GraphMessageId : _config.Watchdog.TextMessageId;
            if (isGraphMessage && !string.IsNullOrWhiteSpace(messageId))
            {
                await DeleteOldMessage(isGraphMessage: true);
                messageId = null;
            }
            if (!string.IsNullOrWhiteSpace(messageId) && !isGraphMessage)
            {
                try
                {
                    var response = await _httpClient.PatchAsJsonAsync($"{webhookUrl.TrimEnd('/')}/messages/{messageId}", new { content });
                    if (response.IsSuccessStatusCode) return;
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _config.Watchdog.TextMessageId = string.Empty;
                        await SaveConfigAsync();
                        messageId = null;
                    }
                    else return;
                }
                catch (Exception ex) { LoggingService.Log($"Failed to edit webhook message {messageId}: {ex.Message}", LogLevel.Error); return; }
            }
            try
            {
                using var multipartContent = new MultipartFormDataContent();
                multipartContent.Add(new StringContent(content), "content");
                if (imagePaths != null)
                {
                    for (int i = 0; i < imagePaths.Count; i++)
                    {
                        var filePath = imagePaths[i];
                        if (!File.Exists(filePath)) continue;
                        var fileBytes = await File.ReadAllBytesAsync(filePath);
                        multipartContent.Add(new ByteArrayContent(fileBytes), $"file{i}", Path.GetFileName(filePath));
                    }
                }
                var postUrl = $"{webhookUrl}?wait=true";
                var response = await _httpClient.PostAsync(postUrl, multipartContent);
                if (response.IsSuccessStatusCode)
                {
                    var messageResponse = await response.Content.ReadFromJsonAsync<DiscordMessageResponse>();
                    if (messageResponse?.Id == null) return;
                    if (isGraphMessage) _config.Watchdog.GraphMessageId = messageResponse.Id;
                    else _config.Watchdog.TextMessageId = messageResponse.Id;
                    await SaveConfigAsync();
                }
            }
            catch (Exception ex) { LoggingService.Log($"Failed to post new webhook message: {ex.Message}", LogLevel.Error); }
        }

        private static async Task DeleteOldMessage(bool isGraphMessage)
        {
            if (_config == null) return;
            string? webhookUrl = _config.WatchdogDiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;
            string? messageId = isGraphMessage ? _config.Watchdog.GraphMessageId : _config.Watchdog.TextMessageId;
            if (string.IsNullOrWhiteSpace(messageId)) return;
            try { await _httpClient.DeleteAsync($"{webhookUrl.TrimEnd('/')}/messages/{messageId}"); }
            catch (Exception ex) { LoggingService.Log($"Failed to delete old webhook message {messageId}: {ex.Message}", LogLevel.Warning); }
        }

        private static async Task SaveConfigAsync()
        {
            if (_config == null) return;
            try { await File.WriteAllTextAsync("config.json", Newtonsoft.Json.JsonConvert.SerializeObject(_config, Formatting.Indented)); }
            catch (Exception ex) { LoggingService.Log($"Failed to save config from WatchdogService: {ex.Message}", LogLevel.Error); }
        }

        private static void RescheduleTimer()
        {
            if (_config == null) return;
            var nextInterval = TimeSpan.FromSeconds(Math.Max(5, _config.Watchdog.ScanIntervalSeconds));
            NextScanTime = DateTime.Now.Add(nextInterval);
            _watchdogTimer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
        }

        private static string GetTextBar(double percent, int width = 8)
        {
            int p = (int)Math.Min(100, Math.Max(0, percent));
            int filled = (int)Math.Round(p * width / 100.0);
            return new string('█', filled) + new string('─', width - filled);
        }
        private class DiscordMessageResponse { [JsonPropertyName("id")] public string? Id { get; set; } }
    }
}