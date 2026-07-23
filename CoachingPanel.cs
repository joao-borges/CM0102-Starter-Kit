using System;
using System.Drawing;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Shared "Coaching" tab content for StaffEditForm and PlayerEditForm
    /// (player-managers have both a player and a nonplayer record). Edits the
    /// nonplayer.dat record: CA/PA, reputations (x50 scale) and the 21 coaching
    /// attributes, stored as CA-weighted intrinsics on the high (CA/20) branch.
    /// The 1-20 display is lossy (clamped at 20 for elite staff, truncated
    /// maths), so only fields the user actually changed are written back.
    /// </summary>
    class CoachingPanel {
        // nonplayer.dat record +14..+34, alphabetical storage order
        static readonly string[] CoachingNames = {
            "Attacking", "Business", "Coaching", "Coaching GKs", "Coaching Tech.",
            "Directness", "Discipline", "Free Roles", "Interference", "Judging Ability",
            "Judging Potential", "Man Handling", "Marking", "Motivating", "Offside",
            "Patience", "Physiotherapy", "Pressing", "Resources", "Tactics", "Youngsters"
        };

        readonly SaveGame save;
        readonly int recordBase;    // file offset of the nonplayer.dat record
        NumericUpDown ability, potential, homeRep, currentRep, worldRep;
        readonly NumericUpDown[] coaching = new NumericUpDown[21];
        int originalAbility, originalPotential, originalHomeRep, originalCurrentRep, originalWorldRep;
        readonly int[] originalCoaching = new int[21];

        public CoachingPanel(SaveGame save, int recordBase) {
            this.save = save;
            this.recordBase = recordBase;
        }

        static NumericUpDown MakeNumeric(int min, int max) {
            return new NumericUpDown { Minimum = min, Maximum = max, Width = 55 };
        }

        public void BuildInto(TabPage page) {
            GroupBox general = new GroupBox { Text = "Coaching ability && reputation (0-200 scales)", Location = new Point(8, 8), Size = new Size(856, 80) };
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

        public void ReadValues() {
            short ability16 = this.save.ReadInt16(this.recordBase + 4);
            this.originalAbility = Math.Min(Math.Max((int) ability16, 1), 200);
            this.originalPotential = Math.Min(Math.Max((int) this.save.ReadInt16(this.recordBase + 6), 1), 200);
            // reputations stored x50 of the 0-200 scale, like players
            this.originalHomeRep = Math.Min(Math.Max((int) Math.Round(this.save.ReadInt16(this.recordBase + 8) / 50.0), 0), 200);
            this.originalCurrentRep = Math.Min(Math.Max((int) Math.Round(this.save.ReadInt16(this.recordBase + 10) / 50.0), 0), 200);
            this.originalWorldRep = Math.Min(Math.Max((int) Math.Round(this.save.ReadInt16(this.recordBase + 12) / 50.0), 0), 200);
            this.ability.Value = this.originalAbility;
            this.potential.Value = this.originalPotential;
            this.homeRep.Value = this.originalHomeRep;
            this.currentRep.Value = this.originalCurrentRep;
            this.worldRep.Value = this.originalWorldRep;
            for (int i = 0; i < 21; i++) {
                sbyte stored = this.save.ReadSByte(this.recordBase + 14 + i);
                this.originalCoaching[i] = Math.Min(Math.Max(
                    PlayerEditForm.ShownFromIntrinsicHigh(stored, ability16), 1), 20);
                this.coaching[i].Value = this.originalCoaching[i];
            }
        }

        public void WriteValues() {
            if ((int) this.ability.Value != this.originalAbility) {
                this.save.WriteInt16(this.recordBase + 4, (short) this.ability.Value);
            }
            if ((int) this.potential.Value != this.originalPotential ||
                (int) this.ability.Value > this.originalPotential) {
                this.save.WriteInt16(this.recordBase + 6,
                    (short) Math.Max(this.potential.Value, this.ability.Value));
            }
            if ((int) this.homeRep.Value != this.originalHomeRep) {
                this.save.WriteInt16(this.recordBase + 8, (short) (this.homeRep.Value * 50));
            }
            if ((int) this.currentRep.Value != this.originalCurrentRep) {
                this.save.WriteInt16(this.recordBase + 10, (short) (this.currentRep.Value * 50));
            }
            if ((int) this.worldRep.Value != this.originalWorldRep) {
                this.save.WriteInt16(this.recordBase + 12, (short) (this.worldRep.Value * 50));
            }
            short newAbility16 = (short) this.ability.Value;
            for (int i = 0; i < 21; i++) {
                if ((int) this.coaching[i].Value != this.originalCoaching[i]) {
                    this.save.WriteSByte(this.recordBase + 14 + i,
                        PlayerEditForm.IntrinsicFromShownHigh((int) this.coaching[i].Value, newAbility16));
                }
            }
        }
    }
}
