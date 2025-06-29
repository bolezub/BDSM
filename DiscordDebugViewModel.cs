using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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

        public DiscordDebugViewModel(GlobalConfig config)
        {
            _config = config;
            FetchMessagesCommand = new RelayCommand(async _ => await FetchMessagesAsync(), _ => !IsBusy);
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

            // --- IMPROVED ERROR HANDLING ---

            // Step 1: Discover Channel ID from the webhook URL.
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


            // Step 2: Fetch messages using the bot token for authentication.
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