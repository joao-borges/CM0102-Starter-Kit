using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Editor dialog for non-player staff (managers, coaches, scouts, retired
    /// players...). Edits the staff.dat personal fields (birth date, nationalities,
    /// caps/goals, value, mentals), the contract wage, the nonplayer.dat coaching
    /// record (CA/PA, reputations, 21 coaching attributes stored as CA-weighted
    /// intrinsics - same "high branch" maths as player attributes) and the
    /// Preferences.dat likes/dislikes. Manager wages are NOT in contract.dat and
    /// have no known on-disk home (man_conf.dat is board-confidence data), so the
    /// wage field stays disabled for staff without a contract record.
    /// Coaching attributes display clamped to 1-20 (the true value of an elite
    /// manager can exceed 20), so only attributes the user actually changed are
    /// written back.
    /// </summary>
    class StaffEditForm : Form {
        static readonly string[] MentalNames = {
            "Adaptability", "Ambition", "Determination", "Loyalty",
            "Pressure", "Professionalism", "Sportsmanship", "Temperament"
        };

        // nonplayer.dat record +14..+34, alphabetical storage order
        static readonly string[] CoachingNames = {
            "Attacking", "Business", "Coaching", "Coaching GKs", "Coaching Tech.",
            "Directness", "Discipline", "Free Roles", "Interference", "Judging Ability",
            "Judging Potential", "Man Handling", "Marking", "Motivating", "Offside",
            "Patience", "Physiotherapy", "Pressing", "Resources", "Tactics", "Youngsters"
        };

        readonly SaveGame save;
        readonly SaveGame.PlayerRef person;
        NumericUpDown dobDay, dobYear, caps, intGoals, value, wage;
        ComboBox dobMonth, nationality, secondNationality;
        Label ageLabel;
        int originalDobDay, originalDobMonth, originalDobYear;
        readonly NumericUpDown[] mentals = new NumericUpDown[8];
        // Coaching tab (nonplayer.dat); all null when the person has no record
        NumericUpDown ability, potential, homeRep, currentRep, worldRep;
        readonly NumericUpDown[] coaching = new NumericUpDown[21];
        int originalAbility, originalPotential, originalHomeRep, originalCurrentRep, originalWorldRep;
        readonly int[] originalCoaching = new int[21];
        readonly ComboBox[] prefClubs = new ComboBox[6];
        readonly TextBox[] prefStaffBoxes = new TextBox[6];
        readonly int[] prefStaffIds = new int[6];
        readonly Dictionary<int, string> clubIdToName = new Dictionary<int, string>();
        readonly Dictionary<string, int> clubNameToId = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        readonly Dictionary<string, int> nationNameToId = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        public StaffEditForm(SaveGame save, SaveGame.PlayerRef person) {
            this.save = save;
            this.person = person;
            this.Text = "Edit Staff - " + person.Name + " (" + person.ClubName + ")";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(900, 620);
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
            BuildControls();
            ReadValues();
            UiHelper.AssignTabOrder(this);
        }

        static NumericUpDown MakeNumeric(int min, int max) {
            return new NumericUpDown { Minimum = min, Maximum = max, Width = 55 };
        }

        void BuildControls() {
            TabControl tabs = new TabControl { Location = new Point(10, 8), Size = new Size(880, 565) };
            TabPage personalPage = new TabPage("Personal");
            TabPage coachingPage = new TabPage("Coaching");
            TabPage prefsPage = new TabPage("Likes && Dislikes");
            tabs.TabPages.Add(personalPage);
            tabs.TabPages.Add(coachingPage);
            tabs.TabPages.Add(prefsPage);
            BuildPersonalPage(personalPage);
            BuildCoachingPage(coachingPage);
            BuildPrefsPage(prefsPage);

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(690, 582), Size = new Size(90, 30) };
            ok.Click += (s, e) => WriteValues();
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(790, 582), Size = new Size(90, 30) };
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.Controls.AddRange(new Control[] { tabs, ok, cancel });
        }

        void BuildPersonalPage(TabPage page) {
            GroupBox personal = new GroupBox { Text = "Personal details", Location = new Point(8, 8), Size = new Size(856, 120) };
            personal.Controls.Add(new Label { Text = "Born", Location = new Point(12, 28), AutoSize = true });
            this.dobDay = MakeNumeric(1, 31);
            this.dobDay.Width = 45;
            this.dobDay.Location = new Point(58, 24);
            personal.Controls.Add(this.dobDay);
            this.dobMonth = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(110, 24), Width = 100
            };
            this.dobMonth.Items.AddRange(UiHelper.MonthNames);
            personal.Controls.Add(this.dobMonth);
            this.dobYear = MakeNumeric(1920, 2090);
            this.dobYear.Width = 62;
            this.dobYear.Location = new Point(217, 24);
            personal.Controls.Add(this.dobYear);
            this.ageLabel = new Label { Text = "", Location = new Point(290, 28), AutoSize = true, ForeColor = Color.DimGray };
            personal.Controls.Add(this.ageLabel);
            EventHandler refreshAge = (s, e) => RefreshAgeLabel();
            this.dobDay.ValueChanged += refreshAge;
            this.dobMonth.SelectedIndexChanged += refreshAge;
            this.dobYear.ValueChanged += refreshAge;

            personal.Controls.Add(new Label { Text = "Value", Location = new Point(400, 28), AutoSize = true });
            this.value = MakeNumeric(0, 500000000);
            this.value.Width = 95;
            this.value.ThousandsSeparator = true;
            this.value.Location = new Point(450, 24);
            personal.Controls.Add(this.value);
            personal.Controls.Add(new Label { Text = "Wage", Location = new Point(570, 28), AutoSize = true });
            this.wage = MakeNumeric(0, 5000000);
            this.wage.Width = 80;
            this.wage.ThousandsSeparator = true;
            this.wage.Location = new Point(620, 24);
            personal.Controls.Add(this.wage);

            personal.Controls.Add(new Label { Text = "Nationality", Location = new Point(12, 66), AutoSize = true });
            this.nationality = UiHelper.MakeAutoCompleteCombo(180);
            this.nationality.Location = new Point(90, 62);
            personal.Controls.Add(this.nationality);
            personal.Controls.Add(new Label { Text = "2nd nat.", Location = new Point(290, 66), AutoSize = true });
            this.secondNationality = UiHelper.MakeAutoCompleteCombo(180);
            this.secondNationality.Location = new Point(355, 62);
            personal.Controls.Add(this.secondNationality);
            List<object> nationItems = new List<object>();
            foreach (SaveGame.Nation nation in this.save.Nations) {
                nationItems.Add(nation.Name);
            }
            this.nationality.Items.AddRange(nationItems.ToArray());
            nationItems.Insert(0, "(none)");
            this.secondNationality.Items.AddRange(nationItems.ToArray());
            personal.Controls.Add(new Label { Text = "Int'l caps", Location = new Point(570, 66), AutoSize = true });
            this.caps = MakeNumeric(0, 255);
            this.caps.Location = new Point(640, 62);
            personal.Controls.Add(this.caps);
            personal.Controls.Add(new Label { Text = "Goals", Location = new Point(710, 66), AutoSize = true });
            this.intGoals = MakeNumeric(0, 255);
            this.intGoals.Location = new Point(760, 62);
            personal.Controls.Add(this.intGoals);

            GroupBox mentalsBox = new GroupBox { Text = "Mental attributes (0-20)", Location = new Point(8, 136), Size = new Size(856, 120) };
            for (int i = 0; i < 8; i++) {
                int column = i % 4, row = i / 4;
                mentalsBox.Controls.Add(new Label { Text = MentalNames[i], Location = new Point(12 + column * 212, 26 + row * 44), AutoSize = true });
                this.mentals[i] = MakeNumeric(0, 20);
                this.mentals[i].Location = new Point(130 + column * 212, 22 + row * 44);
                mentalsBox.Controls.Add(this.mentals[i]);
            }

            page.Controls.AddRange(new Control[] { personal, mentalsBox });
        }

        void BuildCoachingPage(TabPage page) {
            if (this.person.NonPlayerBase < 0) {
                page.Controls.Add(new Label {
                    Text = "This person has no coaching record in the save.",
                    Location = new Point(14, 20), AutoSize = true, ForeColor = Color.DimGray
                });
                return;
            }
            GroupBox general = new GroupBox { Text = "General (0-200 scales)", Location = new Point(8, 8), Size = new Size(856, 80) };
            this.ability = MakeNumeric(1, 200);
            this.potential = MakeNumeric(1, 200);
            this.currentRep = MakeNumeric(0, 200);
            this.homeRep = MakeNumeric(0, 200);
            this.worldRep = MakeNumeric(0, 200);
            object[][] generalFields = {
                new object[] { "Current Ability", this.ability }, new object[] { "Potential Ability", this.potential },
                new object[] { "Cur. Reputation", this.currentRep }, new object[] { "Home Rep.", this.homeRep },
                new object[] { "World Rep.", this.worldRep }
            };
            int x = 12;
            foreach (object[] field in generalFields) {
                Control control = (Control) field[1];
                general.Controls.Add(new Label { Text = (string) field[0], Location = new Point(x, 22), AutoSize = true });
                control.Location = new Point(x, 42);
                general.Controls.Add(control);
                x += control.Width + 60;
            }

            GroupBox attributesBox = new GroupBox {
                Text = "Coaching attributes (as shown in game, 1-20; elite values above 20 show as 20)",
                Location = new Point(8, 96), Size = new Size(856, 210)
            };
            for (int i = 0; i < 21; i++) {
                int column = i % 6, row = i / 6;
                attributesBox.Controls.Add(new Label { Text = CoachingNames[i], Location = new Point(12 + column * 141, 24 + row * 42), AutoSize = true });
                this.coaching[i] = MakeNumeric(1, 20);
                this.coaching[i].Location = new Point(15 + column * 141, 40 + row * 42);
                attributesBox.Controls.Add(this.coaching[i]);
            }

            page.Controls.AddRange(new Control[] { general, attributesBox });
        }

        void BuildPrefsPage(TabPage page) {
            if (this.person.PrefsBase < 0) {
                page.Controls.Add(new Label {
                    Text = "This save has no preferences record for this person.",
                    Location = new Point(14, 20), AutoSize = true, ForeColor = Color.DimGray
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

            string[] clubTitles = { "Favourite clubs", "Disliked clubs" };
            for (int group = 0; group < 2; group++) {
                GroupBox box = new GroupBox { Text = clubTitles[group], Location = new Point(6 + group * 434, 8), Size = new Size(426, 140) };
                for (int slot = 0; slot < 3; slot++) {
                    ComboBox combo = UiHelper.MakeAutoCompleteCombo(392);
                    combo.Location = new Point(15, 24 + slot * 36);
                    combo.Items.AddRange(clubItemArray);
                    box.Controls.Add(combo);
                    this.prefClubs[group * 3 + slot] = combo;
                }
                page.Controls.Add(box);
            }

            string[] staffTitles = { "Favourite people", "Disliked people" };
            for (int group = 0; group < 2; group++) {
                GroupBox box = new GroupBox { Text = staffTitles[group], Location = new Point(6 + group * 434, 156), Size = new Size(426, 140) };
                for (int slot = 0; slot < 3; slot++) {
                    int index = group * 3 + slot;
                    TextBox display = new TextBox { Location = new Point(15, 24 + slot * 36), Width = 332, ReadOnly = true };
                    Button pick = new Button { Text = "...", Location = new Point(354, 23 + slot * 36), Size = new Size(38, 24) };
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

        void ReadValues() {
            this.value.Value = Math.Min(Math.Max(this.save.ReadInt32(this.person.StaffBase + 82), 0), 500000000);
            if (this.person.ContractBase >= 0) {
                this.wage.Value = Math.Min(Math.Max(this.save.ReadInt32(this.person.ContractBase + 12), 0), 5000000);
            } else {
                this.wage.Enabled = false;
            }
            this.caps.Value = this.save.ReadByte(this.person.StaffBase + 34);
            this.intGoals.Value = this.save.ReadByte(this.person.StaffBase + 35);
            SelectNation(this.nationality, this.save.ReadInt32(this.person.StaffBase + 26), false);
            SelectNation(this.secondNationality, this.save.ReadInt32(this.person.StaffBase + 30), true);
            for (int i = 0; i < 8; i++) {
                this.mentals[i].Value = Math.Min(Math.Max((int) this.save.ReadByte(this.person.StaffBase + 86 + i), 0), 20);
            }

            int birthYear = this.save.ReadInt16(this.person.StaffBase + 18);
            int birthDayOfYear = this.save.ReadInt16(this.person.StaffBase + 16);
            if (birthYear > 1800 && birthDayOfYear >= 0 && birthDayOfYear <= 365) {
                int month = 0, remaining = birthDayOfYear + 1;
                while (month < 11 && remaining > UiHelper.DaysInMonth(month, birthYear)) {
                    remaining -= UiHelper.DaysInMonth(month, birthYear);
                    month++;
                }
                this.originalDobDay = remaining;
                this.originalDobMonth = month;
                this.originalDobYear = birthYear;
                this.dobYear.Value = Math.Min(Math.Max(birthYear, (int) this.dobYear.Minimum), (int) this.dobYear.Maximum);
                this.dobMonth.SelectedIndex = month;
                this.dobDay.Value = Math.Min(Math.Max(remaining, 1), 31);
            } else {
                this.dobDay.Enabled = this.dobMonth.Enabled = this.dobYear.Enabled = false;
                this.ageLabel.Text = "(no birth date)";
            }
            RefreshAgeLabel();

            if (this.person.NonPlayerBase >= 0) {
                int recordBase = this.person.NonPlayerBase;
                short ability16 = this.save.ReadInt16(recordBase + 4);
                this.originalAbility = Math.Min(Math.Max((int) ability16, 1), 200);
                this.originalPotential = Math.Min(Math.Max((int) this.save.ReadInt16(recordBase + 6), 1), 200);
                // reputations stored x50 of the 0-200 scale, like players
                this.originalHomeRep = Math.Min(Math.Max((int) Math.Round(this.save.ReadInt16(recordBase + 8) / 50.0), 0), 200);
                this.originalCurrentRep = Math.Min(Math.Max((int) Math.Round(this.save.ReadInt16(recordBase + 10) / 50.0), 0), 200);
                this.originalWorldRep = Math.Min(Math.Max((int) Math.Round(this.save.ReadInt16(recordBase + 12) / 50.0), 0), 200);
                this.ability.Value = this.originalAbility;
                this.potential.Value = this.originalPotential;
                this.homeRep.Value = this.originalHomeRep;
                this.currentRep.Value = this.originalCurrentRep;
                this.worldRep.Value = this.originalWorldRep;
                for (int i = 0; i < 21; i++) {
                    sbyte stored = this.save.ReadSByte(recordBase + 14 + i);
                    this.originalCoaching[i] = Math.Min(Math.Max(
                        PlayerEditForm.ShownFromIntrinsicHigh(stored, ability16), 1), 20);
                    this.coaching[i].Value = this.originalCoaching[i];
                }
            }

            if (this.person.PrefsBase >= 0) {
                for (int i = 0; i < 6; i++) {
                    int clubId = this.save.ReadInt32(this.person.PrefsBase + 4 + i * 4);
                    string clubName;
                    this.prefClubs[i].Text = clubId >= 0 && this.clubIdToName.TryGetValue(clubId, out clubName)
                        ? clubName : "(none)";
                    this.prefStaffIds[i] = this.save.ReadInt32(this.person.PrefsBase + 28 + i * 4);
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

        int CurrentAge(int birthDayOfYear, int birthYear) {
            int age = this.save.GameYear - birthYear;
            if (this.save.GameDay < birthDayOfYear) {
                age--;
            }
            return Math.Max(age, 0);
        }

        void RefreshAgeLabel() {
            if (!this.dobDay.Enabled || this.dobMonth.SelectedIndex < 0) {
                return;
            }
            int year = (int) this.dobYear.Value;
            int month = this.dobMonth.SelectedIndex;
            int dayOfYear = Math.Min((int) this.dobDay.Value, UiHelper.DaysInMonth(month, year)) - 1;
            for (int m = 0; m < month; m++) {
                dayOfYear += UiHelper.DaysInMonth(m, year);
            }
            this.ageLabel.Text = "Age: " + CurrentAge(dayOfYear, year);
        }

        void WriteValues() {
            this.save.WriteInt32(this.person.StaffBase + 82, (int) this.value.Value);
            if (this.person.ContractBase >= 0) {
                this.save.WriteInt32(this.person.ContractBase + 12, (int) this.wage.Value);
            }
            this.save.WriteByte(this.person.StaffBase + 34, (byte) this.caps.Value);
            this.save.WriteByte(this.person.StaffBase + 35, (byte) this.intGoals.Value);
            this.save.WriteInt32(this.person.StaffBase + 26,
                ResolveNation(this.nationality, this.save.ReadInt32(this.person.StaffBase + 26)));
            this.save.WriteInt32(this.person.StaffBase + 30,
                ResolveNation(this.secondNationality, this.save.ReadInt32(this.person.StaffBase + 30)));
            for (int i = 0; i < 8; i++) {
                this.save.WriteByte(this.person.StaffBase + 86 + i, (byte) this.mentals[i].Value);
            }

            if (this.dobDay.Enabled &&
                ((int) this.dobDay.Value != this.originalDobDay ||
                 this.dobMonth.SelectedIndex != this.originalDobMonth ||
                 (int) this.dobYear.Value != this.originalDobYear)) {
                int year = (int) this.dobYear.Value;
                int month = this.dobMonth.SelectedIndex;
                int dayOfMonth = Math.Min((int) this.dobDay.Value, UiHelper.DaysInMonth(month, year));
                int dayOfYear = dayOfMonth - 1;          // 0-based store
                for (int m = 0; m < month; m++) {
                    dayOfYear += UiHelper.DaysInMonth(m, year);
                }
                this.save.WriteInt16(this.person.StaffBase + 16, (short) dayOfYear);
                this.save.WriteInt16(this.person.StaffBase + 18, (short) year);
                this.save.WriteInt32(this.person.StaffBase + 20, year % 4 == 0 ? 1 : 0);
                this.person.Age = CurrentAge(dayOfYear, year);
            }

            if (this.person.NonPlayerBase >= 0) {
                int recordBase = this.person.NonPlayerBase;
                if ((int) this.ability.Value != this.originalAbility) {
                    this.save.WriteInt16(recordBase + 4, (short) this.ability.Value);
                }
                if ((int) this.potential.Value != this.originalPotential ||
                    (int) this.ability.Value > this.originalPotential) {
                    this.save.WriteInt16(recordBase + 6,
                        (short) Math.Max(this.potential.Value, this.ability.Value));
                }
                if ((int) this.homeRep.Value != this.originalHomeRep) {
                    this.save.WriteInt16(recordBase + 8, (short) (this.homeRep.Value * 50));
                }
                if ((int) this.currentRep.Value != this.originalCurrentRep) {
                    this.save.WriteInt16(recordBase + 10, (short) (this.currentRep.Value * 50));
                }
                if ((int) this.worldRep.Value != this.originalWorldRep) {
                    this.save.WriteInt16(recordBase + 12, (short) (this.worldRep.Value * 50));
                }
                // the 1-20 display is lossy (clamped at 20, truncated maths), so only
                // attributes the user touched are re-encoded against the dialog CA
                short newAbility16 = (short) this.ability.Value;
                for (int i = 0; i < 21; i++) {
                    if ((int) this.coaching[i].Value != this.originalCoaching[i]) {
                        this.save.WriteSByte(recordBase + 14 + i,
                            PlayerEditForm.IntrinsicFromShownHigh((int) this.coaching[i].Value, newAbility16));
                    }
                }
            }

            if (this.person.PrefsBase >= 0) {
                for (int i = 0; i < 6; i++) {
                    int offset = this.person.PrefsBase + 4 + i * 4;
                    string text = this.prefClubs[i].Text.Trim();
                    if (text.Length == 0 || text == "(none)") {
                        this.save.WriteInt32(offset, -1);
                    } else {
                        int clubId;
                        if (this.clubNameToId.TryGetValue(text, out clubId)) {
                            this.save.WriteInt32(offset, clubId);
                        }
                    }
                    this.save.WriteInt32(this.person.PrefsBase + 28 + i * 4, this.prefStaffIds[i]);
                }
            }
        }
    }
}
