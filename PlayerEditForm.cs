using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Player editor dialog (Save Editor phase 2/3). Attribute maths from Nick's
    /// CM0102Patcher Scouter: a subset of playing attributes is stored as CA-weighted
    /// intrinsic sbytes; the in-game 1-20 value is
    ///   t = intrinsic/10 + CA/w + 10;  shown = trunc(t*t/30 + t/3 + 0.5)
    /// with w = 20 ("high" branch) or 200 ("low" branch); which branch applies to the
    /// goalkeeping/outfield technical attributes depends on whether the player is a
    /// goalkeeper (GK position rating >= 15). Editing inverts that mapping against the
    /// CA/GK values currently in the dialog.
    /// Condition/fitness live in the injury.dat fitness table (0-10000, shown as %);
    /// "clear injury" resets that record's type/severity flags. Mentals, caps/goals
    /// and the nationalities live in the staff record; liked/disliked clubs and people
    /// live in Preferences.dat.
    /// </summary>
    class PlayerEditForm : Form {
        enum Kind { Raw, High, Gk, Outfield }

        class Attr {
            public string Label;
            public int Offset;      // within the 70-byte player record
            public Kind Kind;
            public NumericUpDown Control;
            public Attr(string label, int offset, Kind kind) { Label = label; Offset = offset; Kind = kind; }
        }

        static readonly string[] PositionNames = {
            "Goalkeeper", "Sweeper", "Defender", "Def Midfielder", "Midfielder",
            "Att Midfielder", "Attacker", "Wingback", "Right", "Left", "Center", "Free Role"
        };

        static readonly string[] MentalNames = {
            "Adaptability", "Ambition", "Determination", "Loyalty",
            "Pressure", "Professionalism", "Sportsmanship", "Temperament"
        };

        readonly SaveGame save;
        readonly SaveGame.PlayerRef player;
        readonly List<Attr> attributes = new List<Attr>();
        readonly NumericUpDown[] positions = new NumericUpDown[12];
        NumericUpDown ability, potential, homeRep, currentRep, worldRep, value, wage, squadNumber;
        // Personal tab
        NumericUpDown condition, fitness, morale, caps, intGoals;
        CheckBox clearInjury;
        ComboBox nationality, secondNationality;
        readonly NumericUpDown[] mentals = new NumericUpDown[8];
        // Likes & dislikes tab
        readonly ComboBox[] prefClubs = new ComboBox[6];       // 3 favourite + 3 disliked
        readonly TextBox[] prefStaffBoxes = new TextBox[6];    // 3 favourite + 3 disliked
        readonly int[] prefStaffIds = new int[6];
        // original values for lossy fields, so an untouched dialog writes nothing back
        int originalConditionPct, originalFitnessPct, originalHomeRep, originalCurrentRep, originalWorldRep;
        readonly Dictionary<int, string> clubIdToName = new Dictionary<int, string>();
        readonly Dictionary<string, int> clubNameToId = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        readonly Dictionary<string, int> nationNameToId = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        public PlayerEditForm(SaveGame save, SaveGame.PlayerRef player) {
            this.save = save;
            this.player = player;
            this.Text = "Edit Player - " + player.Name + " (" + player.ClubName + ", age " + player.Age + ")";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(910, 655);
            foreach (SaveGame.Club club in save.Clubs) {
                this.clubIdToName[club.Id] = club.LongName;
                if (!this.clubNameToId.ContainsKey(club.LongName)) {
                    this.clubNameToId[club.LongName] = club.Id;
                }
            }
            foreach (SaveGame.Nation nation in save.Nations) {
                if (!this.nationNameToId.ContainsKey(nation.Name)) {
                    this.nationNameToId[nation.Name] = nation.Id;
                }
            }
            DefineAttributes();
            BuildControls();
            ReadValues();
        }

        void DefineAttributes() {
            // record order; offsets are +27..+68 in the player record
            string[] labels = {
                "Acceleration", "Aggression", "Agility", "Anticipation", "Balance", "Bravery",
                "Consistency", "Corners", "Crossing", "Decisions", "Dirtiness", "Dribbling",
                "Finishing", "Flair", "Free Kicks", "Handling", "Heading", "Imp. Matches",
                "Injury Prone", "Jumping", "Leadership", "Left Foot", "Long Shots", "Marking",
                "Movement", "Fitness", "One on Ones", "Pace", "Passing", "Penalties",
                "Positioning", "Reflexes", "Right Foot", "Stamina", "Strength", "Tackling",
                "Teamwork", "Technique", "Throw-ins", "Versatility", "Vision", "Work Rate"
            };
            Kind[] kinds = {
                Kind.Raw, Kind.Raw, Kind.Raw, Kind.High, Kind.Raw, Kind.Raw,
                Kind.Raw, Kind.Raw, Kind.Outfield, Kind.High, Kind.Raw, Kind.Outfield,
                Kind.Outfield, Kind.Raw, Kind.Raw, Kind.Gk, Kind.High, Kind.Raw,
                Kind.Raw, Kind.Raw, Kind.Raw, Kind.Raw, Kind.High, Kind.Outfield,
                Kind.Outfield, Kind.Raw, Kind.Gk, Kind.Raw, Kind.High, Kind.High,
                Kind.High, Kind.Gk, Kind.Raw, Kind.Raw, Kind.Raw, Kind.High,
                Kind.Raw, Kind.Raw, Kind.Outfield, Kind.Raw, Kind.Outfield, Kind.Raw
            };
            for (int i = 0; i < labels.Length; i++) {
                this.attributes.Add(new Attr(labels[i], 27 + i, kinds[i]));
            }
        }

        static NumericUpDown MakeNumeric(int min, int max) {
            return new NumericUpDown { Minimum = min, Maximum = max, Width = 55 };
        }

        ComboBox MakeAutoCompleteCombo(int width) {
            ComboBox combo = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = width
            };
            ComboBoxAutoComplete.Attach(combo);
            return combo;
        }

        void BuildControls() {
            TabControl tabs = new TabControl { Location = new Point(10, 8), Size = new Size(890, 600) };
            TabPage attributesPage = new TabPage("Attributes");
            TabPage personalPage = new TabPage("Condition && Personal");
            TabPage prefsPage = new TabPage("Likes && Dislikes");
            tabs.TabPages.Add(attributesPage);
            tabs.TabPages.Add(personalPage);
            tabs.TabPages.Add(prefsPage);
            BuildAttributesPage(attributesPage);
            BuildPersonalPage(personalPage);
            BuildPrefsPage(prefsPage);

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(700, 615), Size = new Size(90, 30) };
            ok.Click += (s, e) => WriteValues();
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(800, 615), Size = new Size(90, 30) };
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.Controls.AddRange(new Control[] { tabs, ok, cancel });
            UiHelper.AssignTabOrder(this);
        }

        void BuildAttributesPage(TabPage page) {
            GroupBox general = new GroupBox { Text = "General (0-200 scales / raw values)", Location = new Point(8, 8), Size = new Size(866, 80) };
            this.ability = MakeNumeric(1, 200); this.potential = MakeNumeric(1, 200);
            this.currentRep = MakeNumeric(0, 200); this.homeRep = MakeNumeric(0, 200); this.worldRep = MakeNumeric(0, 200);
            this.value = MakeNumeric(0, 500000000); this.value.Width = 95; this.value.ThousandsSeparator = true;
            this.wage = MakeNumeric(0, 5000000); this.wage.Width = 80; this.wage.ThousandsSeparator = true;
            this.squadNumber = MakeNumeric(0, 99);
            object[][] generalFields = {
                new object[] { "Current Ability", this.ability }, new object[] { "Potential Ability", this.potential },
                new object[] { "Cur. Reputation", this.currentRep }, new object[] { "Home Rep.", this.homeRep },
                new object[] { "World Rep.", this.worldRep }, new object[] { "Value", this.value },
                new object[] { "Wage", this.wage }, new object[] { "Squad No.", this.squadNumber }
            };
            int x = 12;
            foreach (object[] field in generalFields) {
                Control control = (Control) field[1];
                general.Controls.Add(new Label { Text = (string) field[0], Location = new Point(x, 22), AutoSize = true });
                control.Location = new Point(x, 42);
                general.Controls.Add(control);
                x += control.Width + 45;
            }
            if (this.player.ContractBase < 0) {
                this.wage.Enabled = false;
            }

            GroupBox positionsBox = new GroupBox { Text = "Positions (0-20)", Location = new Point(8, 96), Size = new Size(866, 92) };
            for (int i = 0; i < 12; i++) {
                int column = i % 6, row = i / 6;
                positionsBox.Controls.Add(new Label { Text = PositionNames[i], Location = new Point(12 + column * 143, 22 + row * 34), AutoSize = true });
                this.positions[i] = MakeNumeric(0, 20);
                this.positions[i].Location = new Point(97 + column * 143, 18 + row * 34);
                positionsBox.Controls.Add(this.positions[i]);
            }

            GroupBox attributesBox = new GroupBox { Text = "Playing attributes (as shown in game, 1-20)", Location = new Point(8, 196), Size = new Size(866, 330) };
            for (int i = 0; i < this.attributes.Count; i++) {
                int column = i % 6, row = i / 6;
                Attr attribute = this.attributes[i];
                attributesBox.Controls.Add(new Label { Text = attribute.Label, Location = new Point(12 + column * 143, 24 + row * 42), AutoSize = true });
                attribute.Control = MakeNumeric(1, 20);
                attribute.Control.Location = new Point(15 + column * 143, 40 + row * 42);
                attributesBox.Controls.Add(attribute.Control);
            }

            page.Controls.AddRange(new Control[] { general, positionsBox, attributesBox });
        }

        void BuildPersonalPage(TabPage page) {
            GroupBox fitnessBox = new GroupBox { Text = "Match fitness", Location = new Point(8, 8), Size = new Size(420, 160) };
            fitnessBox.Controls.Add(new Label { Text = "Condition (%)", Location = new Point(12, 26), AutoSize = true });
            this.condition = MakeNumeric(0, 100);
            this.condition.Location = new Point(120, 22);
            fitnessBox.Controls.Add(this.condition);
            fitnessBox.Controls.Add(new Label { Text = "Fitness (%)", Location = new Point(210, 26), AutoSize = true });
            this.fitness = MakeNumeric(0, 100);
            this.fitness.Location = new Point(300, 22);
            fitnessBox.Controls.Add(this.fitness);
            fitnessBox.Controls.Add(new Label { Text = "Morale (0-20)", Location = new Point(12, 60), AutoSize = true });
            this.morale = MakeNumeric(0, 20);
            this.morale.Location = new Point(120, 56);
            fitnessBox.Controls.Add(this.morale);
            Label injuryStatus = new Label { Text = "", Location = new Point(12, 94), AutoSize = true, ForeColor = Color.Firebrick };
            fitnessBox.Controls.Add(injuryStatus);
            this.clearInjury = new CheckBox { Text = "Clear injury && restore full condition on OK", Location = new Point(12, 120), AutoSize = true };
            fitnessBox.Controls.Add(this.clearInjury);
            if (this.player.FitnessBase < 0) {
                this.condition.Enabled = this.fitness.Enabled = this.clearInjury.Enabled = false;
                injuryStatus.Text = "No fitness record found in this save.";
                injuryStatus.ForeColor = Color.DimGray;
            } else if (this.save.ReadByte(this.player.FitnessBase + 18) != 0xff) {
                injuryStatus.Text = "INJURED (type " + this.save.ReadByte(this.player.FitnessBase + 18) +
                    ", severity " + this.save.ReadByte(this.player.FitnessBase + 19) + ")";
            } else {
                injuryStatus.Text = "Not injured.";
                injuryStatus.ForeColor = Color.DimGray;
                this.clearInjury.Enabled = false;
            }

            GroupBox international = new GroupBox { Text = "Nationality && international record", Location = new Point(440, 8), Size = new Size(434, 160) };
            international.Controls.Add(new Label { Text = "Nationality", Location = new Point(12, 26), AutoSize = true });
            this.nationality = MakeAutoCompleteCombo(220);
            this.nationality.Location = new Point(120, 22);
            international.Controls.Add(this.nationality);
            international.Controls.Add(new Label { Text = "2nd nationality", Location = new Point(12, 60), AutoSize = true });
            this.secondNationality = MakeAutoCompleteCombo(220);
            this.secondNationality.Location = new Point(120, 56);
            international.Controls.Add(this.secondNationality);
            List<object> nationItems = new List<object>();
            foreach (SaveGame.Nation nation in this.save.Nations) {
                nationItems.Add(nation.Name);
            }
            this.nationality.Items.AddRange(nationItems.ToArray());
            nationItems.Insert(0, "(none)");
            this.secondNationality.Items.AddRange(nationItems.ToArray());
            international.Controls.Add(new Label { Text = "Int'l caps", Location = new Point(12, 94), AutoSize = true });
            this.caps = MakeNumeric(0, 255);
            this.caps.Location = new Point(120, 90);
            international.Controls.Add(this.caps);
            international.Controls.Add(new Label { Text = "Int'l goals", Location = new Point(210, 94), AutoSize = true });
            this.intGoals = MakeNumeric(0, 255);
            this.intGoals.Location = new Point(300, 90);
            international.Controls.Add(this.intGoals);
            international.Controls.Add(new Label {
                Text = "Changing nationality affects eligibility rules;\nthe national team squad updates over time.",
                Location = new Point(12, 122), AutoSize = true, ForeColor = Color.DimGray
            });

            GroupBox mentalsBox = new GroupBox { Text = "Mental attributes (0-20)", Location = new Point(8, 180), Size = new Size(866, 120) };
            for (int i = 0; i < 8; i++) {
                int column = i % 4, row = i / 4;
                mentalsBox.Controls.Add(new Label { Text = MentalNames[i], Location = new Point(12 + column * 215, 26 + row * 44), AutoSize = true });
                this.mentals[i] = MakeNumeric(0, 20);
                this.mentals[i].Location = new Point(130 + column * 215, 22 + row * 44);
                mentalsBox.Controls.Add(this.mentals[i]);
            }

            page.Controls.AddRange(new Control[] { fitnessBox, international, mentalsBox });
        }

        void BuildPrefsPage(TabPage page) {
            if (this.player.PrefsBase < 0) {
                page.Controls.Add(new Label {
                    Text = "This save has no preferences record for this player.",
                    Location = new Point(12, 16), AutoSize = true, ForeColor = Color.DimGray
                });
                return;
            }
            List<object> clubItems = new List<object> { "(none)" };
            List<string> sortedClubs = new List<string>();
            foreach (SaveGame.Club club in this.save.Clubs) {
                sortedClubs.Add(club.LongName);
            }
            sortedClubs.Sort(StringComparer.CurrentCultureIgnoreCase);
            foreach (string name in sortedClubs) {
                clubItems.Add(name);
            }
            object[] clubItemArray = clubItems.ToArray();

            string[] groupTitles = { "Favourite clubs", "Disliked clubs" };
            for (int group = 0; group < 2; group++) {
                GroupBox box = new GroupBox { Text = groupTitles[group], Location = new Point(8 + group * 436, 8), Size = new Size(428, 140) };
                for (int slot = 0; slot < 3; slot++) {
                    ComboBox combo = MakeAutoCompleteCombo(390);
                    combo.Location = new Point(15, 24 + slot * 36);
                    combo.Items.AddRange(clubItemArray);
                    box.Controls.Add(combo);
                    this.prefClubs[group * 3 + slot] = combo;
                }
                page.Controls.Add(box);
            }

            string[] staffTitles = { "Favourite people", "Disliked people" };
            for (int group = 0; group < 2; group++) {
                GroupBox box = new GroupBox { Text = staffTitles[group], Location = new Point(8 + group * 436, 156), Size = new Size(428, 140) };
                for (int slot = 0; slot < 3; slot++) {
                    int index = group * 3 + slot;
                    TextBox display = new TextBox { Location = new Point(15, 24 + slot * 36), Width = 330, ReadOnly = true };
                    Button pick = new Button { Text = "...", Location = new Point(352, 23 + slot * 36), Size = new Size(38, 24) };
                    pick.Click += (s, e) => PickStaff(index);
                    box.Controls.Add(display);
                    box.Controls.Add(pick);
                    this.prefStaffBoxes[index] = display;
                }
                page.Controls.Add(box);
            }
        }

        void PickStaff(int index) {
            using (StaffPickerDialog picker = new StaffPickerDialog(this.save)) {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                this.prefStaffIds[index] = picker.SelectedId;
                this.prefStaffBoxes[index].Text = StaffDisplayName(picker.SelectedId);
            }
        }

        string StaffDisplayName(int staffId) {
            if (staffId < 0) return "(none)";
            string name = this.save.StaffNameById(staffId);
            return name.Length > 0 ? name : "(unknown #" + staffId + ")";
        }

        bool IsGoalkeeper { get { return this.positions[0].Value >= 15; } }

        void ReadValues() {
            int recordBase = this.player.PlayerBase;
            this.squadNumber.Value = Clamp(this.save.ReadByte(recordBase + 4), 0, 99);
            this.ability.Value = Clamp(this.save.ReadInt16(recordBase + 5), 1, 200);
            this.potential.Value = Clamp(this.save.ReadInt16(recordBase + 7), 1, 200);
            // reputations live on a 0-10000 scale in saves; shown 0-200 like the DB
            this.originalHomeRep = (int) Clamp((decimal) Math.Round(this.save.ReadInt16(recordBase + 9) / 50.0), 0, 200);
            this.originalCurrentRep = (int) Clamp((decimal) Math.Round(this.save.ReadInt16(recordBase + 11) / 50.0), 0, 200);
            this.originalWorldRep = (int) Clamp((decimal) Math.Round(this.save.ReadInt16(recordBase + 13) / 50.0), 0, 200);
            this.homeRep.Value = this.originalHomeRep;
            this.currentRep.Value = this.originalCurrentRep;
            this.worldRep.Value = this.originalWorldRep;
            this.value.Value = Clamp(this.save.ReadInt32(this.player.StaffBase + 82), 0, 500000000);
            if (this.player.ContractBase >= 0) {
                this.wage.Value = Clamp(this.save.ReadInt32(this.player.ContractBase + 12), 0, 5000000);
            }
            for (int i = 0; i < 12; i++) {
                this.positions[i].Value = Clamp(this.save.ReadSByte(recordBase + 15 + i), 0, 20);
            }
            short ability16 = this.save.ReadInt16(recordBase + 5);
            bool goalkeeper = this.save.ReadSByte(recordBase + 15) >= 15;
            foreach (Attr attribute in this.attributes) {
                sbyte stored = this.save.ReadSByte(recordBase + attribute.Offset);
                attribute.Control.Value = Clamp(ToDisplay(attribute.Kind, stored, ability16, goalkeeper), 1, 20);
            }

            // Personal tab
            if (this.player.FitnessBase >= 0) {
                this.originalConditionPct = (int) Clamp((decimal) Math.Round(this.save.ReadInt16(this.player.FitnessBase + 10) / 100.0), 0, 100);
                this.originalFitnessPct = (int) Clamp((decimal) Math.Round(this.save.ReadInt16(this.player.FitnessBase + 8) / 100.0), 0, 100);
                this.condition.Value = this.originalConditionPct;
                this.fitness.Value = this.originalFitnessPct;
            }
            this.morale.Value = Clamp(this.save.ReadByte(recordBase + 69), 0, 20);
            this.caps.Value = this.save.ReadByte(this.player.StaffBase + 34);
            this.intGoals.Value = this.save.ReadByte(this.player.StaffBase + 35);
            SelectNation(this.nationality, this.save.ReadInt32(this.player.StaffBase + 26), false);
            SelectNation(this.secondNationality, this.save.ReadInt32(this.player.StaffBase + 30), true);
            for (int i = 0; i < 8; i++) {
                this.mentals[i].Value = Clamp(this.save.ReadByte(this.player.StaffBase + 86 + i), 0, 20);
            }

            // Likes & dislikes tab
            if (this.player.PrefsBase >= 0) {
                for (int i = 0; i < 6; i++) {
                    int clubId = this.save.ReadInt32(this.player.PrefsBase + 4 + i * 4);
                    string clubName;
                    this.prefClubs[i].Text = clubId >= 0 && this.clubIdToName.TryGetValue(clubId, out clubName)
                        ? clubName : "(none)";
                    this.prefStaffIds[i] = this.save.ReadInt32(this.player.PrefsBase + 28 + i * 4);
                    this.prefStaffBoxes[i].Text = StaffDisplayName(this.prefStaffIds[i]);
                }
            }
        }

        void SelectNation(ComboBox combo, int nationId, bool allowNone) {
            string current = null;
            foreach (SaveGame.Nation nation in this.save.Nations) {
                if (nation.Id == nationId) {
                    current = nation.Name;
                    break;
                }
            }
            combo.Text = current != null ? current : (allowNone ? "(none)" : "");
        }

        int ResolveNation(ComboBox combo, int original) {
            string text = combo.Text.Trim();
            if (text.Length == 0 || text == "(none)") {
                return combo == this.secondNationality ? -1 : original;
            }
            int id;
            return this.nationNameToId.TryGetValue(text, out id) ? id : original;
        }

        void WriteValues() {
            int recordBase = this.player.PlayerBase;
            this.save.WriteByte(recordBase + 4, (byte) this.squadNumber.Value);
            this.save.WriteInt16(recordBase + 5, (short) this.ability.Value);
            this.save.WriteInt16(recordBase + 7, (short) Math.Max(this.potential.Value, this.ability.Value));
            if ((int) this.homeRep.Value != this.originalHomeRep) {
                this.save.WriteInt16(recordBase + 9, (short) (this.homeRep.Value * 50));
            }
            if ((int) this.currentRep.Value != this.originalCurrentRep) {
                this.save.WriteInt16(recordBase + 11, (short) (this.currentRep.Value * 50));
            }
            if ((int) this.worldRep.Value != this.originalWorldRep) {
                this.save.WriteInt16(recordBase + 13, (short) (this.worldRep.Value * 50));
            }
            this.save.WriteInt32(this.player.StaffBase + 82, (int) this.value.Value);
            if (this.player.ContractBase >= 0) {
                this.save.WriteInt32(this.player.ContractBase + 12, (int) this.wage.Value);
            }
            for (int i = 0; i < 12; i++) {
                this.save.WriteSByte(recordBase + 15 + i, (sbyte) this.positions[i].Value);
            }
            short ability16 = (short) this.ability.Value;
            bool goalkeeper = IsGoalkeeper;
            foreach (Attr attribute in this.attributes) {
                sbyte intrinsic = FromDisplay(attribute.Kind, (int) attribute.Control.Value, ability16, goalkeeper);
                this.save.WriteSByte(recordBase + attribute.Offset, intrinsic);
            }

            // Personal tab
            if (this.player.FitnessBase >= 0) {
                if (this.clearInjury.Checked) {
                    this.save.WriteInt16(this.player.FitnessBase + 8, 10000);
                    this.save.WriteInt16(this.player.FitnessBase + 10, 10000);
                    this.save.WriteByte(this.player.FitnessBase + 18, 0xff);
                    this.save.WriteByte(this.player.FitnessBase + 19, 0);
                } else {
                    if ((int) this.condition.Value != this.originalConditionPct) {
                        this.save.WriteInt16(this.player.FitnessBase + 10, (short) (this.condition.Value * 100));
                    }
                    if ((int) this.fitness.Value != this.originalFitnessPct) {
                        this.save.WriteInt16(this.player.FitnessBase + 8, (short) (this.fitness.Value * 100));
                    }
                }
            }
            this.save.WriteByte(recordBase + 69, (byte) this.morale.Value);
            this.save.WriteByte(this.player.StaffBase + 34, (byte) this.caps.Value);
            this.save.WriteByte(this.player.StaffBase + 35, (byte) this.intGoals.Value);
            this.save.WriteInt32(this.player.StaffBase + 26,
                ResolveNation(this.nationality, this.save.ReadInt32(this.player.StaffBase + 26)));
            this.save.WriteInt32(this.player.StaffBase + 30,
                ResolveNation(this.secondNationality, this.save.ReadInt32(this.player.StaffBase + 30)));
            for (int i = 0; i < 8; i++) {
                this.save.WriteByte(this.player.StaffBase + 86 + i, (byte) this.mentals[i].Value);
            }

            // Likes & dislikes tab
            if (this.player.PrefsBase >= 0) {
                for (int i = 0; i < 6; i++) {
                    int offset = this.player.PrefsBase + 4 + i * 4;
                    string text = this.prefClubs[i].Text.Trim();
                    if (text.Length == 0 || text == "(none)") {
                        this.save.WriteInt32(offset, -1);
                    } else {
                        int clubId;
                        if (this.clubNameToId.TryGetValue(text, out clubId)) {
                            this.save.WriteInt32(offset, clubId);
                        }
                    }
                    this.save.WriteInt32(this.player.PrefsBase + 28 + i * 4, this.prefStaffIds[i]);
                }
            }
        }

        static decimal Clamp(decimal value, decimal min, decimal max) {
            return Math.Min(Math.Max(value, min), max);
        }

        static bool UsesHighBranch(Kind kind, bool goalkeeper) {
            switch (kind) {
                case Kind.High: return true;
                case Kind.Gk: return goalkeeper;
                case Kind.Outfield: return !goalkeeper;
                default: return true;
            }
        }

        static int Forward(sbyte intrinsic, short ability, bool high) {
            double t = (intrinsic / 10.0) + (ability / (high ? 20.0 : 200.0)) + 10.0;
            double result = (t * t / 30.0) + (t / 3.0) + 0.5;
            return result < 1 ? 1 : (int) result;
        }

        static int ToDisplay(Kind kind, sbyte stored, short ability, bool goalkeeper) {
            if (kind == Kind.Raw) {
                return stored;
            }
            return Forward(stored, ability, UsesHighBranch(kind, goalkeeper));
        }

        static sbyte FromDisplay(Kind kind, int shown, short ability, bool goalkeeper) {
            if (kind == Kind.Raw) {
                return (sbyte) shown;
            }
            bool high = UsesHighBranch(kind, goalkeeper);
            // invert shown = t^2/30 + t/3 + 0.5  ->  t = -5 + sqrt(10 + 30*shown),
            // then intrinsic = (t - 10 - ability/w) * 10; refine around the estimate
            // because the forward mapping truncates
            double t = -5.0 + Math.Sqrt(10.0 + 30.0 * shown);
            int estimate = (int) Math.Round((t - 10.0 - ability / (high ? 20.0 : 200.0)) * 10.0);
            // clamp into the sbyte range first: some targets are unreachable for
            // extreme CA values (a CA-200 player cannot display 1) - in that case the
            // nearest achievable intrinsic wins
            int center = Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, estimate));
            int best = center, bestDistance = int.MaxValue;
            for (int candidate = Math.Max(sbyte.MinValue, center - 15);
                 candidate <= Math.Min(sbyte.MaxValue, center + 15); candidate++) {
                int distance = Math.Abs(Forward((sbyte) candidate, ability, high) - shown);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return (sbyte) best;
        }
    }
}
