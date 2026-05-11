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
        // ── The fixed backup directory ──
        private const string BackupDirectory = @"D:\backup";

        private readonly DatabaseConfig _config;
        private readonly ACryptoServiceProvider _crypto;

        public RestoreController()
        {
            _config = new DatabaseConfig();
            _crypto = new ACryptoServiceProvider();
            LogService.EnsureHeader();
        }

        public DatabaseConfig GetConfig() => _config;

        // ────────────────────────────────────────────────────
        // GET ALL .7z FILES IN D:\backup
        // ────────────────────────────────────────────────────
        public List<string> GetBackupZipFiles()
        {
            var files = new List<string>();

            if (!Directory.Exists(BackupDirectory))
                throw new DirectoryNotFoundException(
                    string.Format("Backup directory not found: {0}", BackupDirectory));

            // BAGO:
            foreach (string filePath in Directory.GetFiles(BackupDirectory, "*.7z"))
                files.Add(filePath);
            foreach (string filePath in Directory.GetFiles(BackupDirectory, "*.rar"))
                files.Add(filePath);

            return files;
        }

        // ────────────────────────────────────────────────────
        // GET ALL SCHEMAS FROM MySQL SERVER
        // ────────────────────────────────────────────────────
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

        // ────────────────────────────────────────────────────
        // GET SQL ENTRY NAMES INSIDE A ZIP
        // Returns base file names WITHOUT .sql extension
        // ────────────────────────────────────────────────────
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
                                databases.Add(fileName); // original pa rin, hindi pa tri-trim
                        }
                    }
                }
            }

            return databases;
        }

        // ────────────────────────────────────────────────────
        // RESTORE SELECTED SCHEMAS
        //
        // Naming convention inside .7z:
        //   zip filename : roxas_2026_05_08.7z      → branch = "roxas"
        //   sql entry    : sofos2_foroxas_roxas.sql  → trim branch → "sofos2_foroxas"
        //   sql entry    : sofos2_cmtinga_2026_05_11 → trim date lang → "sofos2_cmtinga"
        //   schemaVar    : "_sofos2_foroxas" / "_sofos2_cmtinga" → must match selectedSchemas
        // ────────────────────────────────────────────────────
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
            string plainPassword = GetPlainZipPassword();
            string zipFileName = Path.GetFileName(sourceZipPath);

            // ── Log: no target schema selected ──
            if (selectedSchemas == null || selectedSchemas.Count == 0)
            {
                LogService.LogNoTargetSchema(zipFileName);
                throw new Exception("No schemas selected for restore.");
            }

            // ── Derive branch name from the .7z filename ──
            // "roxas_2026_05_08.7z" → TrimDateSuffix("roxas_2026_05_08") → "roxas"
            string zipBaseName = Path.GetFileNameWithoutExtension(sourceZipPath);
            string branchName = TrimDateSuffix(zipBaseName);

            // Read what SQL files are inside the chosen .7z once
            List<string> zipEntries = GetDatabasesFromZip(sourceZipPath);

            // ── Log: selected schemas that have no matching file in the .7z ──
            foreach (string selected in selectedSchemas)
            {
                bool hasMatch = zipEntries.Exists(entry =>
                {
                    string schemaVar = "_" + ResolveSchemaName(entry, branchName);
                    return string.Equals(schemaVar, selected, StringComparison.OrdinalIgnoreCase);
                });

                if (!hasMatch)
                    LogService.LogNoBackupProvided(zipFileName, selected);
            }

            foreach (string zipEntry in zipEntries)
            {
                // ── Step 1: Resolve schema name ──
                // Convention A: sofos2_cmtinga_2026_05_11 → may date → trim date lang → "sofos2_cmtinga"
                // Convention B: sofos2_foroxas_roxas       → walang date → trim branch → "sofos2_foroxas"
                string trimmedName = ResolveSchemaName(zipEntry, branchName);
                string schemaVar = trimmedName;
                string schemaVarWithPrefix = "_" + trimmedName;

                // ── Step 2: Exact match — is this schema selected by the user? ──
                string matchedSchema = selectedSchemas.Find(s =>
                    string.Equals(s, schemaVarWithPrefix, StringComparison.OrdinalIgnoreCase));

                bool isSelected = matchedSchema != null;
                if (!isSelected)
                {
                    progressCallback?.Invoke(string.Format(
                        "Skipped: {0}  (schema {1} not selected)",
                        zipEntry, schemaVarWithPrefix));
                    result.Skipped.Add(schemaVarWithPrefix);
                    continue;
                }

                progressCallback?.Invoke(string.Format(
                    "Restoring: {0}  →  target schema: {1} ...",
                    zipEntry, schemaVarWithPrefix));

                string tempFolder = Path.Combine(
                    @"C:\Temp\MySQLRestore",
                    string.Format("restore_{0:N}", Guid.NewGuid()));
                Directory.CreateDirectory(tempFolder);

                try
                {
                    // ← original zipEntry (with date) para matamaan ang tamang file sa zip
                    string extractArgs = string.Format(
                        "e \"{0}\" -o\"{1}\" \"-p{2}\" \"{3}.sql\" -y",
                        sourceZipPath, tempFolder, plainPassword, zipEntry);

                    RunProcess(_config.SevenZipPath, extractArgs, "7-Zip extraction failed");

                    // ← original zipEntry din para sa path ng extracted file
                    string sqlFile = Path.Combine(tempFolder, zipEntry + ".sql");
                    if (!File.Exists(sqlFile))
                        throw new FileNotFoundException(string.Format(
                            "Could not find {0}.sql inside the archive.", zipEntry));

                    _config.Database = matchedSchema;
                    RestoreDatabase(sqlFile);

                    LogService.LogSuccess(zipFileName, schemaVar);
                    result.Restored.Add(schemaVar);
                }
                catch (Exception ex)
                {
                    LogService.LogFailure(schemaVar, ex.Message);
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

        // ────────────────────────────────────────────────────
        // PRIVATE: RESOLVE SCHEMA NAME FROM ZIP ENTRY
        // Convention A: may date suffix sa SQL → trim date lang
        //   "sofos2_cmtinga_2026_05_11" → "sofos2_cmtinga"
        // Convention B: walang date, may branch suffix → trim branch
        //   "sofos2_foroxas_roxas" → "sofos2_foroxas"
        // ────────────────────────────────────────────────────
        private static string ResolveSchemaName(string zipEntry, string branchName)
        {
            string dateTrimmed = TrimDateSuffix(zipEntry);

            // Kung nagbago after TrimDateSuffix → may date ang SQL filename
            // → date lang ang na-trim, huwag nang i-TrimBranchSuffix
            if (!string.Equals(dateTrimmed, zipEntry, StringComparison.OrdinalIgnoreCase))
                return dateTrimmed;

            // Kung hindi nagbago → walang date → i-trim ang branch suffix (dati convention)
            return TrimBranchSuffix(zipEntry, branchName);
        }

        // ────────────────────────────────────────────────────
        // PRIVATE: RUN 7zG WITH VISIBLE GUI PROGRESS WINDOW
        // ────────────────────────────────────────────────────
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

                System.Threading.Thread.Sleep(500);

                try
                {
                    process.WaitForInputIdle(2000);
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, 9);
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

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // ────────────────────────────────────────────────────
        // PRIVATE: DECRYPT ZIP PASSWORD FROM App.config
        // ────────────────────────────────────────────────────
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

        // ────────────────────────────────────────────────────
        // PRIVATE: RESTORE ONE SQL FILE INTO _config.Database
        // ────────────────────────────────────────────────────
        private void RestoreDatabase(string sqlFilePath)
        {
            string connStr = string.Format(
                "Server={0};Port={1};Uid={2};Password={3};",
                _config.Server, _config.Port, _config.UserId, _config.Password);

            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();

                using (var cmd = new MySqlCommand("SET GLOBAL max_allowed_packet=536870912;", conn))
                {
                    try { cmd.ExecuteNonQuery(); } catch { }
                }

                using (var cmd = new MySqlCommand(
                    string.Format(
                        "CREATE DATABASE IF NOT EXISTS `{0}`;", _config.Database), conn))
                    cmd.ExecuteNonQuery();
            }

            string cleanSqlPath = sqlFilePath + ".clean.sql";
            try
            {
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
                    "--host={0} --port={1} --user={2} --password={3}" +
                    " --max_allowed_packet=512M" +
                    " --connect_timeout=3600" +
                    " {4}",
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

                    var stdinTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            using (var fileStream = new FileStream(
                                cleanSqlPath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                bufferSize: 4 * 1024 * 1024))
                            {
                                fileStream.CopyTo(process.StandardInput.BaseStream);
                            }
                        }
                        finally
                        {
                            try { process.StandardInput.Close(); } catch { }
                        }
                    });

                    stdinTask.Wait();
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

        // ────────────────────────────────────────────────────
        // PRIVATE: RUN ANY EXTERNAL PROCESS (silent)
        // ────────────────────────────────────────────────────
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

        // ────────────────────────────────────────────────────
        // PRIVATE: TRIM DATE SUFFIX
        // "roxas_2026_05_08" → "roxas"
        // Walks backwards and drops consecutive all-digit segments.
        // ────────────────────────────────────────────────────
        private static string TrimDateSuffix(string entryName)
        {
            string[] parts = entryName.Split('_');
            int cutIndex = parts.Length;

            for (int i = parts.Length - 1; i >= 0; i--)
            {
                bool allDigits = true;
                foreach (char c in parts[i])
                    if (!char.IsDigit(c)) { allDigits = false; break; }

                if (allDigits && parts[i].Length > 0)
                    cutIndex = i;
                else
                    break;
            }

            return string.Join("_", parts, 0, cutIndex);
        }

        // ────────────────────────────────────────────────────
        // PRIVATE: TRIM BRANCH SUFFIX FROM SQL ENTRY NAME
        // branch = derived from .7z filename (e.g. "roxas")
        // "sofos2_foroxas_roxas" → "sofos2_foroxas"
        // "sofos2_roxas"         → "sofos2"
        // "yuu_roxas"            → "yuu"
        // ────────────────────────────────────────────────────
        private static string TrimBranchSuffix(string entryName, string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName)) return entryName;

            string suffix = "_" + branchName;
            if (entryName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return entryName.Substring(0, entryName.Length - suffix.Length);

            return entryName;
        }
    }
}