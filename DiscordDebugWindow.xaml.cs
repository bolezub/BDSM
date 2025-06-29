using System.Windows;

namespace BDSM
{
    public partial class DiscordDebugWindow : Window
    {
        public DiscordDebugWindow()
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;
        }
    }
}