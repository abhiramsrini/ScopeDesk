using System.Collections.Generic;

namespace ScopeDesk.Models
{
    public class MeasurementMatrixRow
    {
        public string Measurement { get; init; } = string.Empty;
        public IReadOnlyList<string> Cells { get; init; } = new List<string>();
    }
}
