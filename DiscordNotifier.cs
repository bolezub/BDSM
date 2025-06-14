using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BDSM
{
    public static class DiscordNotifier
    {
        private static readonly HttpClient httpClient = new HttpClient();

        // --- NEW: Semaphore to ensure only one message is sent at a time ---
        private static readonly SemaphoreSlim _discordSemaphore = new SemaphoreSlim(1, 1);

        public static async Task SendMessageAsync(string webhookUrl, string serverName, string message, List<string>? players = null)
        {
            if (string.IsNullOrEmpty(webhookUrl))
            {
                System.Diagnostics.Debug.WriteLine("Discord webhook URL is not configured. Skipping notification.");
                return;
            }

            // Wait for the semaphore. If another message is being sent, this will pause here.
            await _discordSemaphore.WaitAsync();
            try
            {
                var contentBuilder = new StringBuilder();
                contentBuilder.Append($"**{serverName}** ```{message}```");

                if (players != null && players.Any())
                {
                    contentBuilder.Append($"\nPlayers online:\n```{string.Join("\n", players)}```");
                }

                var payload = new
                {
                    content = contentBuilder.ToString()
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(webhookUrl, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Error sending Discord notification: {response.StatusCode}");
                }

                // --- NEW: Wait for 1.1 seconds after sending a message to avoid rate limits ---
                // This mimics the 'Start-Sleep -Seconds 1' from your PowerShell script.
                await Task.Delay(1100);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! FAILED to send Discord notification: {ex.Message}");
            }
            finally
            {
                // Release the semaphore so the next message in the queue can be sent.
                _discordSemaphore.Release();
            }
        }
    }
}