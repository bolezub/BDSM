using System;

namespace BDSM
{
    // This class holds the data for one point on our graph.
    public class PerformanceDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public int RamUsage { get; set; }
    }
}