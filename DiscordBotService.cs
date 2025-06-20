using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace BDSM
{
    public static class DiscordBotService
    {
        private static DiscordSocketClient? _client;
        private static CommandService? _commands;
        private static IServiceProvider? _services;
        private static GlobalConfig? _config;

        public static async Task StartAsync(GlobalConfig config, IServiceProvider services)
        {
            _config = config;
            _services = services;

            if (string.IsNullOrWhiteSpace(_config.BotToken))
            {
                Debug.WriteLine("Discord Bot Token is not configured. Bot will not be started.");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers
            });

            _commands = new CommandService();

            _client.Log += Log;
            _client.Ready += OnReady;

            try
            {
                _client.MessageReceived += HandleCommandAsync;
                await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
                await _client.LoginAsync(TokenType.Bot, _config.BotToken);
                await _client.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! Discord Bot failed to start: {ex.Message}");
            }
        }

        private static async Task HandleCommandAsync(SocketMessage messageParam)
        {
            if (!(messageParam is SocketUserMessage message) || message.Author.IsBot) return;
            int argPos = 0;
            if (!(message.HasCharPrefix('!', ref argPos))) return;
            var context = new SocketCommandContext(_client, message);
            await _commands.ExecuteAsync(context: context, argPos: argPos, services: _services);
        }

        private static Task OnReady()
        {
            // FIX: Added null-conditional operator ?. to CurrentUser to prevent crash on startup.
            Debug.WriteLine($"Discord Bot connected successfully as {_client?.CurrentUser?.Username ?? "Unknown User"}");
            return Task.CompletedTask;
        }

        private static Task Log(LogMessage msg)
        {
            // Reverted to the original, correct code.
            Debug.WriteLine($"Discord Bot Log: {msg.ToString()}");
            return Task.CompletedTask;
        }
    }
}