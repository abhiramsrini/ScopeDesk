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
using System.Threading.Tasks;

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
        private string _scpiCommand = string.Empty;
        private string _scpiResponse = string.Empty;
        private bool _isConnected;
        private string _statusMessage = "Disconnected";
        private string _footerMessage = string.Empty;
        private DateTime? _latestTimestamp;
        private string _serialNumber = "-";

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
            ChannelOptions = new ObservableCollection<SelectableChannelOption>(BuildChannelOptions());
            MeasurementOptions = new ObservableCollection<SelectableMeasurementOption>(BuildMeasurementOptions());

            MatrixChannels = new ObservableCollection<string>();
            MatrixRows = new ObservableCollection<MeasurementMatrixRow>();

            FooterMessage = $"Logs: {Path.GetDirectoryName(_logPath)}";

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
            FetchMeasurementsCommand = new AsyncRelayCommand(FetchMeasurementsAsync, () => IsConnected);
            SendScpiCommand = new AsyncRelayCommand(SendScpiCommandAsync, () => IsConnected && !string.IsNullOrWhiteSpace(ScpiCommand));
            OpenLogsCommand = new RelayCommand(OpenLogsFolder);
            ClearMatrixCommand = new RelayCommand(ClearMatrix);
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

        public IReadOnlyList<string> MatrixHeaders => new[] { "Measurement" }.Concat(MatrixChannels).ToList();
        public int MatrixColumnCount => MatrixChannels.Count + 1;

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand FetchMeasurementsCommand { get; }
        public IAsyncRelayCommand SendScpiCommand { get; }
        public IRelayCommand OpenLogsCommand { get; }
        public IRelayCommand ClearMatrixCommand { get; }

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
            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                StatusMessage = "Enter a valid IP address.";
                return;
            }

            StatusMessage = "Connecting...";
            var success = await _connectionService.ConnectAsync(IpAddress);

            IsConnected = success;
            StatusMessage = success ? $"Connected to {IpAddress}" : $"Failed to connect to {IpAddress}";

            if (success)
            {
                _logger.LogInformation("Connected to scope at {Ip}", IpAddress);
                await LoadSerialNumberAsync();
            }
        }

        private async Task DisconnectAsync()
        {
            StatusMessage = "Disconnecting...";
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

        private async Task FetchMeasurementsAsync()
        {
            try
            {
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

                    rows.Add(new MeasurementMatrixRow
                    {
                        Measurement = measurement.DisplayName,
                        Cells = cells
                    });
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
    }
}
