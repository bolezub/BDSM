using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json; // For saving config

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

        // FIX: The Start method is removed and its logic is merged into InitializeAndStart.
        public static async Task InitializeAndStart(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            // FIX: Set the configuration and view model references first.
            _config = config;
            _appViewModel = appViewModel;

            if (!_config.Watchdog.IsEnabled)
            {
                Debug.WriteLine("Watchdog service is disabled in settings.");
                return;
            }

            // The rest of the startup logic can now execute safely.
            await DeleteOldMessage(isGraphMessage: false);
            await DeleteOldMessage(isGraphMessage: true);

            RestartTimer();
        }

        public static void RestartTimer()
        {
            if (_config == null || !_config.Watchdog.IsEnabled)
            {
                _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                Debug.WriteLine("Watchdog service stopped or disabled.");
                return;
            }

            var interval = TimeSpan.FromSeconds(Math.Max(5, _config.Watchdog.ScanIntervalSeconds));
            NextScanTime = DateTime.Now.Add(interval);

            if (NextGraphPostTime == default || DateTime.Now > NextGraphPostTime)
            {
                NextGraphPostTime = DateTime.Now.AddMinutes(_config.Watchdog.GraphPostIntervalMinutes);
            }

            if (_watchdogTimer == null)
            {
                _watchdogTimer = new Timer(OnTimerTick, null, interval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _watchdogTimer.Change(interval, Timeout.InfiniteTimeSpan);
            }
            Debug.WriteLine($"Watchdog timer restarted. Next scan in: {interval.TotalSeconds} seconds.");
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
                    NextGraphPostTime = DateTime.Now.AddMinutes(_config.Watchdog.GraphPostIntervalMinutes);
                }

                RescheduleTimer();
            });
        }

        private static async Task PostStatusTableAsync()
        {
            if (_appViewModel == null) return;
            var servers = _appViewModel.Clusters
                                       .SelectMany(c => c.Servers)
                                       .Where(s => s.IsActive && s.IsInstalled)
                                       .ToList();
            var statusLines = new List<string>();
            foreach (var server in servers)
            {
                string cpuBar = GetTextBar(server.CpuUsage);
                double ramUsageGB = server.RamUsage;
                double maxRamGB = server.MaxRam > 0 ? server.MaxRam : 35;
                double ramPercent = (ramUsageGB / maxRamGB) * 100;
                string memBar = GetTextBar(ramPercent);
                string players = $"{server.CurrentPlayers}/{server.MaxPlayers}".PadRight(5);
                string pid = server.Pid.PadRight(10);
                string cpu = $"{server.CpuUsage:F1}%".PadLeft(6);
                string mem = $"{server.RamUsage}GB".PadLeft(7);
                statusLines.Add($"{server.ServerName,-15} Players:{players} {pid} CPU:{cpu} [{cpuBar}]  Mem:{mem} [{memBar}]");
            }
            var sb = new StringBuilder();
            sb.AppendLine("```");
            foreach (var line in statusLines)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine("```");
            await UpdateDiscordMessageAsync(sb.ToString(), isGraphMessage: false);
        }

        private static async Task PostGraphsAsync()
        {
            if (_appViewModel == null) return;
            Debug.WriteLine("Starting graph generation and posting...");

            var imagePaths = new List<string>();
            var servers = _appViewModel.Clusters
                                       .SelectMany(c => c.Servers)
                                       .Where(s => s.IsActive && s.IsInstalled)
                                       .ToList();

            foreach (var server in servers)
            {
                var imagePath = await GraphGenerator.CreateGraphImageAsync(server);
                if (!string.IsNullOrWhiteSpace(imagePath))
                {
                    imagePaths.Add(imagePath);
                }
            }

            if (!imagePaths.Any())
            {
                Debug.WriteLine("No graphs generated, skipping Discord post.");
                return;
            }

            await UpdateDiscordMessageAsync("24-Hour Performance Graphs:", isGraphMessage: true, imagePaths: imagePaths);
        }

        private static void RescheduleTimer()
        {
            if (_config == null) return;
            var nextInterval = TimeSpan.FromSeconds(Math.Max(5, _config.Watchdog.ScanIntervalSeconds));
            NextScanTime = DateTime.Now.Add(nextInterval);
            _watchdogTimer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
        }

        private static async Task DeleteOldMessage(bool isGraphMessage)
        {
            string? webhookUrl = _config?.WatchdogDiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            string? messageId = isGraphMessage ? _config.Watchdog.GraphMessageId : _config.Watchdog.TextMessageId;

            if (string.IsNullOrWhiteSpace(messageId)) return;

            try
            {
                var deleteUrl = $"{webhookUrl.TrimEnd('/')}/messages/{messageId}";
                await _httpClient.DeleteAsync(deleteUrl);
                Debug.WriteLine($"Deleted old message {messageId} on startup.");
            }
            catch (Exception ex) { Debug.WriteLine($"Could not delete old message {messageId}: {ex.Message}"); }
            finally
            {
                if (isGraphMessage) _config.Watchdog.GraphMessageId = string.Empty;
                else _config.Watchdog.TextMessageId = string.Empty;
                await SaveConfigAsync();
            }
        }

        private static async Task UpdateDiscordMessageAsync(string content, bool isGraphMessage, List<string>? imagePaths = null)
        {
            string? webhookUrl = _config?.WatchdogDiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            string? messageId = isGraphMessage ? _config.Watchdog.GraphMessageId : _config.Watchdog.TextMessageId;

            if (isGraphMessage && !string.IsNullOrWhiteSpace(messageId))
            {
                await DeleteOldMessage(isGraphMessage: true);
                messageId = null;
            }

            if (!string.IsNullOrWhiteSpace(messageId) && !isGraphMessage)
            {
                var payload = new { content };
                try
                {
                    var patchUrl = $"{webhookUrl.TrimEnd('/')}/messages/{messageId}";
                    var response = await _httpClient.PatchAsJsonAsync(patchUrl, payload);
                    if (response.IsSuccessStatusCode) return;
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        if (isGraphMessage) _config.Watchdog.GraphMessageId = string.Empty;
                        else _config.Watchdog.TextMessageId = string.Empty;
                    }
                    else return;
                }
                catch (Exception ex) { Debug.WriteLine($"Error patching Discord message: {ex.Message}"); return; }
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
                        var fileBytes = await File.ReadAllBytesAsync(filePath);
                        multipartContent.Add(new ByteArrayContent(fileBytes), $"file{i}", Path.GetFileName(filePath));
                    }
                }

                var postUrl = $"{webhookUrl}?wait=true";
                var response = await _httpClient.PostAsync(postUrl, multipartContent);

                if (response.IsSuccessStatusCode)
                {
                    var messageResponse = await response.Content.ReadFromJsonAsync<DiscordMessageResponse>();
                    if (messageResponse?.Id != null)
                    {
                        if (isGraphMessage) _config.Watchdog.GraphMessageId = messageResponse.Id;
                        else _config.Watchdog.TextMessageId = messageResponse.Id;
                        await SaveConfigAsync();
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Error posting new Discord message: {ex.Message}"); }
        }

        private static async Task SaveConfigAsync()
        {
            if (_config == null) return;
            try
            {
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                await File.WriteAllTextAsync("config.json", json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save config from WatchdogService: {ex.Message}");
            }
        }

        private static string GetTextBar(double percent, int width = 15)
        {
            int p = (int)Math.Min(100, Math.Max(0, percent));
            int filled = (int)Math.Round(p * width / 100.0);
            int empty = width - filled;
            return new string('#', filled) + new string('-', empty);
        }

        private class DiscordMessageResponse
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }
        }
    }
}