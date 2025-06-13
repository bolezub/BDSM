using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BDSM
{
    public static class DataLogger
    {
        private static string _databaseFile = string.Empty;

        public static void InitializeDatabase(string basePath)
        {
            try
            {
                _databaseFile = Path.Combine(basePath, "performance_logs.db");

                using (var connection = new SqliteConnection($"Data Source={_databaseFile}"))
                {
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        CREATE TABLE IF NOT EXISTS PerformanceLogs (
                            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            Timestamp TEXT NOT NULL,
                            ServerName TEXT NOT NULL,
                            CpuUsage REAL NOT NULL,
                            RamUsage INTEGER NOT NULL
                        );
                    ";

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! DATABASE INITIALIZATION FAILED: {ex.Message}");
            }
        }

        public static async Task LogDataPoint(string serverName, float cpuUsage, int ramUsage)
        {
            if (string.IsNullOrEmpty(_databaseFile)) return;

            try
            {
                using (var connection = new SqliteConnection($"Data Source={_databaseFile}"))
                {
                    await connection.OpenAsync();

                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        INSERT INTO PerformanceLogs (Timestamp, ServerName, CpuUsage, RamUsage)
                        VALUES ($timestamp, $serverName, $cpuUsage, $ramUsage);
                    ";

                    command.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("o"));
                    command.Parameters.AddWithValue("$serverName", serverName);
                    command.Parameters.AddWithValue("$cpuUsage", Math.Round(cpuUsage, 2));
                    command.Parameters.AddWithValue("$ramUsage", ramUsage);

                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! FAILED to write performance data to database for {serverName}: {ex.Message}");
            }
        }

        // --- THIS IS THE NEW METHOD ---
        public static async Task<List<PerformanceDataPoint>> GetPerformanceDataAsync(string serverName, int hours = 24)
        {
            var dataPoints = new List<PerformanceDataPoint>();
            if (string.IsNullOrEmpty(_databaseFile)) return dataPoints;

            try
            {
                using (var connection = new SqliteConnection($"Data Source={_databaseFile}"))
                {
                    await connection.OpenAsync();
                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                        SELECT Timestamp, CpuUsage, RamUsage
                        FROM PerformanceLogs
                        WHERE ServerName = $serverName AND Timestamp >= $startTime
                        ORDER BY Timestamp;
                    ";

                    var startTime = DateTime.Now.AddHours(-hours).ToString("o");
                    command.Parameters.AddWithValue("$serverName", serverName);
                    command.Parameters.AddWithValue("$startTime", startTime);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            dataPoints.Add(new PerformanceDataPoint
                            {
                                Timestamp = reader.GetDateTime(0),
                                CpuUsage = reader.GetDouble(1),
                                RamUsage = reader.GetInt32(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! FAILED to read performance data for {serverName}: {ex.Message}");
            }

            return dataPoints;
        }
        // --- END OF NEW METHOD ---
    }
}