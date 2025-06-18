using System;
using System.Diagnostics;
using System.Reflection; // Required for command handling
using System.Threading.Tasks;
using Discord;
using Discord.Commands; // Required for command handling
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
                // Subscribe to the MessageReceived event to handle commands
                _client.MessageReceived += HandleCommandAsync;

                // Discover all of the commands in this assembly and load them.
                await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

                await _client.LoginAsync(TokenType.Bot, _config.BotToken);
                await _client.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! Discord Bot failed to start: {ex.Message}");
            }
        }

        // NEW METHOD: This is the core command handler
        private static async Task HandleCommandAsync(SocketMessage messageParam)
        {
            // Don't process the command if it was a system message or from a bot
            if (!(messageParam is SocketUserMessage message) || message.Author.IsBot) return;

            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;

            // Determine if the message is a command based on the prefix '!'
            if (!(message.HasCharPrefix('!', ref argPos))) return;

            // Create a WebSocket-based command context based on the message
            var context = new SocketCommandContext(_client, message);

            // Execute the command with the command context we just created
            await _commands.ExecuteAsync(
                context: context,
                argPos: argPos,
                services: _services);
        }

        private static Task OnReady()
        {
            Debug.WriteLine($"Discord Bot connected successfully as {_client?.CurrentUser.Username}");
            return Task.CompletedTask;
        }

        private static Task Log(LogMessage msg)
        {
            Debug.WriteLine($"Discord Bot Log: {msg.ToString()}");
            return Task.CompletedTask;
        }
    }
}