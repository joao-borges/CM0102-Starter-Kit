using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static CM0102_Starter_Kit.Helper;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Built-in save game editor (phase 1: club finances). Replaces CM Explorer for the
    /// edits that used to corrupt budgets: money is written to BOTH places the engine
    /// reads (finance ledger int64 + club Bank) and clamped to overflow-safe values.
    /// </summary>
    class SaveEditorForm : Form {
        SaveGame save;
        ComboBox saveSelector;
        TextBox search;
        ListView clubList;
        TextBox balanceBox, bankBox;
        Label status;
        Button writeButton;
        SaveGame.Club selectedClub;
        readonly Action launchLegacyExplorer;

        public SaveEditorForm(Action launchLegacyExplorer) {
            this.launchLegacyExplorer = launchLegacyExplorer;
            this.Text = "Starter Kit Save Editor - Clubs";
            this.Size = new Size(760, 540);
            this.MinimumSize = new Size(640, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            BuildControls();
            RefreshSaveList();
        }

        void BuildControls() {
            Label saveLabel = new Label { Text = "Save game:", AutoSize = true, Location = new Point(12, 15) };
            this.saveSelector = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(85, 12), Width = 320,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            this.saveSelector.SelectedIndexChanged += (s, e) => LoadSelectedSave();

            Label searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(12, 47) };
            this.search = new TextBox { Location = new Point(85, 44), Width = 320 };
            this.search.TextChanged += (s, e) => RefreshClubList();

            this.clubList = new ListView {
                View = View.Details, FullRowSelect = true, HideSelection = false,
                Location = new Point(12, 75), Size = new Size(460, 380),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
                VirtualMode = false, MultiSelect = false
            };
            this.clubList.Columns.Add("Club", 240);
            this.clubList.Columns.Add("Balance", 100, HorizontalAlignment.Right);
            this.clubList.Columns.Add("Bank", 90, HorizontalAlignment.Right);
            this.clubList.SelectedIndexChanged += (s, e) => ShowSelectedClub();

            GroupBox money = new GroupBox {
                Text = "Club money",
                Location = new Point(485, 75), Size = new Size(250, 190),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            money.Controls.Add(new Label { Text = "Balance (cash reserves):", AutoSize = true, Location = new Point(12, 28) });
            this.balanceBox = new TextBox { Location = new Point(15, 48), Width = 150 };
            money.Controls.Add(this.balanceBox);
            money.Controls.Add(new Label { Text = "Bank (drives transfer budget):", AutoSize = true, Location = new Point(12, 82) });
            this.bankBox = new TextBox { Location = new Point(15, 102), Width = 150 };
            money.Controls.Add(this.bankBox);
            money.Controls.Add(new Label {
                Text = "Safe range: 0 - 500,000,000.\nHigher values break the board's\nbudget maths (32-bit overflow).",
                AutoSize = true, Location = new Point(12, 135), ForeColor = Color.DimGray
            });

            this.writeButton = new Button {
                Text = "Apply && Save", Enabled = false,
                Location = new Point(485, 280), Size = new Size(250, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            this.writeButton.Click += (s, e) => ApplyAndSave();

            this.status = new Label {
                Text = "", AutoSize = false, Location = new Point(12, 462), Size = new Size(723, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            Button legacy = new Button {
                Text = "Players && staff: open CM Explorer...",
                Location = new Point(485, 330), Size = new Size(250, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            legacy.Click += (s, e) => this.launchLegacyExplorer();

            this.Controls.AddRange(new Control[] { saveLabel, this.saveSelector, searchLabel, this.search, this.clubList, money, this.writeButton, legacy, this.status });
        }

        void RefreshSaveList() {
            this.saveSelector.Items.Clear();
            if (Directory.Exists(GameFolder)) {
                foreach (string file in Directory.GetFiles(GameFolder, "*.sav")) {
                    this.saveSelector.Items.Add(Path.GetFileName(file));
                }
            }
            if (this.saveSelector.Items.Count > 0) {
                this.saveSelector.SelectedIndex = 0;
            } else {
                this.status.Text = "No save games found in the Game folder.";
            }
        }

        static bool GameIsRunning() {
            return Process.GetProcesses().Any(p => {
                try { return p.ProcessName.ToLower().StartsWith("cm0102"); }
                catch { return false; }
            });
        }

        void LoadSelectedSave() {
            try {
                this.save = new SaveGame(Path.Combine(GameFolder, (string) this.saveSelector.SelectedItem));
                this.save.Load();
                this.status.Text = this.save.Clubs.Count.ToString("N0") + " clubs loaded from " + this.save.FileName + ".";
                RefreshClubList();
            } catch (Exception exception) {
                this.save = null;
                this.clubList.Items.Clear();
                this.status.Text = exception.Message;
            }
        }

        void RefreshClubList() {
            this.clubList.BeginUpdate();
            this.clubList.Items.Clear();
            if (this.save != null) {
                string needle = this.search.Text.Trim().ToLower();
                foreach (SaveGame.Club club in this.save.Clubs) {
                    if (needle.Length < 2 && needle.Length > 0) continue;
                    if (needle.Length == 0 || club.LongName.ToLower().Contains(needle) || club.ShortName.ToLower().Contains(needle)) {
                        ListViewItem item = new ListViewItem(club.LongName);
                        item.SubItems.Add(club.Balance.ToString("N0"));
                        item.SubItems.Add(club.Bank.ToString("N0"));
                        item.Tag = club;
                        this.clubList.Items.Add(item);
                        if (this.clubList.Items.Count >= 400) break;
                    }
                }
            }
            this.clubList.EndUpdate();
            this.writeButton.Enabled = false;
            this.selectedClub = null;
        }

        void ShowSelectedClub() {
            if (this.clubList.SelectedItems.Count == 0) return;
            this.selectedClub = (SaveGame.Club) this.clubList.SelectedItems[0].Tag;
            this.balanceBox.Text = this.selectedClub.Balance.ToString();
            this.bankBox.Text = this.selectedClub.Bank.ToString();
            this.writeButton.Enabled = true;
        }

        void ApplyAndSave() {
            if (this.save == null || this.selectedClub == null) return;
            if (GameIsRunning()) {
                MessageBox.Show(this, "The game is running. Exit CM 01/02 first, otherwise " +
                    "its next save would overwrite this edit.", "Save Editor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            long balance;
            int bank;
            if (!long.TryParse(this.balanceBox.Text.Replace(",", "").Replace(".", ""), out balance) ||
                !int.TryParse(this.bankBox.Text.Replace(",", "").Replace(".", ""), out bank)) {
                this.status.Text = "Enter plain numbers (no currency symbols).";
                return;
            }
            try {
                this.save.SetClubMoney(this.selectedClub, balance, bank);
                string backup = this.save.Save();
                this.status.Text = this.selectedClub.LongName + " updated. Backup: " + Path.GetFileName(backup);
                this.clubList.SelectedItems[0].SubItems[1].Text = balance.ToString("N0");
                this.clubList.SelectedItems[0].SubItems[2].Text = bank.ToString("N0");
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            }
        }
    }
}
