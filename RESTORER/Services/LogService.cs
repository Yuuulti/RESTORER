using System;
using System.IO;
using System.Text;

namespace RESTORER.Services
{
    public static class LogService
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetEnvironmentVariable("backup") ??
            AppDomain.CurrentDomain.BaseDirectory,
            "restore_log.csv");

        public static void EnsureHeader()
        {
            if (!File.Exists(LogPath))
            {
                File.WriteAllText(LogPath,
                    "DateTime,FileName,Status,ErrorMessage\r\n",
                    Encoding.UTF8);
            }
        }

        public static void LogFailure(string fileName, string errorMessage)
        {
            Append(fileName, "Failed", errorMessage);
        }

        private static void Append(string fileName, string status, string error)
        {
            try
            {
                EnsureHeader();
                string line = string.Format(
                    "{0},{1},{2},{3}\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    CsvEscape(fileName),
                    status,
                    CsvEscape(error));
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash the app
            }
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}