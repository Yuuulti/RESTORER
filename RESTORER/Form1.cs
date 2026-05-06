using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using RESTORER.Controller;

namespace RESTORER
{
    public partial class Form1 : Form
    {
        // ── FIELDS ──────────────────────────────────────────────────────────
        private RestoreController _controller;

        // ── CONSTRUCTOR ──────────────────────────────────────────────────────
        public Form1()
        {
            InitializeComponent();
        }

        // ── FORM LOAD ────────────────────────────────────────────────────────
        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                _controller = new RestoreController();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Initialization error:\n\n" + ex.Message,
                    "RESTORER - Config Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // textBox1: "Select Database File" - read-only label, no highlight
            textBox1.ReadOnly = true;
            textBox1.TabStop = false;
            textBox1.BackColor = System.Drawing.SystemColors.Control;
            textBox1.BorderStyle = BorderStyle.None;
            textBox1.Cursor = Cursors.Default;

            // textBox2: pre-fill with the latest .7z found in backup env var
            textBox2.ReadOnly = false;
            textBox2.Text = string.Empty;
            try
            {
                string backupDir = _controller.GetConfig().BackupDir;
                if (!string.IsNullOrWhiteSpace(backupDir) &&
                    Directory.Exists(backupDir))
                {
                    // Find the most recently modified .7z; fall back to .zip
                    string[] files = Directory.GetFiles(backupDir, "*.7z");
                    if (files.Length == 0)
                        files = Directory.GetFiles(backupDir, "*.zip");

                    if (files.Length > 0)
                    {
                        string latest = files[0];
                        DateTime latestTime = File.GetLastWriteTime(latest);
                        foreach (string f in files)
                        {
                            DateTime t = File.GetLastWriteTime(f);
                            if (t > latestTime) { latestTime = t; latest = f; }
                        }
                        textBox2.Text = latest;
                    }
                }
            }
            catch
            {
                // Non-fatal — user can still browse manually
            }

            // Load zip contents into listBox1 if a file was pre-filled
            if (!string.IsNullOrWhiteSpace(textBox2.Text))
                LoadZipContents(textBox2.Text);

            // textBox3: "Target Schema" - read-only label, no highlight
            textBox3.ReadOnly = true;
            textBox3.TabStop = false;
            textBox3.BackColor = System.Drawing.SystemColors.Control;
            textBox3.BorderStyle = BorderStyle.None;
            textBox3.Cursor = Cursors.Default;

            // Move initial focus to button so no textbox gets highlighted
            this.ActiveControl = button1;

            // Load all server schemas into the list on startup
            await LoadServerSchemasAsync();
        }

        // ── LOAD ZIP CONTENTS INTO listBox1 ──────────────────────────────────
        private void LoadZipContents(string zipPath)
        {
            listBox1.Items.Clear();
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) return;

