using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RESTORER.Controller;

namespace RESTORER
{
    public partial class Form1 : Form
    {
        private RestoreController _controller;
        private CancellationTokenSource _cts;
        private System.Threading.Timer _scheduleTimer;

        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                _controller = new RestoreController();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Initialization error:\n\n" + ex.Message,
                    "RESTORER - Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            textBox1.ReadOnly = true;
            textBox1.TabStop = false;
            textBox1.BackColor = System.Drawing.SystemColors.Control;
            textBox1.BorderStyle = BorderStyle.None;
            textBox1.Cursor = Cursors.Default;

            textBox2.ReadOnly = true;
            textBox2.Text = @"D:\backup";

            textBox3.ReadOnly = true;
            textBox3.TabStop = false;
            textBox3.BackColor = System.Drawing.SystemColors.Control;
            textBox3.BorderStyle = BorderStyle.None;
            textBox3.Cursor = Cursors.Default;

            comboBox1.Items.AddRange(new string[] { "Once", "Daily", "Weekly", "Monthly" });
            comboBox1.SelectedIndex = 0;

            dateTimePicker1.Value = DateTime.Now.Date;
            dateTimePicker2.Value = DateTime.Today.AddHours(2);

            checkBox2.CheckedChanged += checkBox2_CheckedChanged;
            comboBox1.SelectedIndexChanged += Schedule_Changed;
            dateTimePicker1.ValueChanged += Schedule_Changed;
            dateTimePicker2.ValueChanged += Schedule_Changed;

            ToggleScheduleControls(false);

            progressBar1.Minimum = 0;
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;
            label8.Text = "";
            label9.Text = "0%";

            button3.Enabled = false;

            LoadBackupZipFiles();
            await LoadServerSchemasAsync();

            checkedListBox1.SelectionMode = SelectionMode.None;
        }

