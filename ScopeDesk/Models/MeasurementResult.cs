using System;

namespace ScopeDesk.Models
{
    public class MeasurementResult
    {
        public DateTime Timestamp { get; init; }
        public string Channel { get; init; } = string.Empty;
        public string Measurement { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
