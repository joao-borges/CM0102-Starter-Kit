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
    /// The lists are DataGridViews on purpose: the native ListView misbehaves under
    /// Wine (scrollbar never appears / scroll range goes stale), while DataGridView
    /// paints and scrolls entirely in managed code.
    /// </summary>
    class SaveEditorForm : Form {
        SaveGame save;
        ComboBox saveSelector;
        Label status;
        // clubs tab
        TextBox clubSearch;
        DataGridView clubGrid;
        TextBox balanceBox, bankBox;
        Button writeButton;
        SaveGame.Club selectedClub;
        // players tab
        TextBox playerSearch;
        ComboBox playerClubFilter, playerNationFilter;
        DataGridView playerGrid;
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

        static DataGridView MakeGrid(Point location, Size size, AnchorStyles anchor) {
            return new DataGridView {
                Location = location, Size = size, Anchor = anchor,
                ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AllowUserToOrderColumns = false,
                RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                BackgroundColor = SystemColors.Window,
                ScrollBars = ScrollBars.Both
            };
        }

        static void AddGridColumn(DataGridView grid, string title, int width, bool numeric) {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn {
                HeaderText = title, Width = width, ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (numeric) {
                // typed values keep column-click sorting numeric, N0 renders separators
                column.ValueType = typeof(long);
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.DefaultCellStyle.Format = "N0";
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            grid.Columns.Add(column);
        }

        void BuildClubsPage(TabPage page) {
            Label searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(10, 13) };
            this.clubSearch = new TextBox { Location = new Point(70, 10), Width = 300 };
            this.clubSearch.TextChanged += (s, e) => RefreshClubList();

            this.clubGrid = MakeGrid(new Point(10, 40), new Size(460, 360),
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom);
            AddGridColumn(this.clubGrid, "Club", 220, false);
            AddGridColumn(this.clubGrid, "Balance", 105, true);
            AddGridColumn(this.clubGrid, "Bank", 95, true);
            this.clubGrid.SelectionChanged += (s, e) => ShowSelectedClub();

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

            page.Controls.AddRange(new Control[] { searchLabel, this.clubSearch, this.clubGrid, money, this.writeButton, legacy });
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

            this.playerGrid = MakeGrid(new Point(10, 40), new Size(722, 360),
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom);
            AddGridColumn(this.playerGrid, "Name", 165, false);
            AddGridColumn(this.playerGrid, "Pos", 75, false);
            AddGridColumn(this.playerGrid, "Age", 40, true);
            AddGridColumn(this.playerGrid, "Club", 155, false);
            AddGridColumn(this.playerGrid, "Nation", 100, false);
            AddGridColumn(this.playerGrid, "CA", 44, true);
            AddGridColumn(this.playerGrid, "PA", 44, true);
            AddGridColumn(this.playerGrid, "Value", 85, true);
            this.playerGrid.CellDoubleClick += (s, e) => {
                if (e.RowIndex >= 0) EditSelectedPlayer();
            };

            page.Controls.AddRange(new Control[] { searchLabel, this.playerSearch, clubLabel, this.playerClubFilter, nationLabel, this.playerNationFilter, hint, this.playerGrid });
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
                this.clubGrid.Rows.Clear();
                this.playerGrid.Rows.Clear();
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
            this.clubGrid.Rows.Clear();
            if (this.save != null) {
                string needle = this.clubSearch.Text.Trim().ToLower();
                foreach (SaveGame.Club club in this.save.Clubs) {
                    if (needle.Length == 0 || club.LongName.ToLower().Contains(needle) || club.ShortName.ToLower().Contains(needle)) {
                        int index = this.clubGrid.Rows.Add(club.LongName, club.Balance, (long) club.Bank);
                        this.clubGrid.Rows[index].Tag = club;
                        if (this.clubGrid.Rows.Count >= 400) break;
                    }
                }
            }
            this.clubGrid.ClearSelection();
            this.writeButton.Enabled = false;
            this.selectedClub = null;
        }

        void RefreshPlayerList() {
            this.playerGrid.Rows.Clear();
            if (this.save != null && this.save.Players.Count > 0) {
                string name = this.playerSearch.Text.Trim().ToLower();
                string club = this.playerClubFilter.Text.Trim().ToLower();
                string nation = this.playerNationFilter.Text.Trim().ToLower();
                if (name.Length >= 3 || club.Length >= 3 || nation.Length >= 2) {
                    foreach (SaveGame.PlayerRef player in this.save.Players) {
                        if (name.Length > 0 && !player.Name.ToLower().Contains(name)) continue;
                        if (club.Length > 0 && !player.ClubName.ToLower().Contains(club)) continue;
                        if (nation.Length > 0 && !player.Nation.ToLower().Contains(nation)) continue;
                        int index = this.playerGrid.Rows.Add(
                            player.Name, player.Position, (long) player.Age,
                            player.ClubName, player.Nation,
                            (long) this.save.ReadInt16(player.PlayerBase + 5),
                            (long) this.save.ReadInt16(player.PlayerBase + 7),
                            (long) this.save.ReadInt32(player.StaffBase + 82));
                        this.playerGrid.Rows[index].Tag = player;
                        if (this.playerGrid.Rows.Count >= 400) break;
                    }
                }
            }
            this.playerGrid.ClearSelection();
        }

        void ShowSelectedClub() {
            if (this.clubGrid.SelectedRows.Count == 0 || this.clubGrid.SelectedRows[0].Tag == null) {
                this.writeButton.Enabled = false;
                this.selectedClub = null;
                return;
            }
            this.selectedClub = (SaveGame.Club) this.clubGrid.SelectedRows[0].Tag;
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
                if (this.clubGrid.SelectedRows.Count > 0) {
                    this.clubGrid.SelectedRows[0].Cells[1].Value = balance;
                    this.clubGrid.SelectedRows[0].Cells[2].Value = (long) bank;
                }
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            }
        }

        void EditSelectedPlayer() {
            if (this.save == null || this.playerGrid.SelectedRows.Count == 0) return;
            SaveGame.PlayerRef player = this.playerGrid.SelectedRows[0].Tag as SaveGame.PlayerRef;
            if (player == null) return;
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
