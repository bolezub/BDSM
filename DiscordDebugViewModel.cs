using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BDSM
{
    // A small record to help deserialize the JSON response from Discord
    public record DiscordMessage([property: JsonPropertyName("id")] string Id);

    public class DiscordDebugViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private string _statusText = "Ready.";
        private bool _isBusy = false;

        public ObservableCollection<string> MessageIds { get; } = new ObservableCollection<string>();

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public ICommand FetchMessagesCommand { get; }
        // --- NEW COMMAND ---
        public ICommand DeleteMessagesCommand { get; }

        public DiscordDebugViewModel(GlobalConfig config)
        {
            _config = config;
            FetchMessagesCommand = new RelayCommand(async _ => await FetchMessagesAsync(), _ => !IsBusy);
            // --- NEW COMMAND INITIALIZATION ---
            DeleteMessagesCommand = new RelayCommand(async _ => await DeleteMessagesAsync(), _ => !IsBusy && MessageIds.Any());
        }

        // --- NEW METHOD FOR DELETING MESSAGES ---
        private async Task DeleteMessagesAsync()
        {
            var result = MessageBox.Show(
                $"This will permanently delete the {MessageIds.Count} fetched messages from the channel. This action cannot be undone.\n\nAre you sure you want to continue?",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                StatusText = "Deletion cancelled.";
                return;
            }

            IsBusy = true;
            string channelId = string.Empty; // We need to get the channel ID again

            // Step 1: We still need the channel ID to build the correct DELETE URL
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var webhookInfo = await httpClient.GetFromJsonAsync<WebhookInfoResponse>(_config.WatchdogDiscordWebhookUrl);
                    if (webhookInfo == null || string.IsNullOrWhiteSpace(webhookInfo.ChannelId))
                    {
                        StatusText = "Error: Could not discover Channel ID from webhook.";
                        IsBusy = false;
                        return;
                    }
                    channelId = webhookInfo.ChannelId;
                }
            }
            catch (HttpRequestException ex)
            {
                StatusText = $"Error: Could not get channel ID. Details: {ex.StatusCode} - {ex.Message}";
                IsBusy = false;
                return;
            }

            // Step 2: Loop and delete messages one by one with a delay
            int deletedCount = 0;
            int totalCount = MessageIds.Count;
            var messageIdsToDelete = new List<string>(MessageIds); // Create a copy to iterate over

            try
            {
                using (var authClient = new HttpClient())
                {
                    authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _config.BotToken);

                    foreach (var messageId in messageIdsToDelete)
                    {
                        StatusText = $"Deleting message {deletedCount + 1} of {totalCount}... (ID: {messageId})";
                        var deleteUrl = $"https://discord.com/api/v9/channels/{channelId}/messages/{messageId}";
                        var response = await authClient.DeleteAsync(deleteUrl);

                        if (response.IsSuccessStatusCode)
                        {
                            deletedCount++;
                        }
                        // We don't stop on error, maybe the message was already deleted manually.

                        // CRITICAL: Wait to avoid API rate limits.
                        await Task.Delay(1100);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"An unexpected error occurred during deletion: {ex.Message}";
                LoggingService.Log($"Discord Debug Tool Error (Deletion): {ex}", LogLevel.Error);
                IsBusy = false;
                return;
            }

            StatusText = $"Deletion complete. Successfully deleted {deletedCount} of {totalCount} messages.";
            MessageIds.Clear();
            IsBusy = false;
        }

        private async Task FetchMessagesAsync()
        {
            IsBusy = true;
            MessageIds.Clear();
            StatusText = "Fetching messages...";

            if (string.IsNullOrWhiteSpace(_config.WatchdogDiscordWebhookUrl) || string.IsNullOrWhiteSpace(_config.BotToken))
            {
                StatusText = "Error: Watchdog Webhook URL or Bot Token is not configured in Global Settings.";
                IsBusy = false;
                return;
            }

            string channelId = string.Empty;
            try
            {
                StatusText = "Step 1: Discovering Channel ID from Webhook URL...";
                using (var httpClient = new HttpClient())
                {
                    var webhookInfo = await httpClient.GetFromJsonAsync<WebhookInfoResponse>(_config.WatchdogDiscordWebhookUrl);
                    if (webhookInfo == null || string.IsNullOrWhiteSpace(webhookInfo.ChannelId))
                    {
                        StatusText = "Error: Could not discover Channel ID from webhook. The response was empty.";
                        IsBusy = false;
                        return;
                    }
                    channelId = webhookInfo.ChannelId;
                }
            }
            catch (HttpRequestException ex)
            {
                StatusText = $"Error on Step 1 (Webhook): Could not connect. Check the Watchdog Webhook URL in your config. Details: {ex.StatusCode} - {ex.Message}";
                LoggingService.Log($"Discord Debug Tool Error (Webhook Fetch): {ex}", LogLevel.Error);
                IsBusy = false;
                return;
            }

            try
            {
                StatusText = $"Step 2: Found Channel ID '{channelId}'. Fetching messages as bot...";
                using (var authClient = new HttpClient())
                {
                    authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _config.BotToken);
                    var messages = await authClient.GetFromJsonAsync<List<DiscordMessage>>($"https://discord.com/api/v9/channels/{channelId}/messages?limit=100");

                    if (messages != null && messages.Any())
                    {
                        foreach (var message in messages)
                        {
                            MessageIds.Add(message.Id);
                        }
                        StatusText = $"Successfully fetched {messages.Count} message IDs from channel {channelId}.";
                    }
                    else
                    {
                        StatusText = "No messages found in the channel.";
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                StatusText = $"Error on Step 2 (Bot): Could not fetch messages. Check the Bot Token and ensure the bot has 'Read Message History' permission on the channel. Details: {ex.StatusCode} - {ex.Message}";
                LoggingService.Log($"Discord Debug Tool Error (Message Fetch): {ex}", LogLevel.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private record WebhookInfoResponse([property: JsonPropertyName("channel_id")] string ChannelId);
    }
}