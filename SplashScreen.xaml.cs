using System;
using System.Diagnostics;
using System.Windows;

namespace BDSM
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            SplashPlayer.Play();
        }

        private void SplashPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Forcing the control to explicitly stop before re-playing is more robust.
                SplashPlayer.Stop();
                SplashPlayer.Position = TimeSpan.FromSeconds(0);
                SplashPlayer.Play();
            }
            catch (Exception ex)
            {
                // If an error happens inside this handler, we absolutely must know about it.
                // This will tell us if the Play() or Stop() command is failing.
                MessageBox.Show($"An error occurred while trying to loop the video:\n\n{ex.Message}",
                                "Video Loop Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }
    }
}