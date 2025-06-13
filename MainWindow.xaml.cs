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
            // This line now points to our renamed ViewModel
            this.DataContext = new ApplicationViewModel();
        }
    }
}