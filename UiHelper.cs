using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    static class UiHelper {
        internal static readonly int[] MonthDays = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        internal static readonly string[] MonthNames = {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        internal static int DaysInMonth(int month, int year) {
            return month == 1 && year % 4 == 0 ? 29 : MonthDays[month];
        }

        /// <summary>Editable combo with the manual (Wine-safe) append autocomplete.</summary>
        internal static ComboBox MakeAutoCompleteCombo(int width) {
            ComboBox combo = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = width
            };
            ComboBoxAutoComplete.Attach(combo);
            return combo;
        }
        /// <summary>
        /// Gives every control an explicit TabIndex following the order it was
        /// added to its container (which in these code-built forms is the visual
        /// order). Without this all controls share TabIndex 0 and the Tab key
        /// has no defined path through the form.
        /// </summary>
        internal static void AssignTabOrder(Control parent) {
            int index = 0;
            foreach (Control child in parent.Controls) {
                child.TabIndex = index++;
                // recurse into layout containers only; composite controls like
                // NumericUpDown manage their own internal children
                if (child is Panel || child is GroupBox || child is TabControl) {
                    AssignTabOrder(child);
                }
            }
        }
    }
}
