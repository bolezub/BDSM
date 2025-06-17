using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BDSM
{
    public class IniFileManager
    {
        private readonly string _filePath;
        private List<string> _lines;

        public IniFileManager(string filePath)
        {
            _filePath = filePath;
            _lines = new List<string>();
        }

        /// <summary>
        /// Asynchronously loads the content of the .ini file into memory.
        /// </summary>
        public async Task LoadAsync()
        {
            if (!File.Exists(_filePath))
            {
                _lines = new List<string>();
                return;
            }
            _lines = (await File.ReadAllLinesAsync(_filePath)).ToList();
        }

        /// <summary>
        /// Asynchronously saves the content from memory back to the .ini file.
        /// </summary>
        public async Task SaveAsync()
        {
            // Ensure the directory exists before attempting to write the file.
            string? directory = Path.GetDirectoryName(_filePath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllLinesAsync(_filePath, _lines);
        }

        /// <summary>
        /// Gets a value for a given section and key.
        /// </summary>
        /// <param name="section">The section name (e.g., "ServerSettings").</param>
        /// <param name="key">The key name (e.g., "Port").</param>
        /// <returns>The value as a string, or an empty string if not found.</returns>
        public string GetValue(string section, string key)
        {
            string currentSection = "";
            foreach (var line in _lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                }
                else if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmedLine.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmedLine.Substring(key.Length + 1);
                    }
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Sets a value for a given section and key. If the key or section doesn't exist, they will be created.
        /// </summary>
        /// <param name="section">The section name (e.g., "ServerSettings").</param>
        /// <param name="key">The key name (e.g., "Port").</param>
        /// <param name="value">The value to set.</param>
        public void SetValue(string section, string key, string value)
        {
            string currentSection = "";
            int sectionLineIndex = -1;
            int keyLineIndex = -1;

            for (int i = 0; i < _lines.Count; i++)
            {
                var trimmedLine = _lines[i].Trim();
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionLineIndex = i;
                    }
                }
                else if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    // Use regex to match "key=" pattern while ignoring whitespace and case
                    if (Regex.IsMatch(trimmedLine, $@"^\s*{key}\s*=", RegexOptions.IgnoreCase))
                    {
                        keyLineIndex = i;
                        break;
                    }
                }
            }

            string newLine = $"{key}={value}";

            if (keyLineIndex != -1)
            {
                // Key was found, update the line
                _lines[keyLineIndex] = newLine;
            }
            else if (sectionLineIndex != -1)
            {
                // Section was found but key was not, add the key to the section
                _lines.Insert(sectionLineIndex + 1, newLine);
            }
            else
            {
                // Neither section nor key was found, add them to the end of the file
                _lines.Add($"[{section}]");
                _lines.Add(newLine);
            }
        }
    }
}