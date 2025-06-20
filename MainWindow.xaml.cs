using System.Windows;

namespace BDSM
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            NotificationService.RegisterSnackbar(MainSnackbar.MessageQueue);

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Check if the DataContext is the correct type
            if (this.DataContext is ApplicationViewModel appVM)
            {
                // Get the status bar view model and start its timer
                appVM.StatusBar.StartTimer();
            }
        }
    }
}