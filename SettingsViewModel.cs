using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BDSM;
using Newtonsoft.Json;

namespace BDSM
{
    public class SettingsViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private ServerConfig? _selectedServer;

        public string SteamCMDPath { get; set; }
        public string BackupPath { get; set; }
        public string ServerIP { get; set; }
        public string RconPassword { get; set; }

        public ObservableCollection<ServerConfig> Servers { get; set; }
        public ServerConfig? SelectedServer
        {
            get => _selectedServer;
            set
            {
                _selectedServer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsServerSelected));
                // This is new - it tells the mods textbox to update when the selection changes.
                OnPropertyChanged(nameof(SelectedServerModsString));
            }
        }

        public bool IsServerSelected => SelectedServer != null;

        // --- This is the new proxy property for the mods list ---
        public string SelectedServerModsString
        {
            get
            {
                // If a server is selected, convert its list of mod IDs to a comma-separated string.
                if (SelectedServer == null) return "";
                return string.Join(",", SelectedServer.MapSpecificMods);
            }
            set
            {
                // When the user types in the textbox, convert the string back to a list of numbers.
                if (SelectedServer != null)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        SelectedServer.MapSpecificMods.Clear();
                    }
                    else
                    {
                        // This will safely parse the numbers and ignore any invalid text
                        var newModIds = new List<int>();
                        var parts = value.Split(',');
                        foreach (var part in parts)
                        {
                            if (int.TryParse(part.Trim(), out int modId))
                            {
                                newModIds.Add(modId);
                            }
                        }
                        SelectedServer.MapSpecificMods = newModIds;
                    }
                }
            }
        }
        // --------------------------------------------------------

        public List<string> AvailableMaps { get; } = new List<string>
        {
            "TheIsland_WP", "ScorchedEarth_WP", "TheCenter_WP", "Aberration_WP", "Extinction_WP", "Astraeos_WP"
        };

        public ICommand SaveSettingsCommand { get; }
        public ICommand AddServerCommand { get; }
        public ICommand RemoveServerCommand { get; }

        public SettingsViewModel(GlobalConfig globalConfig)
        {
            _config = globalConfig;

            SteamCMDPath = _config.SteamCMDPath;
            BackupPath = _config.BackupPath;
            ServerIP = _config.ServerIP;
            RconPassword = _config.RconPassword;

            Servers = new ObservableCollection<ServerConfig>(_config.Servers);

            SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
            AddServerCommand = new RelayCommand(_ => AddServer());
            RemoveServerCommand = new RelayCommand(_ => RemoveServer(), _ => IsServerSelected);
        }

        private void AddServer()
        {
            var newServer = new ServerConfig
            {
                Name = "New Server",
                InstallDir = @"D:\Servers\NewServer",
                MapFolder = "TheIsland_WP",
                Port = 1500,
                QueryPort = 15001,
                RconPort = 15100,
                Active = true
            };
            Servers.Add(newServer);
            SelectedServer = newServer;
        }

        private void RemoveServer()
        {
            if (SelectedServer == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to remove the server '{SelectedServer.Name}'?\nThis action cannot be undone.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Servers.Remove(SelectedServer);
            }
        }

        private void SaveSettings()
        {
            _config.SteamCMDPath = this.SteamCMDPath;
            _config.BackupPath = this.BackupPath;
            _config.ServerIP = this.ServerIP;
            _config.RconPassword = this.RconPassword;

            _config.Servers = this.Servers.ToList();

            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);
                MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}