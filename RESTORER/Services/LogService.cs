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
                    "DateTime,FileName,TargetSchema,Status,ErrorMessage\r\n",
                    Encoding.UTF8);
            }
        }

        // ── Existing: log a restore failure ──
        public static void LogFailure(string schemaName, string errorMessage)
        {
            Append(string.Empty, schemaName, "Failed", errorMessage);
        }

        // ── New: no schema was selected/checked by the user ──
        public static void LogNoTargetSchema(string zipFileName)
        {
            Append(zipFileName, string.Empty, "Skipped", "No target schema selected");
        }

        // ── New: schema was selected but no matching SQL file found in the .7z ──
        public static void LogNoBackupProvided(string zipFileName, string schemaName)
        {
            Append(zipFileName, schemaName, "Skipped", "No backup provided for this schema");
        }

        // ── New: restore completed successfully ──
        public static void LogSuccess(string zipFileName, string schemaName)
        {
            Append(zipFileName, schemaName, "Success", string.Empty);
        }

        // ── Core append ──
        private static void Append(string fileName, string schema, string status, string error)
        {
            try
            {
                EnsureHeader();
                string line = string.Format(
                    "{0},{1},{2},{3},{4}\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    CsvEscape(fileName),
                    CsvEscape(schema),
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