using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using static CM0102_Starter_Kit.Helper;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Built-in save game editor. Clubs tab: money is written to BOTH places the engine
    /// reads (finance ledger int64 + club Bank) and clamped to overflow-safe values.
    /// Players tab: attribute editing with the intrinsic/display conversion handled
    /// (see PlayerEditForm). Every write backs the save up first.
    /// Layout is dock-based ONLY: anchoring children to a TabPage while it still has
    /// its default 200x100 bounds bakes in garbage distances, which made the lists
    /// grow past the visible page (unreachable rows, off-screen scrollbar).
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
        ComboBox batchAttribute;
        DataGridView playerGrid;
        // staff tab
        TextBox staffSearch;
        ComboBox staffClubFilter, staffNationFilter;
        DataGridView staffGrid;
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
            Panel topBar = new Panel { Dock = DockStyle.Top, Height = 40 };
            Label saveLabel = new Label { Text = "Save game:", AutoSize = true, Location = new Point(12, 13) };
            this.saveSelector = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(85, 10), Width = 320
            };
            this.saveSelector.SelectedIndexChanged += (s, e) => LoadSelectedSave();
            topBar.Controls.Add(saveLabel);
            topBar.Controls.Add(this.saveSelector);

            this.status = new Label {
                Text = "", Dock = DockStyle.Bottom, Height = 28,
                Padding = new Padding(12, 6, 4, 0)
            };

            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            TabPage clubsPage = new TabPage("Clubs") { Padding = new Padding(6) };
            TabPage playersPage = new TabPage("Players") { Padding = new Padding(6) };
            TabPage staffPage = new TabPage("Staff") { Padding = new Padding(6) };
            tabs.TabPages.Add(clubsPage);
            tabs.TabPages.Add(playersPage);
            tabs.TabPages.Add(staffPage);
            tabs.SelectedIndexChanged += (s, e) => {
                if (tabs.SelectedTab == playersPage || tabs.SelectedTab == staffPage) EnsurePlayersLoaded();
            };

            BuildClubsPage(clubsPage);
            BuildPlayersPage(playersPage);
            BuildStaffPage(staffPage);

            // dock layout processes the collection back to front: add the filling
            // control first so the edge-docked bars claim their space before it
            this.Controls.Add(tabs);
            this.Controls.Add(this.status);
            this.Controls.Add(topBar);
            UiHelper.AssignTabOrder(this);
        }

        static DataGridView MakeGrid() {
            DataGridView grid = new DataGridView {
                Dock = DockStyle.Fill,
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
            // protected property; without it every repaint under Wine visibly lags
            typeof(DataGridView)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(grid, true, null);
            return grid;
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
            Panel filterBar = new Panel { Dock = DockStyle.Top, Height = 34 };
            Label searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(4, 8) };
            this.clubSearch = new TextBox { Location = new Point(64, 5), Width = 300 };
            this.clubSearch.TextChanged += (s, e) => RefreshClubList();
            filterBar.Controls.Add(searchLabel);
            filterBar.Controls.Add(this.clubSearch);

            this.clubGrid = MakeGrid();
            AddGridColumn(this.clubGrid, "Club", 220, false);
            AddGridColumn(this.clubGrid, "Balance", 105, true);
            AddGridColumn(this.clubGrid, "Bank", 95, true);
            this.clubGrid.SelectionChanged += (s, e) => ShowSelectedClub();

            Panel side = new Panel { Dock = DockStyle.Right, Width = 266 };
            GroupBox money = new GroupBox {
                Text = "Club money",
                Location = new Point(10, 2), Size = new Size(250, 205)
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
                Location = new Point(10, 217), Size = new Size(250, 34)
            };
            this.writeButton.Click += (s, e) => ApplyClubAndSave();

            Button legacy = new Button {
                Text = "Anything else: open CM Explorer...",
                Location = new Point(10, 261), Size = new Size(250, 34)
            };
            legacy.Click += (s, e) => this.launchLegacyExplorer();

            side.Controls.Add(money);
            side.Controls.Add(this.writeButton);
            side.Controls.Add(legacy);

            page.Controls.Add(this.clubGrid);
            page.Controls.Add(side);
            page.Controls.Add(filterBar);
        }

        static ComboBox MakeFilterCombo(int x, int width) {
            ComboBox combo = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDown,
                Location = new Point(x, 5), Width = width
            };
            ComboBoxAutoComplete.AttachFilter(combo);
            return combo;
        }

        /// <summary>Sets a filter combo's choices; the full list is kept in Tag so
        /// AttachFilter can rebuild the dropdown as the user types.</summary>
        static void SetFilterItems(ComboBox combo, string[] items) {
            combo.Tag = items;
            combo.Items.Clear();
            combo.Items.AddRange(items);
            combo.Text = "";
        }

        void BuildPlayersPage(TabPage page) {
            Panel filterBar = new Panel { Dock = DockStyle.Top, Height = 64 };
            Label searchLabel = new Label { Text = "Name:", AutoSize = true, Location = new Point(4, 8) };
            this.playerSearch = new TextBox { Location = new Point(52, 5), Width = 180 };
            this.playerSearch.TextChanged += (s, e) => RefreshPlayerList();
            Label clubLabel = new Label { Text = "Club:", AutoSize = true, Location = new Point(246, 8) };
            this.playerClubFilter = MakeFilterCombo(286, 190);
            this.playerClubFilter.TextChanged += (s, e) => RefreshPlayerList();
            Label nationLabel = new Label { Text = "Nation:", AutoSize = true, Location = new Point(490, 8) };
            this.playerNationFilter = MakeFilterCombo(542, 150);
            this.playerNationFilter.TextChanged += (s, e) => RefreshPlayerList();

            Label batchLabel = new Label { Text = "Batch:", AutoSize = true, Location = new Point(4, 38) };
            this.batchAttribute = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(52, 34), Width = 220
            };
            this.batchAttribute.Items.AddRange(BatchItemLabels());
            this.batchAttribute.SelectedIndex = 0;
            Button batchButton = new Button { Text = "Apply to filtered...", Location = new Point(282, 33), Size = new Size(130, 25) };
            batchButton.Click += (s, e) => BatchEditPlayers();

            filterBar.Controls.AddRange(new Control[] {
                searchLabel, this.playerSearch, clubLabel, this.playerClubFilter, nationLabel, this.playerNationFilter,
                batchLabel, this.batchAttribute, batchButton
            });

            Label hint = new Label {
                Text = "Fill any filter (3+ letters; nation 2+), then double-click a player to edit. Click a column header to sort.",
                Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray,
                Padding = new Padding(4, 6, 0, 0)
            };

            this.playerGrid = MakeGrid();
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

            page.Controls.Add(this.playerGrid);
            page.Controls.Add(hint);
            page.Controls.Add(filterBar);
        }

        void BuildStaffPage(TabPage page) {
            Panel filterBar = new Panel { Dock = DockStyle.Top, Height = 34 };
            Label searchLabel = new Label { Text = "Name:", AutoSize = true, Location = new Point(4, 8) };
            this.staffSearch = new TextBox { Location = new Point(52, 5), Width = 180 };
            this.staffSearch.TextChanged += (s, e) => RefreshStaffList();
            Label clubLabel = new Label { Text = "Club:", AutoSize = true, Location = new Point(246, 8) };
            this.staffClubFilter = MakeFilterCombo(286, 190);
            this.staffClubFilter.TextChanged += (s, e) => RefreshStaffList();
            Label nationLabel = new Label { Text = "Nation:", AutoSize = true, Location = new Point(490, 8) };
            this.staffNationFilter = MakeFilterCombo(542, 150);
            this.staffNationFilter.TextChanged += (s, e) => RefreshStaffList();
            filterBar.Controls.AddRange(new Control[] {
                searchLabel, this.staffSearch, clubLabel, this.staffClubFilter, nationLabel, this.staffNationFilter
            });

            Label hint = new Label {
                Text = "Managers, coaches, scouts and retired players. Fill any filter (3+ letters; nation 2+), then double-click to edit.",
                Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray,
                Padding = new Padding(4, 6, 0, 0)
            };

            this.staffGrid = MakeGrid();
            AddGridColumn(this.staffGrid, "Name", 180, false);
            AddGridColumn(this.staffGrid, "Job", 120, false);
            AddGridColumn(this.staffGrid, "Age", 45, true);
            AddGridColumn(this.staffGrid, "Club", 180, false);
            AddGridColumn(this.staffGrid, "Nation", 120, false);
            AddGridColumn(this.staffGrid, "Value", 95, true);
            this.staffGrid.CellDoubleClick += (s, e) => {
                if (e.RowIndex >= 0) EditSelectedStaff();
            };

            page.Controls.Add(this.staffGrid);
            page.Controls.Add(hint);
            page.Controls.Add(filterBar);
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

        void CloseCurrentSave() {
            if (this.save != null) {
                this.save.Dispose();
                this.save = null;
            }
            this.clubGrid.Rows.Clear();
            this.playerGrid.Rows.Clear();
            this.staffGrid.Rows.Clear();
        }

        protected override void Dispose(bool disposing) {
            if (disposing && this.save != null) {
                this.save.Dispose();
                this.save = null;
            }
            base.Dispose(disposing);
        }

        void LoadSelectedSave() {
            try {
                Cursor = Cursors.WaitCursor;
                CloseCurrentSave();
                this.save = new SaveGame(Path.Combine(GameFolder, (string) this.saveSelector.SelectedItem));
                this.save.Load();
                this.status.Text = this.save.Clubs.Count.ToString("N0") + " clubs loaded from " + this.save.FileName + ".";
                PopulateClubFilter();
                SetFilterItems(this.playerNationFilter, new string[0]);
                SetFilterItems(this.staffNationFilter, new string[0]);
                RefreshClubList();
                RefreshPlayerList();
                RefreshStaffList();
            } catch (OutOfMemoryException) {
                CloseCurrentSave();
                GC.Collect();
                this.status.Text = "Not enough memory - restart the Starter Kit app and try again.";
            } catch (Exception exception) {
                CloseCurrentSave();
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
            SetFilterItems(this.playerClubFilter, names.ToArray());
            SetFilterItems(this.staffClubFilter, names.ToArray());
        }

        void EnsurePlayersLoaded() {
            if (this.save == null || this.save.Players.Count > 0) return;
            try {
                Cursor = Cursors.WaitCursor;
                this.save.LoadPlayers();
                List<string> nationNames = new List<string>();
                foreach (SaveGame.Nation nation in this.save.Nations) {
                    nationNames.Add(nation.Name);
                }
                SetFilterItems(this.playerNationFilter, nationNames.ToArray());
                SetFilterItems(this.staffNationFilter, nationNames.ToArray());
                this.status.Text = this.save.Players.Count.ToString("N0") + " players and " +
                    this.save.StaffMembers.Count.ToString("N0") + " non-player staff indexed.";
                RefreshPlayerList();
                RefreshStaffList();
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            } finally {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>One AddRange instead of per-row inserts - each Rows.Add is a
        /// full layout/invalidate pass, which under Wine visibly drags.</summary>
        DataGridViewRow MakeRow(DataGridView grid, object tag, params object[] values) {
            DataGridViewRow row = new DataGridViewRow();
            row.CreateCells(grid, values);
            row.Tag = tag;
            return row;
        }

        /// <summary>Rows.Clear wipes the grid's sort state; re-apply the column
        /// sort the user had chosen so refreshes (e.g. after a player edit) keep it.</summary>
        static void ReplaceRowsKeepingSort(DataGridView grid, List<DataGridViewRow> rows) {
            DataGridViewColumn sortedColumn = grid.SortedColumn;
            SortOrder sortOrder = grid.SortOrder;
            grid.Rows.Clear();
            grid.Rows.AddRange(rows.ToArray());
            if (sortedColumn != null && sortOrder != SortOrder.None) {
                grid.Sort(sortedColumn, sortOrder == SortOrder.Descending
                    ? System.ComponentModel.ListSortDirection.Descending
                    : System.ComponentModel.ListSortDirection.Ascending);
            }
            grid.ClearSelection();
        }

        void RefreshClubList() {
            List<DataGridViewRow> rows = new List<DataGridViewRow>();
            if (this.save != null) {
                string needle = this.clubSearch.Text.Trim().ToLower();
                foreach (SaveGame.Club club in this.save.Clubs) {
                    if (needle.Length == 0 || club.LongName.ToLower().Contains(needle) || club.ShortName.ToLower().Contains(needle)) {
                        rows.Add(MakeRow(this.clubGrid, club, club.LongName, club.Balance, (long) club.Bank));
                        if (rows.Count >= 400) break;
                    }
                }
            }
            ReplaceRowsKeepingSort(this.clubGrid, rows);
            this.writeButton.Enabled = false;
            this.selectedClub = null;
        }

        /// <summary>All players matching the current filter boxes, uncapped. With
        /// every filter empty this is the whole player table.</summary>
        List<SaveGame.PlayerRef> FilteredPlayers() {
            List<SaveGame.PlayerRef> matches = new List<SaveGame.PlayerRef>();
            if (this.save == null) return matches;
            string name = this.playerSearch.Text.Trim().ToLower();
            string club = this.playerClubFilter.Text.Trim().ToLower();
            string nation = this.playerNationFilter.Text.Trim().ToLower();
            foreach (SaveGame.PlayerRef player in this.save.Players) {
                if (name.Length > 0 && !player.Name.ToLower().Contains(name)) continue;
                if (club.Length > 0 && !player.ClubName.ToLower().Contains(club)) continue;
                if (nation.Length > 0 && !player.Nation.ToLower().Contains(nation)) continue;
                matches.Add(player);
            }
            return matches;
        }

        void RefreshPlayerList() {
            List<DataGridViewRow> rows = new List<DataGridViewRow>();
            if (this.save != null && this.save.Players.Count > 0) {
                string name = this.playerSearch.Text.Trim().ToLower();
                string club = this.playerClubFilter.Text.Trim().ToLower();
                string nation = this.playerNationFilter.Text.Trim().ToLower();
                if (name.Length >= 3 || club.Length >= 3 || nation.Length >= 2) {
                    foreach (SaveGame.PlayerRef player in FilteredPlayers()) {
                        rows.Add(MakeRow(this.playerGrid, player,
                            player.Name, player.Position, (long) player.Age,
                            player.ClubName, player.Nation,
                            (long) this.save.ReadInt16(player.PlayerBase + 5),
                            (long) this.save.ReadInt16(player.PlayerBase + 7),
                            (long) this.save.ReadInt32(player.StaffBase + 82)));
                        if (rows.Count >= 400) break;
                    }
                }
            }
            ReplaceRowsKeepingSort(this.playerGrid, rows);
        }

        void RefreshStaffList() {
            List<DataGridViewRow> rows = new List<DataGridViewRow>();
            if (this.save != null && this.save.StaffMembers.Count > 0) {
                string name = this.staffSearch.Text.Trim().ToLower();
                string club = this.staffClubFilter.Text.Trim().ToLower();
                string nation = this.staffNationFilter.Text.Trim().ToLower();
                if (name.Length >= 3 || club.Length >= 3 || nation.Length >= 2) {
                    foreach (SaveGame.PlayerRef person in this.save.StaffMembers) {
                        if (name.Length > 0 && !person.Name.ToLower().Contains(name)) continue;
                        if (club.Length > 0 && !person.ClubName.ToLower().Contains(club)) continue;
                        if (nation.Length > 0 && !person.Nation.ToLower().Contains(nation)) continue;
                        rows.Add(MakeRow(this.staffGrid, person,
                            person.Name, person.Position, (long) person.Age,
                            person.ClubName, person.Nation,
                            (long) this.save.ReadInt32(person.StaffBase + 82)));
                        if (rows.Count >= 400) break;
                    }
                }
            }
            ReplaceRowsKeepingSort(this.staffGrid, rows);
        }

        // ---------------- batch edit (players tab) ----------------
        // Item indexes: 0 condition%, 1 fitness%, 2 heal injuries (no value),
        // 3 morale, 4 CA, 5 PA, then the 42 playing attributes.
        const int BatchAttrOffset = 6;

        static string[] BatchItemLabels() {
            List<string> labels = new List<string> {
                "Condition (%)", "Fitness (%)", "Heal injuries (100% cond/fit)",
                "Morale (0-20)", "Current Ability (1-200)", "Potential Ability (1-200)"
            };
            labels.AddRange(PlayerEditForm.AttrLabels);
            return labels.ToArray();
        }

        static void BatchItemRange(int item, out int min, out int max) {
            switch (item) {
                case 0: case 1: min = 0; max = 100; break;
                case 3: min = 0; max = 20; break;
                case 4: case 5: min = 1; max = 200; break;
                default: min = 1; max = 20; break;
            }
        }

        int BatchItemInitialValue(int item, SaveGame.PlayerRef first) {
            switch (item) {
                case 0: case 1: return 100;
                case 3: return Math.Min(Math.Max((int) this.save.ReadByte(first.PlayerBase + 69), 0), 20);
                case 4: return Math.Min(Math.Max((int) this.save.ReadInt16(first.PlayerBase + 5), 1), 200);
                case 5: return Math.Min(Math.Max((int) this.save.ReadInt16(first.PlayerBase + 7), 1), 200);
                default: {
                    int attr = item - BatchAttrOffset;
                    short ability = this.save.ReadInt16(first.PlayerBase + 5);
                    bool goalkeeper = this.save.ReadSByte(first.PlayerBase + 15) >= 15;
                    sbyte stored = this.save.ReadSByte(first.PlayerBase + 27 + attr);
                    return Math.Min(Math.Max(
                        PlayerEditForm.ToDisplay(PlayerEditForm.AttrKinds[attr], stored, ability, goalkeeper), 1), 20);
                }
            }
        }

        /// <summary>Stages the batch write for every target; returns how many players
        /// were actually touched (condition/fitness/heal skip players without an
        /// injury-table record). Does NOT save.</summary>
        int ApplyPlayerBatch(int item, int value, List<SaveGame.PlayerRef> targets) {
            int affected = 0;
            foreach (SaveGame.PlayerRef player in targets) {
                switch (item) {
                    case 0:
                        if (player.FitnessBase < 0) continue;
                        this.save.WriteInt16(player.FitnessBase + 10, (short) (value * 100));
                        break;
                    case 1:
                        if (player.FitnessBase < 0) continue;
                        this.save.WriteInt16(player.FitnessBase + 8, (short) (value * 100));
                        break;
                    case 2:
                        if (player.FitnessBase < 0) continue;
                        this.save.WriteInt16(player.FitnessBase + 8, 10000);
                        this.save.WriteInt16(player.FitnessBase + 10, 10000);
                        this.save.WriteByte(player.FitnessBase + 18, 0xff);
                        this.save.WriteByte(player.FitnessBase + 19, 0);
                        break;
                    case 3:
                        this.save.WriteByte(player.PlayerBase + 69, (byte) value);
                        break;
                    case 4: {
                        this.save.WriteInt16(player.PlayerBase + 5, (short) value);
                        short potential = this.save.ReadInt16(player.PlayerBase + 7);
                        if (potential < value) {
                            this.save.WriteInt16(player.PlayerBase + 7, (short) value);
                        }
                        break;
                    }
                    case 5: {
                        short ability = this.save.ReadInt16(player.PlayerBase + 5);
                        this.save.WriteInt16(player.PlayerBase + 7, (short) Math.Max(value, (int) ability));
                        break;
                    }
                    default: {
                        // re-encoded per player against their own CA/GK status
                        int attr = item - BatchAttrOffset;
                        short ability = this.save.ReadInt16(player.PlayerBase + 5);
                        bool goalkeeper = this.save.ReadSByte(player.PlayerBase + 15) >= 15;
                        this.save.WriteSByte(player.PlayerBase + 27 + attr,
                            PlayerEditForm.FromDisplay(PlayerEditForm.AttrKinds[attr], value, ability, goalkeeper));
                        break;
                    }
                }
                affected++;
            }
            return affected;
        }

        void BatchEditPlayers() {
            if (this.save == null) return;
            EnsurePlayersLoaded();
            if (this.save.Players.Count == 0) return;
            int item = this.batchAttribute.SelectedIndex;
            if (item < 0) return;
            List<SaveGame.PlayerRef> targets = FilteredPlayers();
            if (targets.Count == 0) {
                this.status.Text = "No players match the current filter.";
                return;
            }
            string label = this.batchAttribute.Text;
            bool needsValue = item != 2;
            bool unfiltered = this.playerSearch.Text.Trim().Length == 0 &&
                this.playerClubFilter.Text.Trim().Length == 0 &&
                this.playerNationFilter.Text.Trim().Length == 0;
            string scope = targets.Count.ToString("N0") + (unfiltered ? " players (NO filter - the whole save!)" : " filtered players");
            int min, max;
            BatchItemRange(item, out min, out max);
            int value;
            using (BatchDialog dialog = new BatchDialog(
                (needsValue ? "Set \"" + label + "\" for " : "Heal injuries and restore condition/fitness for ") + scope + ".",
                needsValue, min, max, needsValue ? BatchItemInitialValue(item, targets[0]) : 0)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                value = dialog.Value;
            }
            if (BlockedByRunningGame()) {
                this.status.Text = "Batch NOT saved - game running. Retry after exiting the game.";
                return;
            }
            try {
                int affected = ApplyPlayerBatch(item, value, targets);
                string backup = this.save.Save();
                this.status.Text = label + " applied to " + affected.ToString("N0") +
                    " players. Backup: " + Path.GetFileName(backup);
                RefreshPlayerList();
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            }
        }

        /// <summary>Tiny confirm popup for batch edits: message, optional value
        /// spinner, OK/Cancel.</summary>
        class BatchDialog : Form {
            readonly NumericUpDown numeric;

            public int Value { get { return this.numeric != null ? (int) this.numeric.Value : 0; } }

            public BatchDialog(string message, bool needsValue, int min, int max, int initial) {
                this.Text = "Batch edit";
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.MinimizeBox = false;
                this.StartPosition = FormStartPosition.CenterParent;
                this.ClientSize = new Size(380, 130);
                Label text = new Label { Text = message, Location = new Point(12, 10), Size = new Size(356, 48) };
                this.Controls.Add(text);
                if (needsValue) {
                    this.Controls.Add(new Label { Text = "Value:", AutoSize = true, Location = new Point(12, 66) });
                    this.numeric = new NumericUpDown {
                        Minimum = min, Maximum = max,
                        Value = Math.Min(Math.Max(initial, min), max),
                        Location = new Point(60, 62), Width = 70
                    };
                    this.Controls.Add(this.numeric);
                }
                Button ok = new Button { Text = "Apply", DialogResult = DialogResult.OK, Location = new Point(180, 94), Size = new Size(90, 28) };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(278, 94), Size = new Size(90, 28) };
                this.Controls.Add(ok);
                this.Controls.Add(cancel);
                this.AcceptButton = ok;
                this.CancelButton = cancel;
            }
        }

        void EditSelectedStaff() {
            if (this.save == null || this.staffGrid.SelectedRows.Count == 0) return;
            SaveGame.PlayerRef person = this.staffGrid.SelectedRows[0].Tag as SaveGame.PlayerRef;
            if (person == null) return;
            using (StaffEditForm editor = new StaffEditForm(this.save, person)) {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
            }
            if (BlockedByRunningGame()) {
                this.status.Text = "Edit NOT saved - game running. Reopen the editor after exiting the game.";
                return;
            }
            try {
                string backup = this.save.Save();
                this.status.Text = person.Name + " updated. Backup: " + Path.GetFileName(backup);
                RefreshStaffList();
            } catch (Exception exception) {
                this.status.Text = exception.Message;
            }
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