        // ════════════════════════════════════════════════════════════════════
        // SCHEDULE
        // ════════════════════════════════════════════════════════════════════

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            ToggleScheduleControls(checkBox2.Checked);
            UpdateNextRunLabel();
        }

        private void ToggleScheduleControls(bool enabled)
        {
            comboBox1.Enabled = enabled;
            dateTimePicker1.Enabled = enabled;
            dateTimePicker2.Enabled = enabled;
            label4.Enabled = enabled;
            if (enabled) AdjustDateVisibility();
        }

        private void AdjustDateVisibility()
        {
            bool needsDate = comboBox1.SelectedItem?.ToString() == "Once";
            dateTimePicker1.Visible = needsDate;
            label2.Visible = needsDate;
        }

        private void Schedule_Changed(object sender, EventArgs e)
        {
            AdjustDateVisibility();
            UpdateNextRunLabel();
        }

        private void UpdateNextRunLabel()
        {
            if (!checkBox2.Checked) { label4.Text = "Next run: -"; return; }

            string freq = comboBox1.SelectedItem?.ToString() ?? "Once";
            TimeSpan time = dateTimePicker2.Value.TimeOfDay;

            switch (freq)
            {
                case "Once":
                    DateTime scheduled = dateTimePicker1.Value.Date + time;
                    label4.Text = scheduled > DateTime.Now
                        ? "Next run: " + scheduled.ToString("MMM dd, yyyy hh:mm tt")
                        : "⚠ Time is in the past!";
                    break;
                case "Daily":
                    label4.Text = "Next run: Every day at " + DateTime.Today.Add(time).ToString("hh:mm tt");
                    break;
                case "Weekly":
                    label4.Text = "Next run: Every week at " + DateTime.Today.Add(time).ToString("hh:mm tt");
                    break;
                case "Monthly":
                    label4.Text = "Next run: Every month at " + DateTime.Today.Add(time).ToString("hh:mm tt");
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // LOAD .7z FILES
        // ════════════════════════════════════════════════════════════════════

        private void LoadBackupZipFiles()
        {
            listBox1.Items.Clear();
            try
            {
                List<string> zipFiles = _controller.GetBackupZipFiles();

                if (zipFiles.Count == 0)
                {
                    listBox1.Items.Add("");
                    return;
                }

                foreach (string fullPath in zipFiles)
                    listBox1.Items.Add(Path.GetFileName(fullPath));

                // Auto-select first file so user doesn't need to click
                if (listBox1.Items.Count > 0)
                    listBox1.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                listBox1.Items.Add("Error: " + ex.Message);
            }
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e) { }

        // ════════════════════════════════════════════════════════════════════
        // LOAD SERVER SCHEMAS
        // ════════════════════════════════════════════════════════════════════

        private async Task LoadServerSchemasAsync()
        {
            SetUiBusy(true, "Loading schemas...");
            try
            {
                List<string> schemas = await Task.Run(() => _controller.GetServerDatabases());
                checkedListBox1.Items.Clear();
                checkBox1.Checked = false;

                if (schemas.Count == 0)
                {
                    MessageBox.Show("No schemas found on the MySQL server.",
                        "RESTORER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (var schema in schemas)
                    checkedListBox1.Items.Add(schema, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to connect to MySQL server:\n\n" + ex.Message,
                    "RESTORER - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // BROWSE BUTTON
        // ════════════════════════════════════════════════════════════════════

        private void button1_Click_1(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select Backup File";
                dlg.Filter = "7-Zip Archive (*.7z)|*.7z|All Files (*.*)|*.*";
                dlg.InitialDirectory = @"D:\backup";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string fileName = Path.GetFileName(dlg.FileName);
                    int idx = listBox1.Items.IndexOf(fileName);
                    if (idx >= 0)
                    {
                        listBox1.SelectedIndex = idx;
                    }
                    else
                    {
                        listBox1.Items.Add(fileName);
                        listBox1.SelectedIndex = listBox1.Items.Count - 1;
                        listBox1.Tag = dlg.FileName;
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECT ALL
        // ════════════════════════════════════════════════════════════════════

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < checkedListBox1.Items.Count; i++)
                checkedListBox1.SetItemChecked(i, checkBox1.Checked);
        }

        private void checkedListBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (checkedListBox1.SelectedItem == null) return;
            textBox3.Text = checkedListBox1.SelectedItem.ToString();
        }

        // ════════════════════════════════════════════════════════════════════
        // IMPORT BUTTON
        // ════════════════════════════════════════════════════════════════════

        private async void button2_Click_1(object sender, EventArgs e)
        {
            if (_controller == null)
            {
                MessageBox.Show("Controller not initialized.",
                    "RESTORER", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (checkBox2.Checked) { SetScheduleTimer(); return; }

            await RunRestoreAsync(skipConfirm: false);
        }

        // ════════════════════════════════════════════════════════════════════
        // AUTO SCHEDULE TIMER
        // ════════════════════════════════════════════════════════════════════

        private void SetScheduleTimer()
        {
            string freq = comboBox1.SelectedItem?.ToString() ?? "Once";
            TimeSpan time = dateTimePicker2.Value.TimeOfDay;
            DateTime now = DateTime.Now;
            DateTime firstRun;
            long intervalMs;

            switch (freq)
            {
                case "Once":
                    firstRun = dateTimePicker1.Value.Date + time;
                    intervalMs = Timeout.Infinite;
                    break;
                case "Daily":
                    firstRun = DateTime.Today.Add(time);
                    if (firstRun <= now) firstRun = firstRun.AddDays(1);
                    intervalMs = (long)TimeSpan.FromDays(1).TotalMilliseconds;
                    break;
                case "Weekly":
                    firstRun = DateTime.Today.Add(time);
                    if (firstRun <= now) firstRun = firstRun.AddDays(7);
                    intervalMs = (long)TimeSpan.FromDays(7).TotalMilliseconds;
                    break;
                case "Monthly":
                    firstRun = new DateTime(now.Year, now.Month, 1).Add(time).AddMonths(1);
                    intervalMs = Timeout.Infinite;
                    break;
                default:
                    firstRun = DateTime.Today.Add(time);
                    intervalMs = Timeout.Infinite;
                    break;
            }

            if (firstRun <= now && freq == "Once")
            {
                MessageBox.Show("Selected schedule time is already in the past.\nPlease pick a future date/time.",
                    "Invalid Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            long delayMs = (long)(firstRun - now).TotalMilliseconds;
            _scheduleTimer?.Dispose();

            _scheduleTimer = new System.Threading.Timer(
                async _ =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(async () => await RunRestoreAsync(skipConfirm: true)));
                    else
                        await RunRestoreAsync(skipConfirm: true);

                    if (freq == "Monthly") SetScheduleTimer();
                },
                null, delayMs, intervalMs);

            label4.Text = "Next run: " + firstRun.ToString("MMM dd, yyyy hh:mm tt");
            label8.Text = "Scheduled";
            MessageBox.Show("Restore scheduled!\n\nNext run: " + firstRun.ToString("MMM dd, yyyy hh:mm tt"),
                "RESTORER - Scheduled", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ════════════════════════════════════════════════════════════════════
        // RESOLVE ZIP PATH
        // ════════════════════════════════════════════════════════════════════

        private string GetSelectedZipPath()
        {
            if (listBox1.SelectedItem == null) return null;
            string selected = listBox1.SelectedItem.ToString();
            if (selected.StartsWith("(") || selected.StartsWith("Error")) return null;

            if (listBox1.Tag is string tagPath &&
                Path.GetFileName(tagPath).Equals(selected, StringComparison.OrdinalIgnoreCase))
                return tagPath;

            return Path.Combine(@"D:\backup", selected);
        }

        // ════════════════════════════════════════════════════════════════════
        // CORE RESTORE LOGIC
        // ════════════════════════════════════════════════════════════════════

        private async Task RunRestoreAsync(bool skipConfirm = false)
        {
            string zipPath = GetSelectedZipPath();

            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                MessageBox.Show("Please select a valid backup file from the list.",
                    "RESTORER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedSchemas = new List<string>();

            if (skipConfirm)
            {
                // Scheduled run — auto-use ALL schemas from the server list
                foreach (var item in checkedListBox1.Items)
                    selectedSchemas.Add(item.ToString());
            }
            else
            {
                foreach (var item in checkedListBox1.CheckedItems)
                    selectedSchemas.Add(item.ToString());
            }

            if (selectedSchemas.Count == 0)
            {
                string zipFileName = Path.GetFileName(zipPath);
                RESTORER.Services.LogService.LogNoTargetSchema(zipFileName);
                MessageBox.Show("Please check at least one schema from the Target Schema list.",
                    "RESTORER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!skipConfirm)
            {
                List<string> zipEntries;
                try
                {
                    zipEntries = _controller.GetDatabasesFromZip(zipPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not read backup file:\n\n" + ex.Message,
                        "RESTORER - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Derive branch from .7z filename: "roxas_2026_05_08" → "roxas"
                string branchName = TrimDateSuffix(Path.GetFileNameWithoutExtension(zipPath));

                // Match zip entries to selected schemas
                var toRestore = new List<string>();
                foreach (string entry in zipEntries)
                {
                    // "sofos2_foroxas_roxas" → trim "_roxas" → "sofos2_foroxas" → "_sofos2_foroxas"
                    // BAGO (fixed):
                    string schemaVar = "_" + TrimBranchSuffix(entry, branchName);
                    if (selectedSchemas.Exists(s =>
                        string.Equals(s, schemaVar, StringComparison.OrdinalIgnoreCase)))
                        toRestore.Add(schemaVar);
                }

                if (toRestore.Count == 0)
                {
                    MessageBox.Show(
                        "None of the selected schemas match any SQL file inside the backup.\n\nPlease check your selection.",
                        "RESTORER - Nothing to Restore", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string schemaList = "  • " + string.Join("\n  • ", toRestore);
                // BAGO:
                var confirm = MessageBox.Show(
                    string.Format("You are about to restore from:\n{0}\n\nAre you sure you want to restore?\n\nProceed?",
                        Path.GetFileName(zipPath)),
                    "RESTORER - Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;
            }

            progressBar1.Value = 0;
            label8.Text = "Restoring...";
            label9.Text = "0%";
            SetUiBusy(true, "Restoring...");
            button3.Enabled = true;

            _cts = new CancellationTokenSource();
            RestoreResult result = null;

            try
            {
                int step = 0;
                await Task.Run(() =>
                {
                    result = _controller.RestoreSelected(zipPath, selectedSchemas, msg =>
                    {
                        if (_cts.Token.IsCancellationRequested) return;
                        step++;
                        int pct = Math.Min(step * 10, 99);
                        Invoke(new Action(() => SetProgress(pct, msg)));
                    });
                }, _cts.Token);

                SetProgress(100, "Complete");
                label8.Text = "Complete";

                for (int i = 0; i < checkedListBox1.Items.Count; i++)
                    checkedListBox1.SetItemChecked(i, false);
                checkBox1.Checked = false;

                int restoredCount = result?.Restored?.Count ?? 0;
                string restoredList = restoredCount > 0
                    ? "  • " + string.Join("\n  • ", result.Restored)
                    : "  (none)";

                // BAGO:
                MessageBox.Show(
                    "Restore completed!",
                    "RESTORER - Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                label8.Text = "Cancelled";
                label9.Text = "0%";
                progressBar1.Value = 0;
                MessageBox.Show("Restore was cancelled.", "RESTORER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                label8.Text = "Error";
                MessageBox.Show("Restore failed:\n\n" + ex.Message,
                    "RESTORER - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetUiBusy(false);
                button3.Enabled = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════

        // "roxas_2026_05_08" → "roxas"
        private static string TrimDateSuffix(string name)
        {
            string[] parts = name.Split('_');
            int cutIndex = parts.Length;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                bool allDigits = true;
                foreach (char c in parts[i])
                    if (!char.IsDigit(c)) { allDigits = false; break; }
                if (allDigits && parts[i].Length > 0) cutIndex = i;
                else break;
            }
            return string.Join("_", parts, 0, cutIndex);
        }

        // "sofos2_foroxas_roxas" + branch "roxas" → "sofos2_foroxas"
        private static string TrimBranchSuffix(string entryName, string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName)) return entryName;
            string suffix = "_" + branchName;
            if (entryName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return entryName.Substring(0, entryName.Length - suffix.Length);
            return entryName;
        }

        private void SetProgress(int pct, string status = null)
        {
            progressBar1.Value = Math.Min(pct, 100);
            label9.Text = pct + "%";
            if (status != null) label8.Text = status;
        }

        private void SetUiBusy(bool busy, string statusText = null)
        {
            button1.Enabled = !busy;
            button2.Enabled = !busy;
            checkBox1.Enabled = !busy;
            checkedListBox1.Enabled = !busy;
            listBox1.Enabled = !busy;
            checkBox2.Enabled = !busy;
            this.Text = busy && statusText != null ? "RESTORER - " + statusText : "RESTORER";
            this.Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _scheduleTimer?.Dispose();
            _cts?.Cancel();
            base.OnFormClosing(e);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            _scheduleTimer?.Dispose();
            label8.Text = "Cancelled";
            button3.Enabled = false;
        }

        private void textBox1_TextChanged(object sender, EventArgs e) { }
        private void textBox2_TextChanged(object sender, EventArgs e) { }
        private void textBox3_TextChanged(object sender, EventArgs e) { }
        private void groupBox1_Enter(object sender, EventArgs e) { }
        private void label8_Click(object sender, EventArgs e) { }

        private void label9_Click(object sender, EventArgs e)
        {

        }
    }
}