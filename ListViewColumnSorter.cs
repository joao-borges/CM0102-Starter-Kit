using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Click-to-sort state + row comparer for ListView columns: first click sorts
    /// ascending, second descending. Columns whose cells parse as numbers (thousand
    /// separators allowed) sort numerically, everything else alphabetically.
    /// The rows are sorted BEFORE they are added to the ListView - assigning a
    /// ListViewItemSorter to the control itself breaks scrolling under Wine.
    /// </summary>
    class ListViewColumnSorter : IComparer<ListViewItem> {
        int column;
        SortOrder order = SortOrder.None;

        /// <summary>Records the new sort choice; caller then rebuilds the list.</summary>
        public void HandleColumnClick(int clickedColumn) {
            if (this.column == clickedColumn && this.order == SortOrder.Ascending) {
                this.order = SortOrder.Descending;
            } else {
                this.column = clickedColumn;
                this.order = SortOrder.Ascending;
            }
        }

        public void Apply(List<ListViewItem> rows) {
            if (this.order != SortOrder.None) {
                rows.Sort(this);
            }
        }

        public int Compare(ListViewItem left, ListViewItem right) {
            if (this.order == SortOrder.None) {
                return 0;
            }
            string leftText = CellText(left), rightText = CellText(right);
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
