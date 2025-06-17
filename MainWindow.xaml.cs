using System.Windows;

namespace BDSM
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new ApplicationViewModel();

            NotificationService.RegisterSnackbar(MainSnackbar.MessageQueue);
        }
    }
}