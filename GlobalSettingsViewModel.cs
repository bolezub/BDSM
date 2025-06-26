using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;

namespace BDSM
{
    public class GlobalSettingsViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;
        private string _newMapName = string.Empty;
        private string? _selectedMap;

        public ObservableCollection<string> EditableAvailableMaps { get; set; }

        public string NewMapName
        {
            get => _newMapName;
            set { _newMapName = value; OnPropertyChanged(); }
        }

        public string? SelectedMap
        {
            get => _selectedMap;
            set { _selectedMap = value; OnPropertyChanged(); }
        }

        public string SteamCMDPath
        {
            get => _config.SteamCMDPath;
            set { _config.SteamCMDPath = value; OnPropertyChanged(); }
        }

        public string BackupPath
        {
            get => _config.BackupPath;
            set { _config.BackupPath = value; OnPropertyChanged(); }
        }

        public string GameUserSettingsTemplatePath
        {
            get => _config.GameUserSettingsTemplatePath;
            set { _config.GameUserSettingsTemplatePath = value; OnPropertyChanged(); }
        }

        public string ServerIP
        {
            get => _config.ServerIP;
            set { _config.ServerIP = value; OnPropertyChanged(); }
        }

        public string DiscordWebhookUrl
        {
            get => _config.discordWebhookUrl;
            set { _config.discordWebhookUrl = value; OnPropertyChanged(); }
        }

        public string WatchdogDiscordWebhookUrl
        {
            get => _config.WatchdogDiscordWebhookUrl;
            set { _config.WatchdogDiscordWebhookUrl = value; OnPropertyChanged(); }
        }

        public string BotToken
        {
            get => _config.BotToken;
            set { _config.BotToken = value; OnPropertyChanged(); }
        }

        public int BackupIntervalMinutes
        {
            get => _config.BackupIntervalMinutes;
            set { _config.BackupIntervalMinutes = value; OnPropertyChanged(); }
        }

        public int UpdateCheckIntervalMinutes
        {
            get => _config.UpdateCheckIntervalMinutes;
            set { _config.UpdateCheckIntervalMinutes = value; OnPropertyChanged(); }
        }

        public ICommand SaveGlobalSettingsCommand { get; }
        public ICommand BrowseSteamCMDCommand { get; }
        public ICommand BrowseBackupsPathCommand { get; }
        public ICommand BrowseTemplatePathCommand { get; }
        public ICommand AddMapCommand { get; }
        public ICommand RemoveMapCommand { get; }

        public GlobalSettingsViewModel(GlobalConfig globalConfig)
        {
            _config = globalConfig;

            EditableAvailableMaps = new ObservableCollection<string>(_config.AvailableMaps);

            SaveGlobalSettingsCommand = new RelayCommand(_ => SaveSettings());
            BrowseSteamCMDCommand = new RelayCommand(_ => BrowseForFile(SteamCMDPath, path => SteamCMDPath = path, "Executable files (*.exe)|*.exe"));
            BrowseBackupsPathCommand = new RelayCommand(_ => BrowseForFolder(BackupPath, path => BackupPath = path));
            BrowseTemplatePathCommand = new RelayCommand(_ => BrowseForFile(GameUserSettingsTemplatePath, path => GameUserSettingsTemplatePath = path, "INI files (*.ini)|*.ini"));

            AddMapCommand = new RelayCommand(_ => AddMap(), _ => !string.IsNullOrWhiteSpace(NewMapName) && !EditableAvailableMaps.Contains(NewMapName));
            RemoveMapCommand = new RelayCommand(_ => RemoveMap(), _ => SelectedMap != null);
        }

        private void AddMap()
        {
            EditableAvailableMaps.Add(NewMapName);
            NewMapName = string.Empty;
        }

        private void RemoveMap()
        {
            if (SelectedMap == null) return;

            var allServers = _config.Clusters.SelectMany(c => c.Servers);
            if (allServers.Any(s => s.MapFolder == SelectedMap))
            {
                MessageBox.Show($"Cannot remove the map '{SelectedMap}' because it is currently being used by one or more servers in your Clusters configuration.", "Map in Use", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            EditableAvailableMaps.Remove(SelectedMap);
        }

        private void SaveSettings()
        {
            _config.AvailableMaps = EditableAvailableMaps.ToList();

            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);
                BackupSchedulerService.RecalculateNextRunTime();
                UpdateSchedulerService.RecalculateNextRunTime();
                NotificationService.ShowInfo("Global settings saved successfully.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save global settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseForFile(string currentPath, System.Action<string> setPathAction, string filter)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = $"{filter}|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                string? directory = Path.GetDirectoryName(currentPath);
                if (Directory.Exists(directory))
                {
                    openFileDialog.InitialDirectory = directory;
                }
            }

            if (openFileDialog.ShowDialog() == true)
            {
                setPathAction(openFileDialog.FileName);
            }
        }

        private void BrowseForFolder(string currentPath, System.Action<string> setPathAction)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };

            if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            {
                dialog.InitialDirectory = currentPath;
            }

            if (dialog.ShowDialog() == true)
            {
                string? folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    setPathAction(folderPath);
                }
            }
        }
    }
}