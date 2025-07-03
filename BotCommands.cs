using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace BDSM
{
    public class BotCommands : ModuleBase<SocketCommandContext>
    {
        private readonly ApplicationViewModel _appViewModel;
        private readonly CommandService _commandService;

        public BotCommands(ApplicationViewModel appViewModel, CommandService commandService)
        {
            _appViewModel = appViewModel;
            _commandService = commandService;
        }

        [Command("help")]
        [Summary("Lists all available commands or shows info about a specific command.")]
        public async Task HelpAsync([Remainder] string commandName = "")
        {
            var embedBuilder = new EmbedBuilder()
                .WithColor(new Color(0x7289DA))
                .WithTitle("BDSM Bot Help");

            if (string.IsNullOrWhiteSpace(commandName))
            {
                embedBuilder.WithDescription($"Here are all the commands you can use. For more info on a specific command, type `{DiscordBotService.BotPrefix}help <command_name>`.");

                foreach (var module in _commandService.Modules)
                {
                    var commandList = new StringBuilder();
                    foreach (var cmd in module.Commands)
                    {
                        if (cmd.Name.Equals("help", StringComparison.OrdinalIgnoreCase)) continue;

                        commandList.AppendLine($"**`{DiscordBotService.BotPrefix}{cmd.Name}`**: {cmd.Summary ?? "No description"}");
                    }

                    if (commandList.Length > 0)
                    {
                        embedBuilder.AddField(module.Name, commandList.ToString());
                    }
                }
            }
            else
            {
                var result = _commandService.Search(Context, commandName);

                if (!result.IsSuccess)
                {
                    await ReplyAsync($"Sorry, I couldn't find a command like **{commandName}**.");
                    return;
                }

                embedBuilder.WithDescription($"Here's some info about the **`{DiscordBotService.BotPrefix}{commandName}`** command:");

                foreach (var match in result.Commands)
                {
                    var cmd = match.Command;

                    var usage = new StringBuilder();
                    usage.Append($"{DiscordBotService.BotPrefix}{cmd.Name}");
                    foreach (var param in cmd.Parameters)
                    {
                        usage.Append($" <{param.Name}>");
                    }

                    embedBuilder.AddField("Usage", $"`{usage}`");
                    embedBuilder.AddField("Description", $"{cmd.Summary ?? "No description"}");
                }
            }

            await ReplyAsync(embed: embedBuilder.Build());
        }


        [Command("ping")]
        [Summary("A simple test command to check if the bot is responding.")]
        public async Task PingAsync()
        {
            await ReplyAsync("Pong!");
        }

        // --- NEW COMMAND ---
        [Command("clusters")]
        [Summary("Lists all available server clusters.")]
        public async Task ListClustersAsync()
        {
            var clusters = _appViewModel.Clusters.ToList();

            if (!clusters.Any())
            {
                await ReplyAsync("No clusters have been configured.");
                return;
            }

            var embedBuilder = new EmbedBuilder()
                .WithTitle("Available Server Clusters")
                .WithColor(new Color(0x58D68D))
                .WithDescription(string.Join("\n", clusters.Select(c => $"- {c.Name}")));

            await ReplyAsync(embed: embedBuilder.Build());
        }

        [Command("status")]
        [Summary("Displays the current status of all active servers.")]
        public async Task StatusAsync()
        {
            var servers = _appViewModel.Clusters
                                       .SelectMany(c => c.Servers)
                                       .Where(s => s.IsActive && s.IsInstalled)
                                       .ToList();

            if (!servers.Any())
            {
                await ReplyAsync("No active servers are currently being monitored.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("```");
            foreach (var server in servers)
            {
                string players = $"{server.CurrentPlayers}/{server.MaxPlayers}".PadRight(5);
                string pid = server.Pid.PadRight(10);
                string status = server.Status.PadRight(12);

                sb.AppendLine($"{server.ServerName,-15} Status:{status} Players:{players} {pid}");
            }
            sb.AppendLine("```");

            await ReplyAsync(sb.ToString());
        }

        [Command("players")]
        [Summary("Lists the online players for a specific server.")]
        public async Task ListPlayersAsync([Remainder] string serverName)
        {
            var server = FindServer(serverName);
            if (server == null)
            {
                await ReplyAsync($"Sorry, I could not find a server named '{serverName}'.");
                return;
            }

            if (server.Status != "Running" && server.Status != "Update Pending")
            {
                await ReplyAsync($"Server **{server.ServerName}** is not currently running.");
                return;
            }

            if (server.OnlinePlayers.Any())
            {
                var playerList = string.Join("\n", server.OnlinePlayers);
                await ReplyAsync($"**Players online on {server.ServerName}:**\n```\n{playerList}\n```");
            }
            else
            {
                await ReplyAsync($"There are no players currently online on **{server.ServerName}**.");
            }
        }

        [Command("say")]
        [Summary("Sends a chat message to a specific server.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SayAsync(string serverName, [Remainder] string message)
        {
            var server = FindServer(serverName);
            if (server == null)
            {
                await ReplyAsync($"Sorry, I could not find a server named '{serverName}'.");
                return;
            }

            if (server.Status != "Running")
            {
                await ReplyAsync($"Server **{server.ServerName}** is not running, cannot send message.");
                return;
            }

            string rconCommand = $"ServerChat {message}";
            await server.SendRconCommandAsync(rconCommand);
            await ReplyAsync($"Message sent to **{server.ServerName}**.");
        }

        [Command("start")]
        [Summary("Starts a specific server.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task StartServerAsync([Remainder] string serverName)
        {
            var server = FindServer(serverName);
            if (server == null)
            {
                await ReplyAsync($"Sorry, I could not find a server named '{serverName}'.");
                return;
            }

            if (server.StartServerCommand.CanExecute(null))
            {
                server.StartServerCommand.Execute(null);
                await ReplyAsync($"Attempting to start server: **{server.ServerName}**");
            }
            else
            {
                await ReplyAsync($"Could not start server **{server.ServerName}**. It may already be running or another operation is in progress.");
            }
        }

        [Command("stop")]
        [Summary("Gracefully stops a specific server.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task StopServerAsync([Remainder] string serverName)
        {
            var server = FindServer(serverName);
            if (server == null)
            {
                await ReplyAsync($"Sorry, I could not find a server named '{serverName}'.");
                return;
            }

            if (server.StopServerCommand.CanExecute(null))
            {
                server.StopServerCommand.Execute(null);
                await ReplyAsync($"Initiating graceful shutdown for: **{server.ServerName}**");
            }
            else
            {
                await ReplyAsync($"Could not stop server **{server.ServerName}**. It may already be stopped or another operation is in progress.");
            }
        }

        [Command("restart")]
        [Summary("Gracefully restarts a specific server.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task RestartServerAsync([Remainder] string serverName)
        {
            var server = FindServer(serverName);
            if (server == null)
            {
                await ReplyAsync($"Sorry, I could not find a server named '{serverName}'.");
                return;
            }

            if (server.RestartServerCommand.CanExecute(null))
            {
                server.RestartServerCommand.Execute(null);
                await ReplyAsync($"Initiating graceful restart for: **{server.ServerName}**");
            }
            else
            {
                await ReplyAsync($"Could not restart server **{server.ServerName}**. It may not be running or another operation is in progress.");
            }
        }

        [Command("startall")]
        [Summary("Starts all active servers in a specific cluster.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task StartAllAsync([Remainder] string clusterName)
        {
            var cluster = FindCluster(clusterName);
            if (cluster == null)
            {
                await ReplyAsync($"Sorry, I could not find a cluster named '{clusterName}'.");
                return;
            }

            if (cluster.StartAllCommand.CanExecute(null))
            {
                cluster.StartAllCommand.Execute(null);
                await ReplyAsync($"Attempting to start all active servers in cluster: **{cluster.Name}**");
            }
            else
            {
                await ReplyAsync($"Could not start all servers in **{cluster.Name}**. An operation may already be in progress.");
            }
        }

        [Command("stopall")]
        [Summary("Gracefully stops all active servers in a specific cluster.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task StopAllAsync([Remainder] string clusterName)
        {
            var cluster = FindCluster(clusterName);
            if (cluster == null)
            {
                await ReplyAsync($"Sorry, I could not find a cluster named '{clusterName}'.");
                return;
            }

            if (cluster.StopAllCommand.CanExecute(null))
            {
                cluster.StopAllCommand.Execute(null);
                await ReplyAsync($"Initiating graceful shutdown for all active servers in cluster: **{cluster.Name}**");
            }
            else
            {
                await ReplyAsync($"Could not stop all servers in **{cluster.Name}**. An operation may already be in progress.");
            }
        }

        [Command("restartall")]
        [Summary("Gracefully restarts all active servers in a specific cluster.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task RestartAllAsync([Remainder] string clusterName)
        {
            var cluster = FindCluster(clusterName);
            if (cluster == null)
            {
                await ReplyAsync($"Sorry, I could not find a cluster named '{clusterName}'.");
                return;
            }

            if (cluster.RestartAllCommand.CanExecute(null))
            {
                cluster.RestartAllCommand.Execute(null);
                await ReplyAsync($"Initiating graceful restart for all active servers in cluster: **{cluster.Name}**");
            }
            else
            {
                await ReplyAsync($"Could not restart all servers in **{cluster.Name}**. An operation may already be in progress.");
            }
        }

        [Command("messageall")]
        [Summary("Sends a chat message to all active, running servers in a cluster.")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task MessageAllAsync(string clusterName, [Remainder] string message)
        {
            var cluster = FindCluster(clusterName);
            if (cluster == null)
            {
                await ReplyAsync($"Sorry, I could not find a cluster named '{clusterName}'.");
                return;
            }

            var activeRunningServers = cluster.Servers.Where(s => s.IsActive && s.Status == "Running");
            await ServerOperationManager.MessageAllAsync(activeRunningServers, message);

            await ReplyAsync($"Message sent to all active, running servers in cluster: **{cluster.Name}**");
        }

        private ServerViewModel? FindServer(string serverName)
        {
            return _appViewModel.Clusters
                .SelectMany(c => c.Servers)
                .FirstOrDefault(s => s.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase) ||
                                     s.Aliases.Contains(serverName, StringComparer.OrdinalIgnoreCase));
        }

        private ClusterViewModel? FindCluster(string clusterName)
        {
            return _appViewModel.Clusters
                .FirstOrDefault(c => c.Name.Equals(clusterName, StringComparison.OrdinalIgnoreCase));
        }
    }
}