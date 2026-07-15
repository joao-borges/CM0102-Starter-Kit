using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Player editor dialog (Save Editor phase 2). Attribute maths from Nick's
    /// CM0102Patcher Scouter: a subset of playing attributes is stored as CA-weighted
    /// intrinsic sbytes; the in-game 1-20 value is
    ///   t = intrinsic/10 + CA/w + 10;  shown = trunc(t*t/30 + t/3 + 0.5)
    /// with w = 20 ("high" branch) or 200 ("low" branch); which branch applies to the
    /// goalkeeping/outfield technical attributes depends on whether the player is a
    /// goalkeeper (GK position rating >= 15). Editing inverts that mapping against the
    /// CA/GK values currently in the dialog.
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

        readonly SaveGame save;
        readonly SaveGame.PlayerRef player;
        readonly List<Attr> attributes = new List<Attr>();
        readonly NumericUpDown[] positions = new NumericUpDown[12];
        NumericUpDown ability, potential, homeRep, currentRep, worldRep, value, wage, squadNumber;

        public PlayerEditForm(SaveGame save, SaveGame.PlayerRef player) {
            this.save = save;
            this.player = player;
            this.Text = "Edit Player - " + player.Name + " (" + player.ClubName + ", age " + player.Age + ")";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(890, 590);
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

        void BuildControls() {
            GroupBox general = new GroupBox { Text = "General (0-200 scales / raw values)", Location = new Point(10, 8), Size = new Size(870, 80) };
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

            GroupBox positionsBox = new GroupBox { Text = "Positions (0-20)", Location = new Point(10, 96), Size = new Size(870, 92) };
            for (int i = 0; i < 12; i++) {
                int column = i % 6, row = i / 6;
                positionsBox.Controls.Add(new Label { Text = PositionNames[i], Location = new Point(12 + column * 143, 22 + row * 34), AutoSize = true });
                this.positions[i] = MakeNumeric(0, 20);
                this.positions[i].Location = new Point(97 + column * 143, 18 + row * 34);
                positionsBox.Controls.Add(this.positions[i]);
            }

            GroupBox attributesBox = new GroupBox { Text = "Playing attributes (as shown in game, 1-20)", Location = new Point(10, 196), Size = new Size(870, 330) };
            for (int i = 0; i < this.attributes.Count; i++) {
                int column = i % 6, row = i / 6;
                Attr attribute = this.attributes[i];
                attributesBox.Controls.Add(new Label { Text = attribute.Label, Location = new Point(12 + column * 143, 24 + row * 42), AutoSize = true });
                attribute.Control = MakeNumeric(1, 20);
                attribute.Control.Location = new Point(15 + column * 143, 40 + row * 42);
                attributesBox.Controls.Add(attribute.Control);
            }

            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(690, 545), Size = new Size(90, 30) };
            ok.Click += (s, e) => WriteValues();
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(790, 545), Size = new Size(90, 30) };
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.Controls.AddRange(new Control[] { general, positionsBox, attributesBox, ok, cancel });
        }

        bool IsGoalkeeper { get { return this.positions[0].Value >= 15; } }

        void ReadValues() {
            int recordBase = this.player.PlayerBase;
            this.squadNumber.Value = Clamp(this.save.ReadByte(recordBase + 4), 0, 99);
            this.ability.Value = Clamp(this.save.ReadInt16(recordBase + 5), 1, 200);
            this.potential.Value = Clamp(this.save.ReadInt16(recordBase + 7), 1, 200);
            // reputations live on a 0-10000 scale in saves; shown 0-200 like the DB
            this.homeRep.Value = Clamp((decimal) Math.Round(this.save.ReadInt16(recordBase + 9) / 50.0), 0, 200);
            this.currentRep.Value = Clamp((decimal) Math.Round(this.save.ReadInt16(recordBase + 11) / 50.0), 0, 200);
            this.worldRep.Value = Clamp((decimal) Math.Round(this.save.ReadInt16(recordBase + 13) / 50.0), 0, 200);
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
        }

        void WriteValues() {
            int recordBase = this.player.PlayerBase;
            this.save.WriteByte(recordBase + 4, (byte) this.squadNumber.Value);
            this.save.WriteInt16(recordBase + 5, (short) this.ability.Value);
            this.save.WriteInt16(recordBase + 7, (short) Math.Max(this.potential.Value, this.ability.Value));
            this.save.WriteInt16(recordBase + 9, (short) (this.homeRep.Value * 50));
            this.save.WriteInt16(recordBase + 11, (short) (this.currentRep.Value * 50));
            this.save.WriteInt16(recordBase + 13, (short) (this.worldRep.Value * 50));
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
