using Microsoft.Extensions.Logging;
using ScopeDesk.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace ScopeDesk.Services
{
    public class ScopeConnectionService : IAsyncDisposable
    {
        private readonly ILogger<ScopeConnectionService> _logger;
        private dynamic? _scopeCom;
        private bool _headerConfigured;
        private ConnectionType _connectionType = ConnectionType.TcpIp;

        public bool IsConnected { get; private set; }
        public bool HasScopeObject => _scopeCom != null;

        public ScopeConnectionService(ILogger<ScopeConnectionService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConnectAsync(string target, ConnectionType connectionType = ConnectionType.TcpIp, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                return true;
            }

            _connectionType = connectionType;

            return await Task.Run(() =>
            {
                try
                {
                    var type = Type.GetTypeFromProgID("LeCroy.ActiveDSOCtrl.1") ??
                               Type.GetTypeFromProgID("LeCroy.ActiveDSOCtrl");

                    if (type == null)
                    {
                        _logger.LogWarning("ActiveDSO COM component not found. Running in stub mode (no scope calls will be made).");
                        IsConnected = true;
                        return IsConnected;
                    }

                    _scopeCom = Activator.CreateInstance(type);

                    var connectionString = BuildConnectionString(target, connectionType);
                    _scopeCom?.MakeConnection(connectionString);
                    TryDisableCommandHeaders();

                    IsConnected = true;
                    _logger.LogInformation("Connected to oscilloscope via {Mode} ({Target})", connectionType, target);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to oscilloscope via {Mode} ({Target})", connectionType, target);
                    IsConnected = false;
                }

                return IsConnected;
            }, cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected && _scopeCom == null)
            {
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    if (_scopeCom != null)
                    {
                        try
                        {
                            _scopeCom?.Disconnect();
                        }
                        catch (COMException)
                        {
                            // Some ActiveDSO variants do not expose a disconnect call; swallow if missing.
                        }
                        finally
                        {
                            ReleaseScopeObject();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while disconnecting from oscilloscope.");
                }
                finally
                {
                    IsConnected = false;
                    _logger.LogInformation("Disconnected from oscilloscope.");
                }
            }, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask(DisconnectAsync());
        }

        public dynamic? GetScope() => _scopeCom;

        public async Task<string> SendScpiCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Oscilloscope is not connected.");
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (_scopeCom == null)
                    {
                        _logger.LogWarning("Scope object not available; returning stub response.");
                        return "Scope object not available.";
                    }

                    if (!_headerConfigured)
                    {
                        TryDisableCommandHeaders();
                    }

                    // Write command exactly as provided and read response
                    _scopeCom.WriteString(command, 1);

                    var response = _scopeCom.ReadString(5000);
                    return response is string s ? s.Trim() : response?.ToString() ?? "No response.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send SCPI command: {Command}", command);
                    return $"Error: {ex.Message}";
                }
            }, cancellationToken);
        }

        public async Task<string> GetSerialNumberAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Oscilloscope is not connected.");
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (_scopeCom == null)
                    {
                        _logger.LogWarning("Scope object not available; returning stub serial.");
                        return "Stub (no COM)";
                    }

                    _scopeCom.WriteString("VBS? 'return=app.InstrumentID'", 1);
                    var serial = _scopeCom.ReadString(100);
                    return serial is string s ? s.Trim() : serial?.ToString() ?? "N/A";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read scope serial number.");
                    return "Serial unavailable";
                }
            }, cancellationToken);
        }

        public async Task CaptureScreenAsync(string destinationPath, string format = "PNG", CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Oscilloscope is not connected.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

            if (_scopeCom == null)
            {
                _logger.LogWarning("Scope object not available; writing placeholder capture.");
                WriteStubImage(destinationPath);
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Configure hardcopy to stream image back to host
                    _scopeCom.WriteString($"HCSU DEV,{format}", 1);
                    _scopeCom.WriteString("HCSU DEST,REMOTE", 1);
                    _scopeCom.WriteString("SCDP", 1);

                    var data = TryReadBinaryBlock();
                    if (data == null || data.Count == 0)
                    {
                        throw new InvalidOperationException("Scope returned no image data.");
                    }

                    File.WriteAllBytes(destinationPath, data.ToArray());
                    _logger.LogInformation("Captured screen to {Path}", destinationPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to capture oscilloscope screen.");
                    throw;
                }
            }, cancellationToken);
        }

        private IReadOnlyList<byte>? TryReadBinaryBlock()
        {
            // Attempt common binary read methods exposed by ActiveDSO; fall back to parsing SCPI definite-length block from string.
            try
            {
                var bytes = _scopeCom?.ReadByteArray(20_000_000);
                if (bytes is byte[] arr && arr.Length > 0) return arr;
            }
            catch { }

            try
            {
                var bytes = _scopeCom?.ReadBinaryBlock(20_000_000);
                if (bytes is byte[] arr && arr.Length > 0) return arr;
            }
            catch { }

            try
            {
                var bytes = _scopeCom?.ReadBinary(20_000_000);
                if (bytes is byte[] arr && arr.Length > 0) return arr;
            }
            catch { }

            try
            {
                var response = _scopeCom?.ReadString(20_000_000);
                if (response is string s && !string.IsNullOrEmpty(s))
                {
                    var parsed = ParseDefiniteLengthBlock(s);
                    if (parsed.Count > 0) return parsed;
                }
            }
            catch { }

            return null;
        }

        private static IReadOnlyList<byte> ParseDefiniteLengthBlock(string data)
        {
            // SCPI definite-length block: #{digits}{len}{payload}
            if (string.IsNullOrEmpty(data) || data[0] != '#')
            {
                return Encoding.ASCII.GetBytes(data);
            }

            if (data.Length < 3) return Array.Empty<byte>();

            if (!int.TryParse(data[1].ToString(), out var digitCount) || digitCount <= 0)
            {
                return Array.Empty<byte>();
            }

            if (data.Length < 2 + digitCount) return Array.Empty<byte>();

            var lenText = data.Substring(2, digitCount);
            if (!int.TryParse(lenText, out var payloadLength) || payloadLength < 0)
            {
                return Array.Empty<byte>();
            }

            var start = 2 + digitCount;
            if (data.Length < start + payloadLength)
            {
                payloadLength = Math.Max(0, data.Length - start);
            }

            return Encoding.ASCII.GetBytes(data.Substring(start, payloadLength));
        }

        private static void WriteStubImage(string destinationPath)
        {
            // 1x1 transparent PNG
            var stubPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/P6RykQAAAABJRU5ErkJggg==");
            File.WriteAllBytes(destinationPath, stubPng);
        }

        private static string BuildConnectionString(string target, ConnectionType type)
        {
            return type switch
            {
                ConnectionType.UsbTmc => $"USBTMC:{target}",
                _ => $"TCPIP:{target}"
            };
        }

        private void TryDisableCommandHeaders()
        {
            try
            {
                _scopeCom?.WriteString("CHDR OFF", 1);
                _headerConfigured = true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to disable command headers (CHDR OFF).");
            }
        }

        private void ReleaseScopeObject()
        {
            try
            {
                if (_scopeCom != null && Marshal.IsComObject(_scopeCom))
                {
                    Marshal.FinalReleaseComObject(_scopeCom);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to release ActiveDSO COM object.");
            }
            finally
            {
                _scopeCom = null;
                _headerConfigured = false;
            }
        }
    }
}
