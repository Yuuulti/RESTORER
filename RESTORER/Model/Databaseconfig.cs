using System;
using System.Configuration;
using System.IO;
using RESTORER.Services;

namespace RESTORER.Model
{
    public class DatabaseConfig
    {
        public string Server { get; private set; }
        public string Port { get; private set; }
        public string Database { get; set; }
        public string UserId { get; private set; }
        public string Password { get; private set; }
        public string MySqlPath { get; set; }
        public string SevenZipPath { get; set; }
        public string BackupDir { get; private set; }

        public DatabaseConfig()
        {
            var cs = ConfigurationManager.ConnectionStrings["MySqlConnection"];
            if (cs == null)
                throw new Exception("MySqlConnection not found in App.config");

            foreach (var part in cs.ConnectionString.Split(';'))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim().ToLower();
                var val = kv[1].Trim();
                switch (key)
                {
                    case "server":
                    case "host": Server = val; break;
                    case "port": Port = val; break;
                    case "database":
                    case "initial catalog": Database = val; break;
                    case "uid":
                    case "user id":
                    case "username": UserId = val; break;
                    case "password":
                    case "pwd": Password = val; break;
                }
            }

            if (string.IsNullOrWhiteSpace(Port)) Port = "3306";

            // Decrypt the DB password stored in connection string
            if (!string.IsNullOrWhiteSpace(Password))
            {
                try
                {
                    var crypto = new ACryptoServiceProvider();
                    Password = crypto.Decrypt(Password, "pullasciiencrypt");
                }
                catch
                {
                    // If decryption fails, use as-is (plain text fallback)
                }
            }

            // ── Paths from PATH env var, BackupDir from named var ──
            MySqlPath = FindInPath("mysql.exe");
            SevenZipPath = FindInPath("7z.exe");
            BackupDir = RequireEnv("backup");
        }

        private static string FindInPath(string exeName)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var dir in pathVar.Split(';'))
            {
                string trimmed = dir.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                try
                {
                    string full = Path.Combine(trimmed, exeName);
                    if (File.Exists(full))
                        return full;
                }
                catch { }
            }

            throw new Exception(string.Format(
                "Could not find '{0}' in any folder listed in the system PATH.\n\n" +
                "Please ensure the folder containing '{0}' is added to your PATH " +
                "environment variable in System Properties → Environment Variables.",
                exeName));
        }

        private static string RequireEnv(string name)
        {
            string val = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(val))
                throw new Exception(string.Format(
                    "Required environment variable '{0}' is not set.\n\n" +
                    "Please set it in System Properties → Environment Variables.",
                    name));
            return val;
        }

        public static void SavePath(string key, string value)
        {
            var config = ConfigurationManager.OpenExeConfiguration(
                ConfigurationUserLevel.None);
            if (config.AppSettings.Settings[key] == null)
                config.AppSettings.Settings.Add(key, value);
            else
                config.AppSettings.Settings[key].Value = value;
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}