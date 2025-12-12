using Microsoft.Extensions.Logging;
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

        public bool IsConnected { get; private set; }
        public bool HasScopeObject => _scopeCom != null;

        public ScopeConnectionService(ILogger<ScopeConnectionService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ConnectAsync(string ipAddress, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                return true;
            }

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

                    _scopeCom?.MakeConnection($"TCPIP:{ipAddress}", string.Empty, string.Empty, string.Empty);

                    IsConnected = true;
                    _logger.LogInformation("Connected to oscilloscope at {Ip}", ipAddress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to oscilloscope at {Ip}", ipAddress);
                    IsConnected = false;
                }

                return IsConnected;
            }, cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
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
                            _scopeCom = null;
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

                    // Write command and read response
                    _scopeCom.WriteString(command, 1);
                    _scopeCom.WriteString("CHDR OFF", 1); // ensure clean response format if applicable
                    _scopeCom.WriteString("?", 1);

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

                    _scopeCom.WriteString("VBS? 'return=app.Instrument.SerialNumber'", 1);
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
    }
}
