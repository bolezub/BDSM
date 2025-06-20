using System.Windows.Media.Imaging;

namespace BDSM
{
    public class GraphDetailViewModel : BaseViewModel
    {
        public string ServerName { get; }
        public BitmapImage GraphImage { get; }

        public GraphDetailViewModel(string serverName, BitmapImage graphImage)
        {
            ServerName = serverName;
            GraphImage = graphImage;
        }
    }
}