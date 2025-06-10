using System.Windows;

namespace BDSM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // This line now correctly sets the DataContext at runtime
            this.DataContext = new MainViewModel();
        }
    }
}