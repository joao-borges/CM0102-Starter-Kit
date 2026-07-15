using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static CM0102_Starter_Kit.Helper;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Built-in save game editor. Clubs tab: money is written to BOTH places the engine
    /// reads (finance ledger int64 + club Bank) and clamped to overflow-safe values.
    /// Players tab: attribute editing with the intrinsic/display conversion handled
    /// (see PlayerEditForm). Every write backs the save up first.
    /// </summary>
    class SaveEditorForm : Form {
        SaveGame save;
        ComboBox saveSelector;
        Label status;
        // clubs tab
        TextBox clubSearch;
        ListView clubList;
        TextBox balanceBox, bankBox;
        Button writeButton;
        SaveGame.Club selectedClub;
        readonly ListViewColumnSorter clubSorter = new ListViewColumnSorter();
        // players tab
        TextBox playerSearch;
        ComboBox playerClubFilter, playerNationFilter;
        ListView playerList;
        readonly ListViewColumnSorter playerSorter = new ListViewColumnSorter();
        readonly Action launchLegacyExplorer;

        public SaveEditorForm(Action launchLegacyExplorer) {
            this.launchLegacyExplorer = launchLegacyExplorer;
            this.Text = "Starter Kit Save Editor";
            this.Size = new Size(780, 560);
            this.MinimumSize = new Size(700, 460);
            this.StartPosition = FormStartPosition.CenterParent;
            BuildControls();
            RefreshSaveList();
        }

        void BuildControls() {
            Label saveLabel = new Label { Text = "Save game:", AutoSize = true, Location = new Point(12, 15) };
            this.saveSelector = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(85, 12), Width = 320
            };
            this.saveSelector.SelectedIndexChanged += (s, e) => LoadSelectedSave();

            TabControl tabs = new TabControl {
                Location = new Point(8, 42),
                Size = new Size(756, 440),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            TabPage clubsPage = new TabPage("Clubs");
            TabPage playersPage = new TabPage("Players");
            tabs.TabPages.Add(clubsPage);
            tabs.TabPages.Add(playersPage);
            tabs.SelectedIndexChanged += (s, e) => {
                if (tabs.SelectedTab == playersPage) EnsurePlayersLoaded();
            };

            BuildClubsPage(clubsPage);
            BuildPlayersPage(playersPage);

            this.status = new Label {
                Text = "", AutoSize = false, Location = new Point(12, 490), Size = new Size(750, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            this.Controls.AddRange(new Control[] { saveLabel, this.saveSelector, tabs, this.status });
        }

        void BuildClubsPage(TabPage page) {
            Label searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(10, 13) };
            this.clubSearch = new TextBox { Location = new Point(70, 10), Width = 300 };
            this.clubSearch.TextChanged += (s, e) => RefreshClubList();

            this.clubList = new ListView {
                View = View.Details, FullRowSelect = true, HideSelection = false,
                Location = new Point(10, 40), Size = new Size(460, 360),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
                MultiSelect = false
            };
            this.clubList.Columns.Add("Club", 240);
            this.clubList.Columns.Add("Balance", 100, HorizontalAlignment.Right);
            this.clubList.Columns.Add("Bank", 90, HorizontalAlignment.Right);
            this.clubList.SelectedIndexChanged += (s, e) => ShowSelectedClub();
            this.clubList.ListViewItemSorter = this.clubSorter;
            this.clubList.ColumnClick += (s, e) => this.clubSorter.HandleColumnClick(this.clubList, e.Column);

            GroupBox money = new GroupBox {
                Text = "Club money",
                Location = new Point(482, 40), Size = new Size(250, 190),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            money.Controls.Add(new Label { Text = "Balance (cash reserves):", AutoSize = true, Location = new Point(12, 28) });
            this.balanceBox = new TextBox { Location = new Point(15, 48), Width = 150 };
            money.Controls.Add(this.balanceBox);
            money.Controls.Add(new Label { Text = "Bank (drives transfer budget):", AutoSize = true, Location = new Point(12, 82) });
            this.bankBox = new TextBox { Location = new Point(15, 102), Width = 150 };
            money.Controls.Add(this.bankBox);
            money.Controls.Add(new Label {
                Text = "Safe range: 0 - 500,000,000.\nHigher values break the board's\nbudget maths (32-bit overflow).\nIn-game display converts currency,\nso the shown figure will differ.",
                AutoSize = true, Location = new Point(12, 130), ForeColor = Color.DimGray
            });

            this.writeButton = new Button {
                Text = "Apply && Save", Enabled = false,
                Location = new Point(482, 245), Size = new Size(250, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            this.writeButton.Click += (s, e) => ApplyClubAndSave();

            Button legacy = new Button {
                Text = "Anything else: open CM Explorer...",
                Location = new Point(482, 290), Size = new Size(250, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            legacy.Click += (s, e) => this.launchLegacyExplorer();

            page.Controls.AddRange(new Control[] { searchLabel, this.clubSearch, this.clubList, money, this.writeButton, legacy });
        }

        static ComboBox MakeFilterCombo(int x, int width) {
            ComboBox combo = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDown,
                Location = new Point(x, 10), Width = width
            };
            ComboBoxAutoComplete.Attach(combo);
            return combo;
        }

        void BuildPlayersPage(TabPage page) {
            Label searchLabel = new Label { Text = "Name:", AutoSize = true, Location = new Point(10, 13) };
            this.playerSearch = new TextBox { Location = new Point(58, 10), Width = 180 };
            this.playerSearch.TextChanged += (s, e) => RefreshPlayerList();
            Label clubLabel = new Label { Text = "Club:", AutoSize = true, Location = new Point(252, 13) };
            this.playerClubFilter = MakeFilterCombo(292, 190);
            this.playerClubFilter.TextChanged += (s, e) => RefreshPlayerList();
            Label nationLabel = new Label { Text = "Nation:", AutoSize = true, Location = new Point(496, 13) };
            this.playerNationFilter = MakeFilterCombo(548, 150);
            this.playerNationFilter.TextChanged += (s, e) => RefreshPlayerList();
            Label hint = new Label {
                Text = "Fill any filter (3+ letters; nation 2+), then double-click a player to edit. Click a column header to sort.",
                AutoSize = true, Location = new Point(10, 405), ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            this.playerList = new ListView {
                View = View.Details, FullRowSelect = true, HideSelection = false,
                Location = new Point(10, 40), Size = new Size(722, 360),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                MultiSelect = false
            };
            this.playerList.Columns.Add("Name", 190);
            this.playerList.Columns.Add("Age", 42, HorizontalAlignment.Right);
            this.playerList.Columns.Add("Club", 180);
            this.playerList.Columns.Add("Nation", 110);
            this.playerList.Columns.Add("CA", 46, HorizontalAlignment.Right);
            this.playerList.Columns.Add("PA", 46, HorizontalAlignment.Right);
            this.playerList.Columns.Add("Value", 95, HorizontalAlignment.Right);
            this.playerList.DoubleClick += (s, e) => EditSelectedPlayer();
            this.playerList.ListViewItemSorter = this.playerSorter;
            this.playerList.ColumnClick += (s, e) => this.playerSorter.HandleColumnClick(this.playerList, e.Column);

            page.Controls.AddRange(new Control[] { searchLabel, this.playerSearch, clubLabel, this.playerClubFilter, nationLabel, this.playerNationFilter, hint, this.playerList });
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
            // Exact name only - "cm0102" is the game; matching by prefix would also
            // catch the Starter Kit's own process (CM0102StarterKit)
            return Process.GetProcessesByName("cm0102").Length > 0;
        }

        void LoadSelectedSave() {
            try {
                Cursor = Cursors.WaitCursor;
                this.save = new SaveGame(Path.Combine(GameFolder, (string) this.saveSelector.SelectedItem));
                this.save.Load();
                this.status.Text = this.save.Clubs.Count.ToString("N0") + " clubs loaded from " + this.save.FileName + ".";
                PopulateClubFilter();
                this.playerNationFilter.Items.Clear();
                RefreshClubList();
                RefreshPlayerList();
            } catch (Exception exception) {
                this.save = null;
                this.clubList.Items.Clear();
                this.playerList.Items.Clear();
                this.status.Text = exception.Message;
            } finally {
                Cursor = Cursors.Default;
            }
        }

        void PopulateClubFilter() {
            List<string> names = new List<string>();
            foreach (SaveGame.Club club in this.save.Clubs) {
                names.Add(club.LongName);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            this.playerClubFilter.Items.Clear();
            this.playerClubFilter.Items.AddRange(names.ToArray());
            this.playerClubFilter.Text = "";
        }

        void EnsurePlayersLoaded() {
            if (this.save == null || this.save.Players.Count > 0) return;
            try {
                Cursor = Cursors.WaitCursor;
                this.save.LoadPlayers();
                this.playerNationFilter.Items.Clear();
                foreach (SaveGame.Nation nation in this.save.Nations) {
                    this.playerNationFilter.Items.Add(nation.Name);
                }
                this.status.Text = this.save.Players.Count.ToString("N0") + " players indexed.";
                RefreshPlayerList();
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            } finally {
                Cursor = Cursors.Default;
            }
        }

        void RefreshClubList() {
            this.clubList.BeginUpdate();
            this.clubList.Items.Clear();
            if (this.save != null) {
                string needle = this.clubSearch.Text.Trim().ToLower();
                foreach (SaveGame.Club club in this.save.Clubs) {
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

        void RefreshPlayerList() {
            this.playerList.BeginUpdate();
            this.playerList.Items.Clear();
            if (this.save != null && this.save.Players.Count > 0) {
                string name = this.playerSearch.Text.Trim().ToLower();
                string club = this.playerClubFilter.Text.Trim().ToLower();
                string nation = this.playerNationFilter.Text.Trim().ToLower();
                if (name.Length >= 3 || club.Length >= 3 || nation.Length >= 2) {
                    foreach (SaveGame.PlayerRef player in this.save.Players) {
                        if (name.Length > 0 && !player.Name.ToLower().Contains(name)) continue;
                        if (club.Length > 0 && !player.ClubName.ToLower().Contains(club)) continue;
                        if (nation.Length > 0 && !player.Nation.ToLower().Contains(nation)) continue;
                        ListViewItem item = new ListViewItem(player.Name);
                        item.SubItems.Add(player.Age.ToString());
                        item.SubItems.Add(player.ClubName);
                        item.SubItems.Add(player.Nation);
                        item.SubItems.Add(this.save.ReadInt16(player.PlayerBase + 5).ToString());
                        item.SubItems.Add(this.save.ReadInt16(player.PlayerBase + 7).ToString());
                        item.SubItems.Add(this.save.ReadInt32(player.StaffBase + 82).ToString("N0"));
                        item.Tag = player;
                        this.playerList.Items.Add(item);
                        if (this.playerList.Items.Count >= 400) break;
                    }
                }
            }
            this.playerList.EndUpdate();
        }

        void ShowSelectedClub() {
            if (this.clubList.SelectedItems.Count == 0) return;
            this.selectedClub = (SaveGame.Club) this.clubList.SelectedItems[0].Tag;
            this.balanceBox.Text = this.selectedClub.Balance.ToString();
            this.bankBox.Text = this.selectedClub.Bank.ToString();
            this.writeButton.Enabled = true;
        }

        bool BlockedByRunningGame() {
            if (GameIsRunning()) {
                MessageBox.Show(this, "The game is running. Exit CM 01/02 first, otherwise " +
                    "its next save would overwrite this edit.", "Save Editor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }
            return false;
        }

        void ApplyClubAndSave() {
            if (this.save == null || this.selectedClub == null || BlockedByRunningGame()) return;
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

        void EditSelectedPlayer() {
            if (this.save == null || this.playerList.SelectedItems.Count == 0) return;
            SaveGame.PlayerRef player = (SaveGame.PlayerRef) this.playerList.SelectedItems[0].Tag;
            using (PlayerEditForm editor = new PlayerEditForm(this.save, player)) {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
            }
            if (BlockedByRunningGame()) {
                this.status.Text = "Edit NOT saved - game running. Reopen the editor after exiting the game.";
                return;
            }
            try {
                string backup = this.save.Save();
                this.status.Text = player.Name + " updated. Backup: " + Path.GetFileName(backup);
                RefreshPlayerList();
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            }
        }
    }
}
