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

        public BotCommands(ApplicationViewModel appViewModel)
        {
            _appViewModel = appViewModel;
        }

        [Command("ping")]
        [Summary("A simple test command to check if the bot is responding.")]
        public async Task PingAsync()
        {
            await ReplyAsync("Pong!");
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

        // --- NEW INFORMATIONAL COMMAND ---

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

        // --- NEW CHAT COMMAND ---

        [Command("say")]
        [Summary("Sends a chat message to a specific server.")]
        [RequireUserPermission(GuildPermission.Administrator)] // SECURITY: Only admins can use this
        public async Task SayAsync(string serverName, [Remainder] string message)
        {
            // Note: For server names with spaces, users will need to put them in quotes.
            // Example: !say "The Island" Hello everyone
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


        // --- SINGLE SERVER COMMANDS ---

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

        // --- CLUSTER-WIDE COMMANDS ---

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

        // --- HELPER METHODS ---

        private ServerViewModel? FindServer(string serverName)
        {
            return _appViewModel.Clusters
                .SelectMany(c => c.Servers)
                .FirstOrDefault(s => s.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase));
        }

        private ClusterViewModel? FindCluster(string clusterName)
        {
            return _appViewModel.Clusters
                .FirstOrDefault(c => c.Name.Equals(clusterName, StringComparison.OrdinalIgnoreCase));
        }
    }
}