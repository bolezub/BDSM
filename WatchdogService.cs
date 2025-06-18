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

namespace BDSM
{
    public static class WatchdogService
    {
        private static Timer? _watchdogTimer;
        private static GlobalConfig? _config;
        private static ApplicationViewModel? _appViewModel;

        private static readonly HttpClient _httpClient = new();
        private static string? _textMessageId;
        private static readonly string _textMessageIdFilePath = Path.Combine(AppContext.BaseDirectory, "watchdog_text_message_id.txt");

        private static string? _graphMessageId;
        private static readonly string _graphMessageIdFilePath = Path.Combine(AppContext.BaseDirectory, "watchdog_graph_message_id.txt");

        public static DateTime NextScanTime { get; private set; }
        public static DateTime NextGraphPostTime { get; private set; }

        public static void Start(GlobalConfig config, ApplicationViewModel appViewModel)
        {
            _config = config;
            _appViewModel = appViewModel;

            if (!_config.Watchdog.IsEnabled)
            {
                Debug.WriteLine("Watchdog service is disabled in settings.");
                return;
            }

            // This is now handled by RestartTimer, which is called after this method
        }

        public static async Task InitializeAndStart()
        {
            if (_config == null) return;

            // NEW: Delete old messages on startup for a clean slate
            await DeleteOldMessage(isGraphMessage: false);
            await DeleteOldMessage(isGraphMessage: true);

            RestartTimer();
        }

        // NEW METHOD: Restarts the timer with current config values
        public static void RestartTimer()
        {
            if (_config == null || !_config.Watchdog.IsEnabled)
            {
                _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer if disabled
                Debug.WriteLine("Watchdog service stopped or disabled.");
                return;
            }

            var interval = TimeSpan.FromSeconds(Math.Max(5, _config.Watchdog.ScanIntervalSeconds));
            NextScanTime = DateTime.Now.Add(interval);
            NextGraphPostTime = DateTime.Now.AddMinutes(_config.Watchdog.GraphPostIntervalMinutes);

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
                double maxRamGB = server.MaxRam > 0 ? server.MaxRam : 1;
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

            string idFilePath = isGraphMessage ? _graphMessageIdFilePath : _textMessageIdFilePath;
            string? messageId = isGraphMessage ? _graphMessageId : _textMessageId;

            if (File.Exists(idFilePath))
            {
                messageId = await File.ReadAllTextAsync(idFilePath);
            }

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
                // Clear the state regardless of success
                if (isGraphMessage) _graphMessageId = null;
                else _textMessageId = null;
                File.Delete(idFilePath);
            }
        }

        private static async Task UpdateDiscordMessageAsync(string content, bool isGraphMessage, List<string>? imagePaths = null)
        {
            string? webhookUrl = _config?.WatchdogDiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            string? messageId = isGraphMessage ? _graphMessageId : _textMessageId;
            string idFilePath = isGraphMessage ? _graphMessageIdFilePath : _textMessageIdFilePath;

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
                        messageId = null;
                        File.Delete(idFilePath);
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
                        if (isGraphMessage) _graphMessageId = messageResponse.Id;
                        else _textMessageId = messageResponse.Id;
                        await File.WriteAllTextAsync(idFilePath, messageResponse.Id);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Error posting new Discord message: {ex.Message}"); }
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