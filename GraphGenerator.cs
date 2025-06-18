using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BDSM
{
    public static class GraphGenerator
    {
        public static async Task<string?> CreateGraphImageAsync(ServerViewModel server)
        {
            try
            {
                var rawData = await DataLogger.GetPerformanceDataAsync(server.ServerName, 24);

                // NEW: Create a complete timeline for the last 24 hours, filling in gaps
                var timeNow = DateTime.Now;
                var startTime = timeNow.AddHours(-24);
                var filledData = new List<PerformanceDataPoint>();

                // We'll create a point every 10 minutes for a smooth-looking graph
                for (var time = startTime; time <= timeNow; time = time.AddMinutes(10))
                {
                    // Find the closest data point we actually logged within a 10-minute window
                    var nearestPoint = rawData.OrderBy(p => Math.Abs((p.Timestamp - time).TotalMinutes))
                                              .FirstOrDefault(p => Math.Abs((p.Timestamp - time).TotalMinutes) < 10);

                    if (nearestPoint != null)
                    {
                        filledData.Add(new PerformanceDataPoint { Timestamp = time, CpuUsage = nearestPoint.CpuUsage, RamUsage = nearestPoint.RamUsage });
                    }
                    else
                    {
                        // If no data found, it means the server was down. Log as 0.
                        filledData.Add(new PerformanceDataPoint { Timestamp = time, CpuUsage = 0, RamUsage = 0 });
                    }
                }

                var cpuValues = filledData.Select(d => new DateTimePoint(d.Timestamp, d.CpuUsage)).ToList();
                var ramValues = filledData.Select(d => new DateTimePoint(d.Timestamp, d.RamUsage)).ToList();

                var chart = new SKCartesianChart
                {
                    Width = 1200,
                    Height = 600,
                    Series = new ISeries[]
                    {
                        new LineSeries<DateTimePoint>
                        {
                            Name = "CPU Usage (%)",
                            Values = cpuValues,
                            Fill = null,
                            Stroke = new SolidColorPaint(SKColors.CornflowerBlue) { StrokeThickness = 3 },
                            GeometryFill = null,
                            GeometryStroke = null,
                            ScalesYAt = 0
                        },
                        new LineSeries<DateTimePoint>
                        {
                            Name = "Memory Usage (GB)",
                            Values = ramValues,
                            Fill = null,
                            Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 3 },
                            GeometryFill = null,
                            GeometryStroke = null,
                            ScalesYAt = 1
                        }
                    },
                    XAxes = new[]
                    {
                        new Axis { Labeler = value => new DateTime((long)value).ToString("HH:mm"), UnitWidth = TimeSpan.FromHours(2).Ticks, MinLimit = startTime.Ticks, MaxLimit = timeNow.Ticks }
                    },
                    YAxes = new[]
                    {
                        new Axis { Name = "CPU (%)", MinLimit = 0, MaxLimit = 100, Position = LiveChartsCore.Measure.AxisPosition.Start },
                        new Axis { Name = "RAM (GB)", MinLimit = 0, Position = LiveChartsCore.Measure.AxisPosition.End }
                    },
                    Title = new LabelVisual
                    {
                        Text = $"{server.ServerName} - CPU and Memory Usage ({DateTime.Now:yyyy-MM-dd})",
                        TextSize = 24,
                        Paint = new SolidColorPaint(SKColors.Black)
                    },
                    LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom,
                    Background = SKColors.White
                };

                string graphDir = Path.Combine(AppContext.BaseDirectory, "graphs");
                Directory.CreateDirectory(graphDir);
                string sanitizedServerName = new string(server.ServerName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Replace(" ", "");
                string filePath = Path.Combine(graphDir, $"{sanitizedServerName}_{DateTime.Now:yyyy-MM-dd}.png");

                await Task.Run(() => chart.SaveImage(filePath));

                Debug.WriteLine($"Generated graph for {server.ServerName} at {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! FAILED to generate graph for {server.ServerName}: {ex.Message}");
                return null;
            }
        }
    }
}