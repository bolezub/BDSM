using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BDSM
{
    public static class ServerOperationManager
    {
        /// <summary>
        /// Sends the "SaveWorld" RCON command to all running servers in the provided list.
        /// </summary>
        public static async Task SaveAllAsync(IEnumerable<ServerViewModel> servers, GlobalConfig config)
        {
            var runningServers = servers.Where(s => s.Status == "Running").ToList();
            var saveTasks = runningServers.Select(server => server.SendRconCommandAsync("SaveWorld")).ToList();
            await Task.WhenAll(saveTasks);
        }

        /// <summary>
        /// Starts all stopped servers in the provided list.
        /// </summary>
        public static void StartAll(IEnumerable<ServerViewModel> servers)
        {
            var stoppedServers = servers.Where(s => s.Status == "Stopped").ToList();
            foreach (var server in stoppedServers)
            {
                server.StartServer();
            }
        }

        /// <summary>
        /// Stops all running servers in the provided list using a graceful shutdown command.
        /// </summary>
        public static async Task StopAllAsync(IEnumerable<ServerViewModel> servers)
        {
            var runningServers = servers.Where(s => s.Status == "Running").ToList();
            var stopTasks = runningServers.Select(server => server.EmergencyStopServer()).ToList();
            await Task.WhenAll(stopTasks);
        }

        /// <summary>
        /// Sends a "ServerChat" RCON command to all running servers in the provided list.
        /// </summary>

        public static async Task MessageAllAsync(IEnumerable<ServerViewModel> servers, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            var runningServers = servers.Where(s => s.Status == "Running").ToList();
            var messageTasks = runningServers.Select(server => server.SendRconCommandAsync($"ServerChat {message}")).ToList();
            await Task.WhenAll(messageTasks);
        }

    }
}