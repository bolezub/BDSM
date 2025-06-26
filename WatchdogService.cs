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
            }
            catch (Exception) { }
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
                catch (Exception) { return; }
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
            catch (Exception) { }
        }

        private static async Task SaveConfigAsync()
        {
            if (_config == null) return;
            try
            {
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                await File.WriteAllTextAsync("config.json", json);
            }
            catch (Exception)
            {
                // Error logging removed
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