using System;
using System.Windows.Threading;

namespace BDSM
{
    public class StatusBarViewModel : BaseViewModel
    {
        private string _latestBuildText = "Latest Build: checking...";
        public string LatestBuildText
        {
            get => _latestBuildText;
            set { _latestBuildText = value; OnPropertyChanged(); }
        }

        private string _nextBackupText = "Calculating...";
        public string NextBackupText
        {
            get => _nextBackupText;
            set { _nextBackupText = value; OnPropertyChanged(); }
        }

        private string _nextUpdateText = "Calculating...";
        public string NextUpdateText
        {
            get => _nextUpdateText;
            set { _nextUpdateText = value; OnPropertyChanged(); }
        }

        private string _nextScheduledTaskText = "No tasks scheduled.";
        public string NextScheduledTaskText
        {
            get => _nextScheduledTaskText;
            set { _nextScheduledTaskText = value; OnPropertyChanged(); }
        }

        public StatusBarViewModel()
        {
            // The constructor is empty. The timer is no longer here.
        }
    }
}