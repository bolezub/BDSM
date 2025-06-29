using System;
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

        public static async Task StartAsync(GlobalConfig config, IServiceProvider services)
        {
            _services = services;

            if (string.IsNullOrWhiteSpace(config.BotToken)) return;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildMembers
            });

            _commands = new CommandService();

            _client.Log += Log;

            try
            {
                await _client.LoginAsync(TokenType.Bot, config.BotToken);
                await _client.StartAsync();
                _client.MessageReceived += HandleCommandAsync;
                await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Discord Bot Error: {ex.Message}", LogLevel.Error);
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

        private static Task Log(LogMessage msg)
        {
            // You can add logging here if you wish, but for now it's clean.
            return Task.CompletedTask;
        }
    }
}