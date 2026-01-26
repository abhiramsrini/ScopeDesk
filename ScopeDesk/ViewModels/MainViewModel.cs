using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScopeDesk.Models;
using ScopeDesk.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ScopeDesk.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly ScopeConnectionService _connectionService;
        private readonly MeasurementService _measurementService;
        private readonly ILogger<MainViewModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _logPath;

        private string _ipAddress = "192.168.0.100";
        private string _visaResource = string.Empty;
        private ConnectionType _connectionType = ConnectionType.TcpIp;
        private int _continuousIntervalMs = 500;
        private string _scpiCommand = string.Empty;
        private string _scpiResponse = string.Empty;
        private bool _isConnected;
        private bool _isContinuousRunning;
        private bool _isFetching;
        private bool _isBusy;
        private string _statusMessage = "Disconnected";
        private string _footerMessage = string.Empty;
        private DateTime? _latestTimestamp;
        private string _serialNumber = "-";
        private CancellationTokenSource? _continuousCts;
        private bool _isContinuousFetching;
        private bool _isShuttingDown;

        public MainViewModel(
            ScopeConnectionService connectionService,
            MeasurementService measurementService,
            ILogger<MainViewModel> logger,
            IConfiguration configuration)
        {
            _connectionService = connectionService;
            _measurementService = measurementService;
            _logger = logger;
            _configuration = configuration;

            _logPath = Environment.ExpandEnvironmentVariables(_configuration["Logging:File:Path"] ?? "%LocalAppData%/ScopeDesk/logs/scope.log");

            IpAddress = _configuration["Connection:DefaultIp"] ?? _ipAddress;
            VisaResource = _configuration["Connection:DefaultVisaResource"] ?? "USB0::0x05FF::0xFFFF::SERIAL::INSTR";
            ConnectionType = Enum.TryParse(_configuration["Connection:DefaultInterface"], true, out ConnectionType parsedType)
                ? parsedType
                : ConnectionType.TcpIp;
            _continuousIntervalMs = int.TryParse(_configuration["Connection:ContinuousIntervalMs"], out var interval)
                ? Math.Max(interval, 100)
                : 500;
            ChannelOptions = new ObservableCollection<SelectableChannelOption>(BuildChannelOptions());
            MeasurementOptions = new ObservableCollection<SelectableMeasurementOption>(BuildMeasurementOptions());

            MatrixChannels = new ObservableCollection<string>();
            MatrixRows = new ObservableCollection<MeasurementMatrixRow>();

            FooterMessage = $"Logs: {Path.GetDirectoryName(_logPath)}";

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
            FetchMeasurementsCommand = new AsyncRelayCommand(FetchMeasurementsAsync, () => IsConnected && !_isBusy && !IsContinuousRunning);
            SendScpiCommand = new AsyncRelayCommand(SendScpiCommandAsync, () => IsConnected && !string.IsNullOrWhiteSpace(ScpiCommand));
            OpenLogsCommand = new RelayCommand(OpenLogsFolder);
            ClearMatrixCommand = new RelayCommand(ClearMatrix);
            StartContinuousCommand = new AsyncRelayCommand(StartContinuousAsync, () => IsConnected && !IsContinuousRunning && !_isBusy);
            StopContinuousCommand = new AsyncRelayCommand(StopContinuousAsync, () => IsContinuousRunning);
            ExportCsvCommand = new RelayCommand(ExportCsv, CanExportCsv);
            CaptureScreenCommand = new AsyncRelayCommand(CaptureScreenAsync, () => IsConnected);
        }

        public ObservableCollection<SelectableChannelOption> ChannelOptions { get; }
        public ObservableCollection<SelectableMeasurementOption> MeasurementOptions { get; }
        public ObservableCollection<string> MatrixChannels { get; }
        public ObservableCollection<MeasurementMatrixRow> MatrixRows { get; }

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public string VisaResource
        {
            get => _visaResource;
            set => SetProperty(ref _visaResource, value);
        }

        public ConnectionType ConnectionType
        {
            get => _connectionType;
            set => SetProperty(ref _connectionType, value);
        }

        public string ScpiCommand
        {
            get => _scpiCommand;
            set
            {
                if (SetProperty(ref _scpiCommand, value))
                {
                    SendScpiCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ScpiResponse
        {
            get => _scpiResponse;
            set => SetProperty(ref _scpiResponse, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatusText));
                    ConnectCommand.NotifyCanExecuteChanged();
                    DisconnectCommand.NotifyCanExecuteChanged();
                    FetchMeasurementsCommand.NotifyCanExecuteChanged();
                    SendScpiCommand.NotifyCanExecuteChanged();
                    StartContinuousCommand.NotifyCanExecuteChanged();
                    StopContinuousCommand.NotifyCanExecuteChanged();
                    CaptureScreenCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string ConnectionStatusText => IsConnected ? "Connected" : "Disconnected";

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string FooterMessage
        {
            get => _footerMessage;
            set => SetProperty(ref _footerMessage, value);
        }

        public DateTime? LatestTimestamp
        {
            get => _latestTimestamp;
            private set => SetProperty(ref _latestTimestamp, value);
        }

        public string SerialNumber
        {
            get => _serialNumber;
            private set => SetProperty(ref _serialNumber, value);
        }

        public bool IsContinuousRunning
        {
            get => _isContinuousRunning;
            private set
            {
                if (SetProperty(ref _isContinuousRunning, value))
                {
                    StartContinuousCommand.NotifyCanExecuteChanged();
                    StopContinuousCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(ContinuousStatusText));
                }
            }
        }

        public string ContinuousStatusText => IsContinuousRunning ? "Running..." : "Idle";

        public IReadOnlyList<string> MatrixHeaders => new[] { "Measurement" }.Concat(MatrixChannels).ToList();
        public int MatrixColumnCount => MatrixChannels.Count + 1;

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand FetchMeasurementsCommand { get; }
        public IAsyncRelayCommand SendScpiCommand { get; }
        public IRelayCommand OpenLogsCommand { get; }
        public IRelayCommand ClearMatrixCommand { get; }
        public IAsyncRelayCommand StartContinuousCommand { get; }
        public IAsyncRelayCommand StopContinuousCommand { get; }
        public IRelayCommand ExportCsvCommand { get; }
        public IAsyncRelayCommand CaptureScreenCommand { get; }

        public async Task ShutdownAsync()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;

            try
            {
                StatusMessage = "Closing connection...";
                _logger.LogInformation("Shutdown started: stopping continuous mode and disconnecting.");
                await StopContinuousAsync();
                await _connectionService.DisconnectAsync();
                IsConnected = false;
                StatusMessage = "Disconnected";
                _logger.LogInformation("Shutdown cleanup completed.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Shutdown cleanup failed.");
            }
        }

        private IEnumerable<SelectableChannelOption> BuildChannelOptions()
        {
            yield return new SelectableChannelOption { Id = "C1", DisplayName = "Channel 1", IsSelected = true };
            yield return new SelectableChannelOption { Id = "C2", DisplayName = "Channel 2", IsSelected = true };
            yield return new SelectableChannelOption { Id = "C3", DisplayName = "Channel 3", IsSelected = true };
            yield return new SelectableChannelOption { Id = "C4", DisplayName = "Channel 4", IsSelected = true };
        }

        private IEnumerable<SelectableMeasurementOption> BuildMeasurementOptions()
        {
            var items = new List<SelectableMeasurementOption>
            {
                new SelectableMeasurementOption { Id = "Mean", DisplayName = "Mean", IsSelected = true },
                new SelectableMeasurementOption { Id = "Amplitude", DisplayName = "Amplitude", IsSelected = true },
                new SelectableMeasurementOption { Id = "Frequency", DisplayName = "Frequency", IsSelected = true },
                new SelectableMeasurementOption { Id = "Rise Time", DisplayName = "Rise Time", IsSelected = true },
                new SelectableMeasurementOption { Id = "Fall Time", DisplayName = "Fall Time", IsSelected = true },
                new SelectableMeasurementOption { Id = "Peak-to-Peak", DisplayName = "Peak-to-Peak", IsSelected = true },
                new SelectableMeasurementOption { Id = "Width", DisplayName = "Width", IsSelected = true },
                new SelectableMeasurementOption { Id = "Period", DisplayName = "Period", IsSelected = true }
            };

            return items;
        }

        private async Task ConnectAsync()
        {
            string target = ConnectionType == ConnectionType.TcpIp ? IpAddress : VisaResource;

            if (string.IsNullOrWhiteSpace(target))
            {
                StatusMessage = ConnectionType == ConnectionType.TcpIp
                    ? "Enter a valid IP address."
                    : "Enter a valid VISA resource string.";
                return;
            }

            StatusMessage = "Connecting...";
            var success = await _connectionService.ConnectAsync(target, ConnectionType);

            IsConnected = success;
            StatusMessage = success
                ? $"Connected to {ConnectionType} ({target})"
                : $"Failed to connect to {ConnectionType} ({target})";

            if (success)
            {
                _logger.LogInformation("Connected to scope via {Mode} ({Target})", ConnectionType, target);
                await LoadSerialNumberAsync();
            }
        }

        private async Task DisconnectAsync()
        {
            StatusMessage = "Disconnecting...";
            await StopContinuousAsync();
            await _connectionService.DisconnectAsync();
            IsConnected = false;
            StatusMessage = "Disconnected";
            SerialNumber = "-";
        }

        private async Task SendScpiCommandAsync()
        {
            try
            {
                StatusMessage = "Sending SCPI command...";
                var response = await _connectionService.SendScpiCommandAsync(ScpiCommand);
                ScpiResponse = response;
                StatusMessage = "SCPI command sent.";
            }
            catch (Exception ex)
            {
                ScpiResponse = $"Error: {ex.Message}";
                StatusMessage = "Failed to send SCPI command.";
                _logger.LogError(ex, "Error sending SCPI command.");
            }
        }

        private Task FetchMeasurementsAsync()
        {
            return FetchMeasurementsInternalAsync(allowBusy: false);
        }

        private async Task FetchMeasurementsInternalAsync(bool allowBusy)
        {
            if (_isFetching || (_isBusy && !allowBusy))
            {
                return;
            }

            try
            {
                _isFetching = true;
                _isBusy = true;
                FetchMeasurementsCommand.NotifyCanExecuteChanged();
                StartContinuousCommand.NotifyCanExecuteChanged();

                var measurementTargets = GetSelectedMeasurements().ToList();
                var channels = GetSelectedChannels().ToList();

                MatrixRows.Clear();
                MatrixChannels.Clear();

                var results = await _measurementService.FetchMeasurementsAsync(measurementTargets, channels);

                foreach (var channel in channels)
                {
                    MatrixChannels.Add(channel.DisplayName);
                }

                LatestTimestamp = results.FirstOrDefault()?.Timestamp ?? DateTime.Now;

                var lookup = results.ToLookup(r => (r.Measurement, r.Channel), r => r.Value);
                var rows = new List<MeasurementMatrixRow>();

                foreach (var measurement in measurementTargets)
                {
                    var cells = new List<string> { measurement.DisplayName };
                    foreach (var channel in channels)
                    {
                        var value = lookup[(measurement.DisplayName, channel.DisplayName)].FirstOrDefault() ?? "-";
                        cells.Add(value);
                    }

                    var row = new MeasurementMatrixRow
                    {
                        Measurement = measurement.DisplayName
                    };
                    row.SetCells(cells);
                    rows.Add(row);
                }

                foreach (var row in rows)
                {
                    MatrixRows.Add(row);
                }

                UpdateMatrixMetadata();
                StatusMessage = $"Fetched {results.Count} measurement(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to fetch measurements.";
                _logger.LogError(ex, "Error fetching measurements.");
            }
            finally
            {
                _isFetching = false;
                _isBusy = false;
                ExportCsvCommand.NotifyCanExecuteChanged();
                FetchMeasurementsCommand.NotifyCanExecuteChanged();
                StartContinuousCommand.NotifyCanExecuteChanged();
            }
        }

        private IEnumerable<MeasurementOption> GetSelectedMeasurements()
        {
            var selected = MeasurementOptions.Where(o => o.IsSelected).ToList();

            if (selected.Count == 0)
            {
                return MeasurementOptions
                    .Select(o => new MeasurementOption { Id = o.Id, DisplayName = o.DisplayName });
            }

            return selected.Select(o => new MeasurementOption { Id = o.Id, DisplayName = o.DisplayName });
        }

        private IEnumerable<ChannelOption> GetSelectedChannels()
        {
            var selected = ChannelOptions.Where(c => c.IsSelected).ToList();

            if (selected.Count == 0)
            {
                return ChannelOptions
                    .Select(c => new ChannelOption { Id = c.Id, DisplayName = c.DisplayName });
            }

            return selected.Select(c => new ChannelOption { Id = c.Id, DisplayName = c.DisplayName });
        }

        private void OpenLogsFolder()
        {
            try
            {
                var directory = Path.GetDirectoryName(_logPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                var startInfo = new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open logs folder.");
            }
        }

        private void ClearMatrix()
        {
            MatrixRows.Clear();
            MatrixChannels.Clear();
            LatestTimestamp = null;
            UpdateMatrixMetadata();
            StatusMessage = "Matrix cleared.";
            ExportCsvCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadSerialNumberAsync()
        {
            try
            {
                SerialNumber = await _connectionService.GetSerialNumberAsync();
            }
            catch (Exception ex)
            {
                SerialNumber = "Serial unavailable";
                _logger.LogWarning(ex, "Failed to load serial number.");
            }
        }

        private void UpdateMatrixMetadata()
        {
            OnPropertyChanged(nameof(MatrixHeaders));
            OnPropertyChanged(nameof(MatrixColumnCount));
        }

        private async Task StartContinuousAsync()
        {
            if (IsContinuousRunning || !IsConnected)
            {
                return;
            }

            _continuousCts = new CancellationTokenSource();
            _isBusy = true;
            FetchMeasurementsCommand.NotifyCanExecuteChanged();
            StartContinuousCommand.NotifyCanExecuteChanged();
            StopContinuousCommand.NotifyCanExecuteChanged();
            IsContinuousRunning = true;
            StatusMessage = $"Continuous run started (every {_continuousIntervalMs} ms).";

            await RunContinuousAsync(_continuousCts.Token);
        }

        private async Task RunContinuousAsync(CancellationToken token)
        {
            var cts = _continuousCts;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await UpdateMatrixValuesAsync(token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Continuous fetch iteration failed.");
                }

                try
                {
                    await Task.Delay(_continuousIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            IsContinuousRunning = false;
            _isBusy = false;
            StatusMessage = "Continuous run stopped.";
            cts?.Dispose();
            _continuousCts = null;
            FetchMeasurementsCommand.NotifyCanExecuteChanged();
            StartContinuousCommand.NotifyCanExecuteChanged();
            StopContinuousCommand.NotifyCanExecuteChanged();
        }

        private async Task UpdateMatrixValuesAsync(CancellationToken token)
        {
            if (_isContinuousFetching)
            {
                return;
            }

            _isContinuousFetching = true;

            try
            {
                var measurementTargets = GetSelectedMeasurements().ToList();
                var channels = GetSelectedChannels().ToList();

                // If the structure is empty (e.g., first run), build it once.
                if (!MatrixRows.Any() || MatrixChannels.Count == 0)
                {
                    await FetchMeasurementsInternalAsync(allowBusy: true);
                    return;
                }

                var results = await _measurementService.FetchMeasurementsAsync(measurementTargets, channels, token);

                LatestTimestamp = results.FirstOrDefault()?.Timestamp ?? DateTime.Now;

                var lookup = results.ToLookup(r => (r.Measurement, r.Channel), r => r.Value);

                foreach (var row in MatrixRows)
                {
                    var updated = new List<string> { row.Measurement };
                    foreach (var channel in channels)
                    {
                        var value = lookup[(row.Measurement, channel.DisplayName)].FirstOrDefault() ?? "-";
                        updated.Add(value);
                    }

                    row.SetCells(updated);
                }

                StatusMessage = $"Updated {results.Count} measurement(s).";
            }
            catch (OperationCanceledException)
            {
                // Swallow cancellation to allow graceful stop.
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to update measurements.";
                _logger.LogWarning(ex, "UpdateMatrixValuesAsync failed.");
            }
            finally
            {
                _isContinuousFetching = false;
            }
        }

        private async Task StopContinuousAsync()
        {
            if (_continuousCts == null)
            {
                return;
            }

            try
            {
                _continuousCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            IsContinuousRunning = false;
            StatusMessage = "Continuous run stopped.";
            _isBusy = false;
            FetchMeasurementsCommand.NotifyCanExecuteChanged();
            StartContinuousCommand.NotifyCanExecuteChanged();
            StopContinuousCommand.NotifyCanExecuteChanged();
            await Task.CompletedTask;
        }

        private async Task CaptureScreenAsync()
        {
            if (!IsConnected)
            {
                StatusMessage = "Connect to the scope before capturing.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|Bitmap (*.bmp)|*.bmp|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|All Files (*.*)|*.*",
                FileName = $"ScopeDesk_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AddExtension = true,
                DefaultExt = "png",
                OverwritePrompt = true
            };

            var result = dialog.ShowDialog();
            if (result != true)
            {
                StatusMessage = "Screen capture canceled.";
                return;
            }

            var selectedExt = Path.GetExtension(dialog.FileName).ToUpperInvariant();
            var format = selectedExt switch
            {
                ".BMP" => "BMP",
                ".JPG" or ".JPEG" => "JPEG",
                _ => "PNG"
            };

            try
            {
                StatusMessage = "Capturing screen...";
                await _connectionService.CaptureScreenAsync(dialog.FileName, format);
                StatusMessage = $"Saved screenshot to {dialog.FileName}.";
                _logger.LogInformation("Captured scope screen to {File}", dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to capture screen.";
                _logger.LogError(ex, "Screen capture failed.");
            }
        }

        private bool CanExportCsv()
        {
            return MatrixRows.Any();
        }

        private void ExportCsv()
        {
            if (!MatrixRows.Any())
            {
                StatusMessage = "No measurements to export.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"ScopeDesk_Matrix_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                AddExtension = true,
                DefaultExt = "csv",
                OverwritePrompt = true
            };

            var result = dialog.ShowDialog();
            if (result != true)
            {
                StatusMessage = "Export canceled.";
                return;
            }

            try
            {
                var csv = BuildCsv();
                File.WriteAllText(dialog.FileName, csv, Encoding.UTF8);
                StatusMessage = $"Exported matrix to {dialog.FileName}.";
                _logger.LogInformation("Exported measurement matrix to {File}", dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to export CSV.";
                _logger.LogError(ex, "Error exporting measurement matrix.");
            }
        }

        private string BuildCsv()
        {
            var builder = new StringBuilder();

            if (LatestTimestamp.HasValue)
            {
                builder.Append("Timestamp,");
                builder.AppendLine(EscapeCsv(LatestTimestamp.Value.ToString("O")));
            }

            builder.AppendLine(string.Join(',', MatrixHeaders.Select(EscapeCsv)));

            foreach (var row in MatrixRows)
            {
                builder.AppendLine(string.Join(',', row.Cells.Select(EscapeCsv)));
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            var escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }
    }
}
