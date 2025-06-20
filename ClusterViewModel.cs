using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BDSM
{
    public class ClusterViewModel : BaseViewModel
    {
        private readonly ClusterConfig _clusterConfig;
        private readonly GlobalConfig _globalConfig;
        private readonly ApplicationViewModel _appViewModel; // Added for context

        public string Name => _clusterConfig.Name;
        public ObservableCollection<ServerViewModel> Servers { get; } = new ObservableCollection<ServerViewModel>();

        public ICommand StartAllCommand { get; }
        public ICommand StopAllCommand { get; }
        public ICommand EmergencyStopAllCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand RestartAllCommand { get; }
        public ICommand MessageAllCommand { get; }

        // MODIFIED: Constructor now accepts ApplicationViewModel
        public ClusterViewModel(ClusterConfig clusterConfig, GlobalConfig globalConfig, ApplicationViewModel appViewModel)
        {
            _clusterConfig = clusterConfig;
            _globalConfig = globalConfig;
            _appViewModel = appViewModel; // Store the reference

            StartAllCommand = new RelayCommand(
                _ => ServerOperationManager.StartAll(this.Servers.Where(s => s.IsActive)),
                _ => !TaskSchedulerService.IsMajorOperationInProgress);

            StopAllCommand = new RelayCommand(
                async _ => await UpdateManager.PerformMaintenanceShutdownAsync(this.Servers.Where(s => s.IsActive).ToList(), _globalConfig),
                _ => !TaskSchedulerService.IsMajorOperationInProgress);

            EmergencyStopAllCommand = new RelayCommand(
                async _ => await ServerOperationManager.StopAllAsync(this.Servers.Where(s => s.IsActive)),
                _ => !TaskSchedulerService.IsMajorOperationInProgress);

            SaveAllCommand = new RelayCommand(
                async _ => await ServerOperationManager.SaveAllAsync(this.Servers.Where(s => s.IsActive), _globalConfig),
                _ => !TaskSchedulerService.IsMajorOperationInProgress);

            RestartAllCommand = new RelayCommand(
                async _ => await UpdateManager.PerformScheduledRebootAsync(this.Servers.Where(s => s.IsActive).ToList(), _globalConfig),
                _ => !TaskSchedulerService.IsMajorOperationInProgress);

            MessageAllCommand = new RelayCommand(
                async _ => await ShowMessageDialog(),
                _ => !TaskSchedulerService.IsMajorOperationInProgress);
        }

        private async Task ShowMessageDialog()
        {
            var messageWindow = new MessageWindow("Send Cluster Message", "Enter message to send to all active servers in this cluster");

            bool? result = messageWindow.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(messageWindow.MessageText))
            {
                var activeServers = this.Servers.Where(s => s.IsActive);
                await ServerOperationManager.MessageAllAsync(activeServers, messageWindow.MessageText);
            }
        }
    }
}