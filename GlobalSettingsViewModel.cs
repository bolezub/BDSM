using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Text;

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

        public string BotPrefix
        {
            get => _config.BotPrefix;
            set { _config.BotPrefix = value; OnPropertyChanged(); }
        }

        public string StartArgumentsTemplate
        {
            get => _config.StartArgumentsTemplate;
            set { _config.StartArgumentsTemplate = value; OnPropertyChanged(); }
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

        public int ShutdownTimeoutSeconds
        {
            get => _config.ShutdownTimeoutSeconds;
            set { _config.ShutdownTimeoutSeconds = value; OnPropertyChanged(); }
        }

        public int SteamCmdTimeoutMinutes
        {
            get => _config.SteamCmdTimeoutMinutes;
            set { _config.SteamCmdTimeoutMinutes = value; OnPropertyChanged(); }
        }

        public ICommand SaveGlobalSettingsCommand { get; }
        public ICommand BrowseSteamCMDCommand { get; }
        public ICommand BrowseBackupsPathCommand { get; }
        public ICommand BrowseTemplatePathCommand { get; }
        public ICommand AddMapCommand { get; }
        public ICommand RemoveMapCommand { get; }
        // --- NEW COMMAND ---
        public ICommand ShowBotHelpCommand { get; }

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

            // --- NEW COMMAND INITIALIZATION ---
            ShowBotHelpCommand = new RelayCommand(_ => ShowBotHelp());
        }

        // --- NEW METHOD TO DISPLAY HELP ---
        private void ShowBotHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("To use the Discord bot, you must create your own bot application and invite it to your server.");
            sb.AppendLine();
            sb.AppendLine("--- Part 1: Create the Bot ---");
            sb.AppendLine("1. Go to the Discord Developer Portal (discord.com/developers/applications).");
            sb.AppendLine("2. Click 'New Application' and give it a name (e.g., 'My ASA Bot').");
            sb.AppendLine("3. Go to the 'Bot' tab on the left menu.");
            sb.AppendLine("4. Scroll down to 'Privileged Gateway Intents' and turn ON both 'SERVER MEMBERS INTENT' and 'MESSAGE CONTENT INTENT'.");
            sb.AppendLine("5. Click 'Reset Token' to generate your bot's token. Copy this token and paste it into the 'Discord Bot Token' field in this application.");
            sb.AppendLine();
            sb.AppendLine("--- Part 2: Invite the Bot to Your Server ---");
            sb.AppendLine("1. In the Developer Portal, go to 'OAuth2' -> 'URL Generator'.");
            sb.AppendLine("2. In 'Scopes', check the 'bot' box.");
            sb.AppendLine("3. In 'Bot Permissions' that appears below, check 'Send Messages', 'Read Message History', and 'Embed Links'.");
            sb.AppendLine("4. Copy the generated URL at the bottom, paste it into your browser, and invite the bot to your Discord server.");
            sb.AppendLine();
            sb.AppendLine("--- Part 3: Check Channel Permissions ---");
            sb.AppendLine("1. In your Discord server, make sure the bot's role has permission to View, Read, and Send messages in the channel(s) where you want to use commands.");

            MessageBox.Show(sb.ToString(), "Discord Bot Setup Instructions", MessageBoxButton.OK, MessageBoxImage.Information);
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