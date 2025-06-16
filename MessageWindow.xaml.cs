using System.Windows;
using MaterialDesignThemes.Wpf;

namespace BDSM
{
    public partial class MessageWindow : Window
    {
        public string MessageText { get; private set; } = "";

        // A parameterless constructor for the designer
        public MessageWindow() : this("Dialog", "Enter text...")
        {
        }

        // The new constructor we need
        public MessageWindow(string title, string hint)
        {
            InitializeComponent();
            this.Owner = Application.Current.MainWindow;
            this.Title = title;
            HintAssist.SetHint(MessageTextBox, hint); // Set the hint text on the TextBox
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            MessageText = MessageTextBox.Text;
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}