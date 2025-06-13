using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BDSM
{
    public static class DiscordNotifier
    {
        private static readonly HttpClient httpClient = new HttpClient();

        // --- MODIFIED: Method now accepts an optional list of players ---
        public static async Task SendMessageAsync(string webhookUrl, string serverName, string message, List<string>? players = null)
        {
            if (string.IsNullOrEmpty(webhookUrl))
            {
                System.Diagnostics.Debug.WriteLine("Discord webhook URL is not configured. Skipping notification.");
                return;
            }

            try
            {
                // Start building the content
                var contentBuilder = new StringBuilder();
                contentBuilder.Append($"**{serverName}** ```{message}```");

                // If there's a player list, add it to the message
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! FAILED to send Discord notification: {ex.Message}");
            }
        }
    }
}