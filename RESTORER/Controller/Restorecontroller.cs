using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Configuration;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using RESTORER.Model;
using RESTORER.Services;
using System.Runtime.InteropServices;

namespace RESTORER.Controller
{
    public class RestoreResult
    {
        public List<string> Restored { get; set; } = new List<string>();
        public List<string> Skipped { get; set; } = new List<string>();
    }

    public class RestoreController
    {
        private readonly DatabaseConfig _config;
        private readonly ACryptoServiceProvider _crypto;

        public RestoreController()
        {
            _config = new DatabaseConfig();
            _crypto = new ACryptoServiceProvider();
            LogService.EnsureHeader();
        }

        public DatabaseConfig GetConfig() => _config;

        // ── GET ALL SCHEMAS FROM MySQL SERVER ──
        public List<string> GetServerDatabases()
        {
            var databases = new List<string>();

            string connStr = string.Format(
                "Server={0};Port={1};Uid={2};Password={3};",
                _config.Server, _config.Port, _config.UserId, _config.Password);

            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SHOW DATABASES;", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string dbName = reader.GetString(0);
                        if (dbName != "information_schema" &&
                            dbName != "performance_schema" &&
                            dbName != "mysql" &&
                            dbName != "sys")
                            databases.Add(dbName);
                    }
                }
            }

            return databases;
        }

        // ── GET SCHEMAS INSIDE A ZIP ──
        public List<string> GetDatabasesFromZip(string zipPath)
        {
            string plainPassword = GetPlainZipPassword();

            var databases = new List<string>();

            string args = string.Format(
                "l \"{0}\" \"-p{1}\"",
                zipPath, plainPassword);

            var psi = new ProcessStartInfo
            {
                FileName = _config.SevenZipPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception(
                        "Failed to read the backup file. " +
                        "The password may be incorrect.\n\n" + error);

                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(
                            new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(
                                parts[parts.Length - 1]);
                            if (!string.IsNullOrWhiteSpace(fileName))
                                databases.Add(fileName);
                        }
                    }
                }
            }

            return databases;
        }

        // ── RESTORE SELECTED SCHEMAS ──
        public RestoreResult RestoreSelected(
            string sourceZipPath,
            List<string> selectedSchemas,
            Action<string> progressCallback)
        {
            var result = new RestoreResult();

            if (string.IsNullOrWhiteSpace(sourceZipPath))
                throw new ArgumentException("Source path cannot be empty.");
            if (!File.Exists(sourceZipPath))
                throw new FileNotFoundException("Backup file not found.", sourceZipPath);
            if (selectedSchemas == null || selectedSchemas.Count == 0)
                throw new Exception("No schemas selected for restore.");

            string plainPassword = GetPlainZipPassword();

            List<string> zipEntries = GetDatabasesFromZip(sourceZipPath);

            foreach (string schemaName in selectedSchemas)
            {
                string matchedEntry = zipEntries.Find(z =>
                    z.Equals(schemaName, StringComparison.OrdinalIgnoreCase) ||
                    z.StartsWith(schemaName + "_", StringComparison.OrdinalIgnoreCase)
                );

                if (matchedEntry == null)
                {
                    result.Skipped.Add(schemaName);
                    LogService.LogFailure(schemaName, "Schema not found in backup file");
                    continue;
                }

                progressCallback?.Invoke(string.Format("Restoring: {0} ...", schemaName));

                string tempFolder = Path.Combine(
                    @"C:\Temp\MySQLRestore",
                    string.Format("restore_{0:N}", Guid.NewGuid()));
                Directory.CreateDirectory(tempFolder);

                try
                {
                    string sevenZipGui = Path.Combine(
                    Path.GetDirectoryName(_config.SevenZipPath), "7zG.exe");

                    string extractArgs = string.Format(
                        "e \"{0}\" -o\"{1}\" \"-p{2}\" \"{3}.sql\"",
                        sourceZipPath, tempFolder, plainPassword, matchedEntry);

                    RunProcessGui(sevenZipGui, extractArgs, "7-Zip extraction failed");

                    string sqlFile = Path.Combine(tempFolder, matchedEntry + ".sql");
                    if (!File.Exists(sqlFile))
                        throw new FileNotFoundException(string.Format(
                            "Could not find {0}.sql inside the archive.", matchedEntry));

                    _config.Database = schemaName;
                    RestoreDatabase(sqlFile);

                    result.Restored.Add(schemaName);
                }
                catch (Exception ex)
                {
                    LogService.LogFailure(schemaName, ex.Message);
                    throw;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempFolder))
                            Directory.Delete(tempFolder, true);
                    }
                    catch { }
                }
            }

            return result;
        }
        // ── PRIVATE: RUN 7zG WITH VISIBLE GUI PROGRESS WINDOW ──
        // ── PRIVATE: RUN 7zG WITH VISIBLE GUI PROGRESS WINDOW ──
        private static void RunProcessGui(string exe, string args, string errorMsg)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();

                // Give the window time to appear then bring it to front
                System.Threading.Thread.Sleep(500);

                try
                {
                    process.WaitForInputIdle(2000);
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, 9);   // SW_RESTORE
                        SetForegroundWindow(process.MainWindowHandle);
                    }
                }
                catch { }

                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception(string.Format(
                        "{0} (exit {1})", errorMsg, process.ExitCode));
            }
        }

        // ── Win32 imports to bring 7zG window to front ──
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // ── PRIVATE: DECRYPT ZIP PASSWORD FROM App.config ──
        private string GetPlainZipPassword()
        {
            string encryptedPassword =
                ConfigurationManager.AppSettings["EncrpytedPassword"];
            if (string.IsNullOrWhiteSpace(encryptedPassword))
                throw new Exception(
                    "No encrypted password found in App.config.\n\n" +
                    "Please add 'EncrpytedPassword' key under <appSettings>.");

            return _crypto.Decrypt(encryptedPassword, "pullasciiencrypt");
        }

        // ── PRIVATE: RESTORE ONE SQL FILE INTO _config.Database ──
        private void RestoreDatabase(string sqlFilePath)
        {
            string connStr = string.Format(
                "Server={0};Port={1};Uid={2};Password={3};",
                _config.Server, _config.Port, _config.UserId, _config.Password);

            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand(
                    string.Format(
                        "CREATE DATABASE IF NOT EXISTS `{0}`;", _config.Database), conn))
                    cmd.ExecuteNonQuery();
            }

            string cleanSqlPath = sqlFilePath + ".clean.sql";
            try
            {
                // Strip USE statements so data always lands in the correct schema
                using (var reader = new StreamReader(sqlFilePath, Encoding.UTF8))
                using (var writer = new StreamWriter(cleanSqlPath, false, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.TrimStart().StartsWith(
                            "USE ", StringComparison.OrdinalIgnoreCase))
                            continue;
                        writer.WriteLine(line);
                    }
                }

                string args = string.Format(
                    "--host={0} --port={1} --user={2} --password={3} {4}",
                    _config.Server, _config.Port, _config.UserId,
                    _config.Password, _config.Database);

                var psi = new ProcessStartInfo
                {
                    FileName = _config.MySqlPath,
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = psi })
                {
                    process.Start();

                    using (var fileStream = new FileStream(
                        cleanSqlPath, FileMode.Open, FileAccess.Read))
                    {
                        fileStream.CopyTo(process.StandardInput.BaseStream);
                    }
                    process.StandardInput.BaseStream.Flush();
                    process.StandardInput.Close();
                    process.WaitForExit();

                    string error = process.StandardError.ReadToEnd();
                    if (process.ExitCode != 0)
                        throw new Exception(string.Format(
                            "mysql restore failed for '{0}' (exit {1}):\n{2}",
                            _config.Database, process.ExitCode, error));
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(cleanSqlPath)) File.Delete(cleanSqlPath);
                }
                catch { }
            }
        }

        // ── PRIVATE: RUN ANY EXTERNAL PROCESS ──
        private static void RunProcess(string exe, string args, string errorMsg)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception(string.Format(
                        "{0} (exit {1}):\nSTDOUT: {2}\nSTDERR: {3}",
                        errorMsg, process.ExitCode, output, error));
            }
        }

        private static string EscapeForShell(string input)
        {
            return input.Replace("\"", "\\\"");
        }
    }
}