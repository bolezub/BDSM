using System.Collections.ObjectModel;
using System.IO;
using BDSM; // This using statement is now handled by the namespace
using Newtonsoft.Json;

namespace BDSM
{
    public class MainViewModel
    {
        public ObservableCollection<ServerViewModel> Servers { get; set; }

        public MainViewModel()
        {
            Servers = new ObservableCollection<ServerViewModel>();

            var config = JsonConvert.DeserializeObject<GlobalConfig>(File.ReadAllText("config.json"));

            if (config != null && config.Servers != null)
            {
                foreach (var serverConfig in config.Servers)
                {
                    if (!serverConfig.IsHidden)
                    {
                        var svm = new ServerViewModel(serverConfig);
                        // Set placeholder data based on the mockup
                        if (svm.ServerName == "The Island")
                        {
                            svm.Status = "Running"; svm.Pid = "PID 98451"; svm.CurrentPlayers = 1; svm.CpuUsage = 43; svm.RamUsage = 25;
                        }
                        else if (svm.ServerName == "Scorched Earth")
                        {
                            svm.Status = "Starting"; svm.CurrentPlayers = 0;
                        }
                        else if (svm.ServerName == "The Center")
                        {
                            svm.Status = "Stopped"; svm.CurrentPlayers = 0;
                        }
                        else // For Aberration, Extinction etc.
                        {
                            svm.Status = "Running"; svm.Pid = $"PID {new System.Random().Next(10000, 20000)}"; svm.CurrentPlayers = 1; svm.CpuUsage = 43; svm.RamUsage = 25;
                        }
                        Servers.Add(svm);
                    }
                }
            }
        }
    }
}