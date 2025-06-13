using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using System.Collections.ObjectModel;

namespace BDSM
{
    public class GraphDetailViewModel : BaseViewModel
    {
        public string ServerName { get; }
        public ObservableCollection<ISeries> Series { get; }
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        public GraphDetailViewModel(string serverName, ObservableCollection<ISeries> series, Axis[] xAxes, Axis[] yAxes)
        {
            ServerName = serverName;
            Series = series;
            XAxes = xAxes;
            YAxes = yAxes;
        }
    }
}