            try
            {
                List<string> entries = _controller.GetDatabasesFromZip(zipPath);
                if (entries.Count == 0)
                {
                    listBox1.Items.Add("(no .sql files found in archive)");
                    return;
                }
                foreach (var entry in entries)
                    listBox1.Items.Add(entry + ".sql");
            }
            catch (Exception ex)
            {
                listBox1.Items.Add("Error reading archive: " + ex.Message);
            }
        }

        // ── LOAD ALL SERVER SCHEMAS ON STARTUP ───────────────────────────────
        private async Task LoadServerSchemasAsync()
        {
            SetUiBusy(true, "Loading schemas...");

            try
            {
                List<string> schemas = await Task.Run(() =>
                    _controller.GetServerDatabases());

                checkedListBox1.Items.Clear();
                checkBox1.Checked = false;

                if (schemas.Count == 0)
                {
                    MessageBox.Show(
                        "No schemas found on the MySQL server.",
                        "RESTORER",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                foreach (var schema in schemas)
                    checkedListBox1.Items.Add(schema, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to connect to MySQL server:\n\n" + ex.Message,
                    "RESTORER - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // ── BUTTON 1 - Browse for zip file ───────────────────────────────────
        // designer event: button1_Click_1
        private void button1_Click_1(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select Backup File";
                dlg.Filter =
                    "7-Zip Archive (*.7z)|*.7z|Zip Archive (*.zip)|*.zip|All Files (*.*)|*.*";

                // Open dialog starting from backup dir if available
                try
                {
                    string backupDir = _controller?.GetConfig()?.BackupDir;
                    if (!string.IsNullOrWhiteSpace(backupDir) &&
                        Directory.Exists(backupDir))
                        dlg.InitialDirectory = backupDir;
                }
                catch { }

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    textBox2.Text = dlg.FileName;
                    LoadZipContents(dlg.FileName);
                }
            }
        }

        // ── SELECT ALL ───────────────────────────────────────────────────────
        // designer event: checkBox1_CheckedChanged
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < checkedListBox1.Items.Count; i++)
                checkedListBox1.SetItemChecked(i, checkBox1.Checked);
        }

        // ── CHECKED LIST BOX SELECTION ───────────────────────────────────────
        // designer event: checkedListBox1_SelectedIndexChanged
        private void checkedListBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (checkedListBox1.SelectedItem == null) return;
            textBox3.Text = checkedListBox1.SelectedItem.ToString();
        }

        // ── IMPORT (RESTORE) ─────────────────────────────────────────────────
        // designer event: button2_Click_1
        private async void button2_Click_1(object sender, EventArgs e)
        {
            if (_controller == null)
            {
                MessageBox.Show(
                    "Controller not initialized. Check environment variables.",
                    "RESTORER",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Validate zip file selected
            string zipPath = textBox2.Text.Trim();
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                MessageBox.Show(
                    "Please select a valid backup file first.",
                    "RESTORER",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Collect checked schemas from the server list
            var selected = new List<string>();
            foreach (var item in checkedListBox1.CheckedItems)
                selected.Add(item.ToString());

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Please select at least one schema to restore.",
                    "RESTORER",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Confirm before proceeding
            string schemaList = "  - " + string.Join("\n  - ", selected);
            var confirm = MessageBox.Show(
                string.Format(
                    "You are about to restore {0} schema(s):\n\n{1}\n\n" +
                    "Only schemas that exist inside the backup file will be restored.\n" +
                    "Each schema restores into its matching database on the server.\n\n" +
                    "Proceed?",
                    selected.Count, schemaList),
                "RESTORER - Confirm Restore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetUiBusy(true, "Restoring...");

            try
            {
                RestoreResult result = await Task.Run(() =>
                    _controller.RestoreSelected(
                        zipPath,
                        selected,
                        msg => Invoke(new Action(() => SetUiBusy(true, msg)))));

                string summary = string.Format(
                    "Restore completed!\n\nRestored ({0}):\n  - {1}",
                    result.Restored.Count,
                    result.Restored.Count > 0
                        ? string.Join("\n  - ", result.Restored)
                        : "(none)");

                if (result.Skipped.Count > 0)
                    summary += string.Format(
                        "\n\nSkipped - not found in backup ({0}):\n  - {1}",
                        result.Skipped.Count,
                        string.Join("\n  - ", result.Skipped));

                MessageBox.Show(
                    summary,
                    "RESTORER - Done",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                // Uncheck all after success
                for (int i = 0; i < checkedListBox1.Items.Count; i++)
                    checkedListBox1.SetItemChecked(i, false);
                checkBox1.Checked = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Restore failed:\n\n" + ex.Message,
                    "RESTORER - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetUiBusy(false);
            }
        }

        // ── CANCEL ───────────────────────────────────────────────────────────
        // designer event: button3_Click
        private void button3_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        // ── STUBS required by designer event wiring ──────────────────────────
        private void textBox1_TextChanged(object sender, EventArgs e) { }
        private void textBox2_TextChanged(object sender, EventArgs e) { }
        private void textBox3_TextChanged(object sender, EventArgs e) { }

        // ── UI BUSY STATE ────────────────────────────────────────────────────
        private void SetUiBusy(bool busy, string statusText = null)
        {
            button1.Enabled = !busy;
            button2.Enabled = !busy;
            button3.Enabled = !busy;
            checkBox1.Enabled = !busy;
            checkedListBox1.Enabled = !busy;

            this.Text = busy && statusText != null
                ? "RESTORER - " + statusText
                : "RESTORER";
            this.Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }
    }
}