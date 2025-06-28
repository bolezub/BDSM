using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BDSM
{
    public class EventLogViewModel : BaseViewModel
    {
        public ObservableCollection<string> LogEntries { get; }

        public ICommand RefreshLogCommand { get; }
        public ICommand ClearLogCommand { get; }

        public EventLogViewModel()
        {
            LogEntries = new ObservableCollection<string>();
            RefreshLogCommand = new RelayCommand(_ => LoadLog());
            ClearLogCommand = new RelayCommand(_ => ClearLog());
            LoadLog();
        }

        private void LoadLog()
        {
            LogEntries.Clear();
            var entries = LoggingService.ReadLog();
            // Show newest entries first
            foreach (var entry in entries.AsEnumerable().Reverse())
            {
                LogEntries.Add(entry);
            }
        }

        private void ClearLog()
        {
            var result = MessageBox.Show("Are you sure you want to permanently clear the event log?", "Confirm Clear Log", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                LoggingService.ClearLog();
                LoadLog();
                NotificationService.ShowInfo("Event log cleared.");
            }
        }
    }
}