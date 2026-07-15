using System;
using System.Drawing;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Search dialog to pick any staff member (player, manager, coach...) from the
    /// save, used by the liked/disliked staff editors. SelectedId is the chosen
    /// staff id, or -1 when "None" was picked.
    /// </summary>
    class StaffPickerDialog : Form {
        readonly SaveGame save;
        readonly TextBox search;
        readonly ListBox results;

        public int SelectedId { get; private set; }

        public StaffPickerDialog(SaveGame save) {
            this.save = save;
            this.SelectedId = -1;
            this.Text = "Pick a person";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(420, 420);

            Label hint = new Label { Text = "Type at least 3 letters of the name:", AutoSize = true, Location = new Point(12, 12) };
            this.search = new TextBox { Location = new Point(12, 32), Width = 396 };
            this.search.TextChanged += (s, e) => RefreshResults();
            this.results = new ListBox { Location = new Point(12, 62), Size = new Size(396, 300) };
            this.results.DoubleClick += (s, e) => Accept();

            Button ok = new Button { Text = "OK", Location = new Point(140, 375), Size = new Size(85, 30) };
            ok.Click += (s, e) => Accept();
            Button none = new Button { Text = "None", Location = new Point(233, 375), Size = new Size(85, 30) };
            none.Click += (s, e) => { this.SelectedId = -1; this.DialogResult = DialogResult.OK; };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(326, 375), Size = new Size(85, 30) };
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.Controls.AddRange(new Control[] { hint, this.search, this.results, ok, none, cancel });
        }

        void RefreshResults() {
            this.results.BeginUpdate();
            this.results.Items.Clear();
            string needle = this.search.Text.Trim().ToLower();
            if (needle.Length >= 3) {
                foreach (SaveGame.StaffEntry entry in this.save.StaffDirectory) {
                    if (entry.Name.ToLower().Contains(needle)) {
                        this.results.Items.Add(new StaffItem { Entry = entry });
                        if (this.results.Items.Count >= 200) break;
                    }
                }
            }
            this.results.EndUpdate();
        }

        void Accept() {
            StaffItem item = this.results.SelectedItem as StaffItem;
            if (item == null) return;
            this.SelectedId = item.Entry.Id;
            this.DialogResult = DialogResult.OK;
        }

        class StaffItem {
            public SaveGame.StaffEntry Entry;
            public override string ToString() {
                return Entry.Name + "  (" + Entry.ClubName + ")";
            }
        }
    }
}
