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
        private bool _isConnected;
        private string _statusMessage = "Disconnected";
        private string _footerMessage = string.Empty;

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
            ChannelOptions = new ObservableCollection<ChannelOption>(BuildChannelOptions());
            SelectedChannel = ChannelOptions.FirstOrDefault();

            MeasurementOptions = new ObservableCollection<SelectableMeasurementOption>(BuildMeasurementOptions());
            WireMeasurementSelection();
            var allMeasurement = MeasurementOptions.FirstOrDefault(o => o.IsAll);
            if (allMeasurement != null)
            {
                allMeasurement.IsSelected = true;
            }

            Results = new ObservableCollection<MeasurementResult>();

            FooterMessage = $"Logs: {Path.GetDirectoryName(_logPath)}";

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
            FetchMeasurementsCommand = new AsyncRelayCommand(FetchMeasurementsAsync, () => IsConnected);
            OpenLogsCommand = new RelayCommand(OpenLogsFolder);
        }

        public ObservableCollection<ChannelOption> ChannelOptions { get; }
        public ObservableCollection<SelectableMeasurementOption> MeasurementOptions { get; }
        public ObservableCollection<MeasurementResult> Results { get; }

        private ChannelOption? _selectedChannel;
        public ChannelOption? SelectedChannel
        {
            get => _selectedChannel;
            set => SetProperty(ref _selectedChannel, value);
        }

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
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

        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand FetchMeasurementsCommand { get; }
        public IRelayCommand OpenLogsCommand { get; }

        private IEnumerable<ChannelOption> BuildChannelOptions()
        {
            yield return new ChannelOption { Id = "ALL", DisplayName = "All Channels", IsAll = true };
            yield return new ChannelOption { Id = "C1", DisplayName = "Channel 1" };
            yield return new ChannelOption { Id = "C2", DisplayName = "Channel 2" };
            yield return new ChannelOption { Id = "C3", DisplayName = "Channel 3" };
            yield return new ChannelOption { Id = "C4", DisplayName = "Channel 4" };
        }

        private IEnumerable<SelectableMeasurementOption> BuildMeasurementOptions()
        {
            var items = new List<SelectableMeasurementOption>
            {
                new SelectableMeasurementOption { Id = "ALL", DisplayName = "All measurements", IsAll = true },
                new SelectableMeasurementOption { Id = "Mean", DisplayName = "Mean" },
                new SelectableMeasurementOption { Id = "Amplitude", DisplayName = "Amplitude" },
                new SelectableMeasurementOption { Id = "Frequency", DisplayName = "Frequency" },
                new SelectableMeasurementOption { Id = "Rise Time", DisplayName = "Rise Time" },
                new SelectableMeasurementOption { Id = "Fall Time", DisplayName = "Fall Time" },
                new SelectableMeasurementOption { Id = "Peak-to-Peak", DisplayName = "Peak-to-Peak" },
                new SelectableMeasurementOption { Id = "Width", DisplayName = "Width" },
                new SelectableMeasurementOption { Id = "Period", DisplayName = "Period" }
            };

            return items;
        }

        private void WireMeasurementSelection()
        {
            foreach (var option in MeasurementOptions)
            {
                option.PropertyChanged += MeasurementOption_PropertyChanged;
            }
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
            }
        }

        private async Task DisconnectAsync()
        {
            StatusMessage = "Disconnecting...";
            await _connectionService.DisconnectAsync();
            IsConnected = false;
            StatusMessage = "Disconnected";
        }

        private async Task FetchMeasurementsAsync()
        {
            try
            {
                var measurementTargets = GetSelectedMeasurements().ToList();
                var channels = GetSelectedChannels().ToList();

                Results.Clear();

                var results = await _measurementService.FetchMeasurementsAsync(measurementTargets, channels);

                foreach (var result in results)
                {
                    Results.Add(result);
                }

                StatusMessage = $"Fetched {results.Count} measurement(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = "Failed to fetch measurements.";
                _logger.LogError(ex, "Error fetching measurements.");
            }
        }

        private void MeasurementOption_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SelectableMeasurementOption.IsSelected) || sender is not SelectableMeasurementOption changed)
            {
                return;
            }

            if (changed.IsAll)
            {
                foreach (var option in MeasurementOptions.Where(o => !o.IsAll))
                {
                    option.IsSelected = changed.IsSelected;
                }
            }
            else
            {
                var allOption = MeasurementOptions.FirstOrDefault(o => o.IsAll);
                if (allOption == null)
                {
                    return;
                }

                if (!changed.IsSelected)
                {
                    allOption.IsSelected = false;
                }
                else if (MeasurementOptions.Where(o => !o.IsAll).All(o => o.IsSelected))
                {
                    allOption.IsSelected = true;
                }
            }
        }

        private IEnumerable<MeasurementOption> GetSelectedMeasurements()
        {
            var allSelected = MeasurementOptions.FirstOrDefault(o => o.IsAll)?.IsSelected == true;
            var selected = MeasurementOptions.Where(o => o.IsSelected && !o.IsAll).ToList();

            if (allSelected || selected.Count == 0)
            {
                return MeasurementOptions.Where(o => !o.IsAll)
                    .Select(o => new MeasurementOption { Id = o.Id, DisplayName = o.DisplayName });
            }

            return selected.Select(o => new MeasurementOption { Id = o.Id, DisplayName = o.DisplayName });
        }

        private IEnumerable<ChannelOption> GetSelectedChannels()
        {
            if (SelectedChannel == null || SelectedChannel.IsAll)
            {
                return ChannelOptions.Where(c => !c.IsAll).ToList();
            }

            return new List<ChannelOption> { SelectedChannel };
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
    }
}
