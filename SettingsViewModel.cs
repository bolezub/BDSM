using CoreRCON;
using CoreRCON.Parsers.Standard;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BDSM
{
    public class SettingsViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private ClusterConfig? _selectedCluster;
        private ServerConfig? _selectedServer;

        public ObservableCollection<ClusterConfig> Clusters { get; set; }

        public ClusterConfig? SelectedCluster
        {
            get => _selectedCluster;
            set
            {
                _selectedCluster = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsClusterSelected));
                OnPropertyChanged(nameof(SelectedClusterMainModList));

                // --- ADD THIS LINE to notify the server list to update ---
                OnPropertyChanged(nameof(ServersInSelectedCluster));

                SelectedServer = null;
            }
        }

        public ServerConfig? SelectedServer
        {
            get => _selectedServer;
            set
            {
                _selectedServer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsServerSelected));
                OnPropertyChanged(nameof(SelectedServerMapSpecificMods));
            }
        }

        public bool IsClusterSelected => SelectedCluster != null;
        public bool IsServerSelected => SelectedServer != null;

        // This property gets the server list ONLY from the currently selected cluster
        public ObservableCollection<ServerConfig>? ServersInSelectedCluster
        {
            get => SelectedCluster != null ? new ObservableCollection<ServerConfig>(SelectedCluster.Servers) : null;
            set
            {
                if (SelectedCluster != null)
                {
                    SelectedCluster.Servers = value?.ToList() ?? new List<ServerConfig>();
                    OnPropertyChanged();
                }
            }
        }

        #region Proxy Properties for Mod Lists

        // Proxy for the selected cluster's main mod list
        public string SelectedClusterMainModList
        {
            get => SelectedCluster != null ? string.Join(",", SelectedCluster.MainModList) : "";
            set
            {
                if (SelectedCluster != null)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        SelectedCluster.MainModList.Clear();
                    }
                    else
                    {
                        SelectedCluster.MainModList = value.Split(',')
                            .Select(part => int.TryParse(part.Trim(), out int modId) ? modId : -1)
                            .Where(modId => modId != -1)
                            .ToList();
                    }
                }
            }
        }

        // Proxy for the selected server's map-specific mod list
        public string SelectedServerMapSpecificMods
        {
            get => SelectedServer != null ? string.Join(",", SelectedServer.MapSpecificMods) : "";
            set
            {
                if (SelectedServer != null)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        SelectedServer.MapSpecificMods.Clear();
                    }
                    else
                    {
                        SelectedServer.MapSpecificMods = value.Split(',')
                            .Select(part => int.TryParse(part.Trim(), out int modId) ? modId : -1)
                            .Where(modId => modId != -1)
                            .ToList();
                    }
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        public List<string> AvailableMaps { get; } = new List<string>
        {
            "TheIsland_WP", "ScorchedEarth_WP", "TheCenter_WP", "Aberration_WP", "Extinction_WP", "Astraeos_WP"
        };

        public ICommand SaveSettingsCommand { get; }
        public ICommand AddClusterCommand { get; }
        public ICommand RemoveClusterCommand { get; }
        public ICommand AddServerCommand { get; }
        public ICommand RemoveServerCommand { get; }

        public SettingsViewModel(GlobalConfig globalConfig)
        {
            _config = globalConfig;
            Clusters = new ObservableCollection<ClusterConfig>(_config.Clusters);

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            AddClusterCommand = new RelayCommand(_ => AddCluster());
            RemoveClusterCommand = new RelayCommand(_ => RemoveCluster(), _ => IsClusterSelected);
            AddServerCommand = new RelayCommand(_ => AddServer(), _ => IsClusterSelected);
            RemoveServerCommand = new RelayCommand(_ => RemoveServer(), _ => IsServerSelected);
        }

        private void AddCluster()
        {
            var newCluster = new ClusterConfig();
            Clusters.Add(newCluster);
            SelectedCluster = newCluster;
        }

        private void RemoveCluster()
        {
            if (SelectedCluster == null) return;
            var result = MessageBox.Show($"Are you sure you want to remove the cluster '{SelectedCluster.Name}' and all its servers?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                Clusters.Remove(SelectedCluster);
            }
        }

        private void AddServer()
        {
            if (SelectedCluster == null) return;
            var newServer = new ServerConfig { Name = "New Server", Active = true, MapFolder = "TheIsland_WP" };
            SelectedCluster.Servers.Add(newServer);
            OnPropertyChanged(nameof(ServersInSelectedCluster)); // Notify UI the collection has changed
            SelectedServer = newServer;
        }

        private void RemoveServer()
        {
            if (SelectedCluster == null || SelectedServer == null) return;
            var result = MessageBox.Show($"Are you sure you want to remove the server '{SelectedServer.Name}'?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                SelectedCluster.Servers.Remove(SelectedServer);
                OnPropertyChanged(nameof(ServersInSelectedCluster)); // Notify UI the collection has changed
            }
        }

        private void SaveSettings()
        {
            // The collections are already updated by binding, so we just need to save the main config object.
            _config.Clusters = Clusters.ToList();

            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);
                MessageBox.Show("Settings saved successfully! You may need to restart the application for all changes to take effect.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}