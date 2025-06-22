using System.Windows;
using System.Windows.Input;

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

        // Allows the window to be dragged from the new title bar
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        // Handles the click on our custom Close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Handles the click on our custom Maximize/Restore button
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        // Handles the click on our custom Minimize button
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
    }
}