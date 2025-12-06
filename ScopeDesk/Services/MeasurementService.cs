using Microsoft.Extensions.Logging;
using ScopeDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeDesk.Services
{
    public class MeasurementService
    {
        private readonly ScopeConnectionService _connectionService;
        private readonly ILogger<MeasurementService> _logger;
        private readonly Random _random = new Random();
        private readonly Dictionary<string, (string paramEngine, int slot)> _measurementMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Amplitude", ("Amplitude", 1) },
            { "Mean", ("Mean", 2) },
            { "Rise Time", ("Rise", 3) },
            { "Fall Time", ("Fall", 4) },
            { "Peak-to-Peak", ("PeakToPeak", 5) },
            { "Frequency", ("Frequency", 6) },
            { "Width", ("Width", 7) },
            { "Period", ("Period", 8) }
        };

        public MeasurementService(ScopeConnectionService connectionService, ILogger<MeasurementService> logger)
        {
            _connectionService = connectionService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<MeasurementResult>> FetchMeasurementsAsync(
            IReadOnlyCollection<MeasurementOption> measurements,
            IReadOnlyCollection<ChannelOption> channels,
            CancellationToken cancellationToken = default)
        {
            if (!_connectionService.IsConnected)
            {
                throw new InvalidOperationException("Oscilloscope is not connected.");
            }

            return await Task.Run(() =>
            {
                var results = new List<MeasurementResult>();
                var measurementTargets = measurements.Where(m => !m.IsAll).ToList();
                var channelTargets = channels.Where(c => !c.IsAll).ToList();

                if (measurementTargets.Count == 0 || channelTargets.Count == 0)
                {
                    return results;
                }

                foreach (var channel in channelTargets)
                {
                    foreach (var measurement in measurementTargets)
                    {
                        string value;
                        try
                        {
                            value = ReadMeasurement(channel.Id, measurement.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to read {Measurement} on {Channel}", measurement.DisplayName, channel.DisplayName);
                            value = "N/A";
                        }

                        results.Add(new MeasurementResult
                        {
                            Channel = channel.DisplayName,
                            Measurement = measurement.DisplayName,
                            Value = value,
                            Timestamp = DateTime.Now
                        });
                    }
                }

                _logger.LogInformation("Fetched {Count} measurements across {ChannelCount} channel(s).", results.Count, channelTargets.Count);
                return (IReadOnlyList<MeasurementResult>)results;
            }, cancellationToken);
        }

        private string ReadMeasurement(string channelId, string measurementId)
        {
            if (!_connectionService.HasScopeObject)
            {
                return GenerateStubValue(measurementId);
            }

            if (!_measurementMap.TryGetValue(measurementId, out var map))
            {
                throw new InvalidOperationException($"Measurement mapping not found for {measurementId}");
            }

            var scope = _connectionService.GetScope();
            if (scope == null)
            {
                return GenerateStubValue(measurementId);
            }

            scope.WriteString($"VBS 'app.Measure.P{map.slot}.ParamEngine = \"{map.paramEngine}\"'", 1);
            scope.WriteString($"VBS 'app.Measure.P{map.slot}.Source1 = \"{channelId}\"'", 1);
            scope.WriteString($"VBS? 'return=app.Measure.P{map.slot}.Out.Result.Value'", 1);
            var value = scope.ReadString(100);

            return value is string s ? s.Trim() : value?.ToString() ?? "N/A";
        }

        private string GenerateStubValue(string measurementId)
        {
            var baseValue = _random.NextDouble() * 10;

            return measurementId.ToLowerInvariant() switch
            {
                "frequency" => $"{baseValue * 10_000:F2} Hz",
                "period" => $"{1 / Math.Max(baseValue, 0.001):F6} s",
                "amplitude" => $"{baseValue:F3} V",
                "mean" => $"{baseValue / 2:F3} V",
                "rise time" => $"{Math.Max(baseValue / 1000, 0.0001):F6} s",
                "fall time" => $"{Math.Max(baseValue / 1000, 0.0001):F6} s",
                "duty cycle" => $"{Math.Min(baseValue * 10, 100):F2} %",
                "rms" => $"{baseValue / 3:F3} V",
                "peak-to-peak" => $"{baseValue * 1.2:F3} V",
                "max" => $"{baseValue * 1.5:F3} V",
                "min" => $"{baseValue * 0.5:F3} V",
                _ => $"{baseValue:F3}"
            };
        }
    }
}
