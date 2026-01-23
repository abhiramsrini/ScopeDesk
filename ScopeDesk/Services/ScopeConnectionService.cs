using Microsoft.Extensions.Logging;
using ScopeDesk.Models;
using System;
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
