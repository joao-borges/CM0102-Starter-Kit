using System;
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
