using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Manual append-style autocomplete for editable ComboBoxes. The built-in
    /// AutoCompleteMode/AutoCompleteSource pair crashes under Wine as soon as a
    /// character is typed (the suggestion popup relies on a shell COM interface),
    /// so this does the same job with FindString plus a text selection: type a
    /// prefix, the first matching item is filled in with the completed tail
    /// selected, keep typing (or press Backspace) to refine.
    /// </summary>
    static class ComboBoxAutoComplete {
        /// <summary>
        /// Contains-style filtering for the list-filter combos: typing rebuilds the
        /// dropdown to show ONLY items containing the typed text (anywhere in the
        /// name, case-insensitive) and opens it. The full choice list lives in
        /// combo.Tag as a string[] - callers must set it whenever the choices
        /// change (SetFilterItems in SaveEditorForm). No append-completion here:
        /// the typed text is left alone, it doubles as the grid filter.
        /// </summary>
        public static void AttachFilter(ComboBox combo) {
            bool[] updating = { false };
            combo.TextUpdate += (s, e) => {
                if (updating[0]) {
                    return;
                }
                string[] all = combo.Tag as string[];
                if (all == null) {
                    return;
                }
                string typed = combo.Text.Trim();
                List<string> matches = new List<string>();
                if (typed.Length == 0) {
                    matches.AddRange(all);
                } else {
                    foreach (string item in all) {
                        if (item.IndexOf(typed, StringComparison.CurrentCultureIgnoreCase) >= 0) {
                            matches.Add(item);
                        }
                    }
                }
                updating[0] = true;
                try {
                    string text = combo.Text;
                    combo.Items.Clear();
                    if (matches.Count > 0) {
                        combo.Items.AddRange(matches.ToArray());
                    }
                    if (typed.Length > 0 && matches.Count > 0 && !combo.DroppedDown) {
                        try {
                            combo.DroppedDown = true;
                            Cursor.Current = Cursors.Default;   // DroppedDown leaves a wait cursor
                        } catch (Exception) {
                            // headless/odd environments cannot open the popup; the
                            // items are filtered either way
                        }
                    }
                    // rebuilding/opening auto-selects an item under Mono WinForms,
                    // clobbering the box - re-assert the typed text LAST
                    if (combo.SelectedIndex != -1) {
                        combo.SelectedIndex = -1;
                    }
                    if (combo.Text != text) {
                        combo.Text = text;
                    }
                    combo.SelectionStart = text.Length;
                    combo.SelectionLength = 0;
                } finally {
                    updating[0] = false;
                }
            };
        }

        public static void Attach(ComboBox combo) {
            bool[] deleting = { false }, updating = { false };
            combo.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) {
                    deleting[0] = true;
                }
            };
            combo.TextUpdate += (s, e) => {
                if (updating[0]) {
                    return;
                }
                if (deleting[0]) {
                    deleting[0] = false;
                    return;
                }
                string typed = combo.Text;
                if (typed.Length == 0) {
                    return;
                }
                int index = combo.FindString(typed);
                if (index < 0) {
                    return;
                }
                string full = combo.Items[index].ToString();
                if (full.Length <= typed.Length) {
                    return;
                }
                updating[0] = true;
                try {
                    combo.Text = full;
                    combo.Select(typed.Length, full.Length - typed.Length);
                } finally {
                    updating[0] = false;
                }
            };
        }
    }
}
