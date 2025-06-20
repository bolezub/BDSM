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
                if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
                {
                    System.Diagnostics.Debug.WriteLine($"!!! DATABASE INITIALIZATION FAILED: Base path is invalid or does not exist.");
                    return;
                }

                _databaseFile = Path.Combine(basePath, "performance_logs.db");

                using (var connection = new SqliteConnection($"Data Source={_databaseFile}"))
                {
                    connection.Open();

                    var command = connection.CreateCommand();
                    // FIX: Change ServerName column to ServerId for unique tracking
                    command.CommandText =
                    @"
                        CREATE TABLE IF NOT EXISTS PerformanceLogs (
                            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            Timestamp TEXT NOT NULL,
                            ServerId TEXT NOT NULL, 
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

        // FIX: Log data against the server's unique Guid
        public static async Task LogDataPoint(Guid serverId, float cpuUsage, int ramUsage)
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
                        INSERT INTO PerformanceLogs (Timestamp, ServerId, CpuUsage, RamUsage)
                        VALUES ($timestamp, $serverId, $cpuUsage, $ramUsage);
                    ";

                    command.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("o"));
                    command.Parameters.AddWithValue("$serverId", serverId.ToString());
                    command.Parameters.AddWithValue("$cpuUsage", Math.Round(cpuUsage, 2));
                    command.Parameters.AddWithValue("$ramUsage", ramUsage);

                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! FAILED to write performance data to database for {serverId}: {ex.Message}");
            }
        }

        public static async Task<List<PerformanceDataPoint>> GetPerformanceDataAsync(Guid serverId, int hours = 24)
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
                        WHERE ServerId = $serverId AND Timestamp >= $startTime
                        ORDER BY Timestamp;
                    ";

                    var startTime = DateTime.Now.AddHours(-hours).ToString("o");
                    command.Parameters.AddWithValue("$serverId", serverId.ToString());
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
                System.Diagnostics.Debug.WriteLine($"!!! FAILED to read performance data for {serverId}: {ex.Message}");
            }

            return dataPoints;
        }
    }
}