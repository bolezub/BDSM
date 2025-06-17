using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace BDSM
{
    public class GlobalSettingsViewModel : BaseViewModel
    {
        private readonly GlobalConfig _config;

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

        public string RconPassword
        {
            get => _config.RconPassword;
            set { _config.RconPassword = value; OnPropertyChanged(); }
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

        public GlobalSettingsViewModel(GlobalConfig globalConfig)
        {
            _config = globalConfig;
            SaveGlobalSettingsCommand = new RelayCommand(_ => SaveSettings());

            // MODIFIED: We now pass the current path to the browse commands
            BrowseSteamCMDCommand = new RelayCommand(_ => BrowseForFile(SteamCMDPath, path => SteamCMDPath = path, "Executable files (*.exe)|*.exe"));
            BrowseBackupsPathCommand = new RelayCommand(_ => BrowseForFolder(BackupPath, path => BackupPath = path));
            BrowseTemplatePathCommand = new RelayCommand(_ => BrowseForFile(GameUserSettingsTemplatePath, path => GameUserSettingsTemplatePath = path, "INI files (*.ini)|*.ini"));
        }

        private void SaveSettings()
        {
            try
            {
                string updatedJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText("config.json", updatedJson);
                NotificationService.ShowInfo("Global settings saved successfully.");
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save global settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // MODIFIED: Method now accepts the current path to set the initial directory
        private void BrowseForFile(string currentPath, System.Action<string> setPathAction, string filter)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = $"{filter}|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            // NEW: Set the initial directory based on the current path
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

        // MODIFIED: Method now accepts the current path to set the initial directory
        private void BrowseForFolder(string currentPath, System.Action<string> setPathAction)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };

            // NEW: Set the initial directory based on the current path
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