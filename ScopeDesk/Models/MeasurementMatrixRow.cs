using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace ScopeDesk.Models
{
    // Represents a single measurement row in the matrix. Cells[0] is the measurement label, remaining indices align to channels.
    public class MeasurementMatrixRow : ObservableObject
    {
        public string Measurement { get; init; } = string.Empty;

        public ObservableCollection<string> Cells { get; } = new();

        public void SetCells(IReadOnlyList<string> values)
        {
            Cells.Clear();
            foreach (var v in values)
            {
                Cells.Add(v);
            }
            OnPropertyChanged(nameof(Cells));
        }
    }
}
