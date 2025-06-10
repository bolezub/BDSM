using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using BDSM;
using Newtonsoft.Json;

public class MainViewModel : BaseViewModel
{
    public ObservableCollection<ServerViewModel> Servers { get; set; }

    public MainViewModel()
    {
        Servers = new ObservableCollection<ServerViewModel>();

        bool isInDesignMode = DesignerProperties.GetIsInDesignMode(new DependencyObject());

        if (!isInDesignMode)
        {
            var config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

            if (config != null && config.Servers != null)
            {
                foreach (var serverConfig in config.Servers)
                {
                    if (!serverConfig.IsHidden)
                    {
                        var svm = new ServerViewModel(serverConfig, config); // This line is correct
                        Servers.Add(svm);
                    }
                }
            }
        }
        else
        {
            // --- This section is corrected ---
            var dummyServerConfig = new ServerConfig { Name = "ASA Design Time", MemoryThresholdGB = 35 };
            var dummyGlobalConfig = new GlobalConfig(); // Create a dummy global config
            var dummyServer = new ServerViewModel(dummyServerConfig, dummyGlobalConfig) // Pass it here
            {
                Status = "Running",
                Pid = "PID 12345",
                CurrentPlayers = 3,
                MaxPlayers = 60,
                CpuUsage = 50,
                RamUsage = 16,
                ServerVersion = "design.time.version"
            };
            Servers.Add(dummyServer);
            Servers.Add(dummyServer);
        }
    }
}