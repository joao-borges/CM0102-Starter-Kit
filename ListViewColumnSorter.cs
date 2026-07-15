using System;
using System.Collections;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Click-to-sort for ListView columns: first click ascending, second descending.
    /// Columns whose cells parse as numbers (thousand separators allowed) sort
    /// numerically, everything else alphabetically.
    /// </summary>
    class ListViewColumnSorter : IComparer {
        int column;
        SortOrder order = SortOrder.None;

        public void HandleColumnClick(ListView list, int clickedColumn) {
            if (this.column == clickedColumn && this.order == SortOrder.Ascending) {
                this.order = SortOrder.Descending;
            } else {
                this.column = clickedColumn;
                this.order = SortOrder.Ascending;
            }
            list.Sort();
        }

        public void Reset() {
            this.order = SortOrder.None;
        }

        public int Compare(object left, object right) {
            if (this.order == SortOrder.None) {
                return 0;
            }
            string leftText = CellText((ListViewItem) left), rightText = CellText((ListViewItem) right);
            decimal leftNumber, rightNumber;
            int result =
                decimal.TryParse(leftText.Replace(",", ""), out leftNumber) &&
                decimal.TryParse(rightText.Replace(",", ""), out rightNumber)
                    ? leftNumber.CompareTo(rightNumber)
                    : string.Compare(leftText, rightText, StringComparison.CurrentCultureIgnoreCase);
            return this.order == SortOrder.Descending ? -result : result;
        }

        string CellText(ListViewItem item) {
            return this.column < item.SubItems.Count ? item.SubItems[this.column].Text : "";
        }
    }
}
