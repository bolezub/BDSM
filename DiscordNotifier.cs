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

        private static readonly SemaphoreSlim _discordSemaphore = new SemaphoreSlim(1, 1);

        public static async Task SendMessageAsync(string webhookUrl, string serverName, string message, List<string>? players = null)
        {
            if (string.IsNullOrEmpty(webhookUrl))
            {
                return;
            }

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
                    // Error logging removed
                }

                await Task.Delay(1100);
            }
            catch (Exception)
            {
                // Error logging removed
            }
            finally
            {
                _discordSemaphore.Release();
            }
        }
    }
}