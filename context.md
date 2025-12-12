# Project Context

## Purpose
ScopeDesk is a .NET 7 WPF client for LeCroy oscilloscopes. It connects over TCPIP using the ActiveDSO control, shows serial/status, fetches channel/measurement matrices, sends SCPI commands, and provides branded styling with rolling logs and an in-app logs shortcut. Stub mode keeps the UI usable when the COM control is unavailable.

## Branding
- Header on white with logo at `Resources/images/logo.png`
- Primary: `#241F61`
- Secondary: `#2F308B`
- Tertiary: `#ED1C24`
- Theme resource dictionary: `ScopeDesk/Resources/Theme.xaml`

## Tech Stack
- .NET 7 WPF, C#
- MVVM: CommunityToolkit.Mvvm
- Hosting/DI: Microsoft.Extensions.Hosting/DependencyInjection
- Logging: Serilog (rolling file sink, size-based retention)
- Configuration: appsettings.json
- COM interop: ActiveDSO control (ProgID `LeCroy.ActiveDSOCtrl.1` with `LeCroy.ActiveDSOCtrl` fallback; stub mode if missing)

## Configuration Defaults
- Default IP: `192.168.0.100` (`DefaultPort` stored but not used in the TCPIP connection string)
- Logging: `%LocalAppData%/ScopeDesk/logs/scope.log`, ~5 MB per file, retain 10, roll on size limit

## Key Components
- `Services/ScopeConnectionService`: ActiveDSO connect/disconnect (`MakeConnection("TCPIP:<addr>")`), `HasScope` flag for stub awareness, SCPI helper (writes command, `CHDR OFF`, `?`, reads response), serial fetch via VBS (`app.Instrument.SerialNumber`), handles controls lacking explicit `Disconnect`
- `Services/MeasurementService`: VBS measurement calls mapped to P1–P8 slots (Amplitude, Mean, Rise, Fall, PeakToPeak, Frequency, Width, Period); iterates selected channels/measurements and returns timestamped results; generates stub values if COM is missing
- `ViewModels/MainViewModel`: connection state and header status message, serial number display, checkbox-based channel/measurement selection (all preselected; falls back to all if none checked), fetch builds a measurement matrix (measurements as rows, channels as columns) with `LatestTimestamp`, clear matrix command, SCPI command/response binding, logs folder opener; default IP and log path pulled from config
- `MainWindow.xaml`: UI with branded header/status, connection + SCPI panels, channel/measurement checklists, matrix grid with timestamp and fetch/clear buttons, logs-folder shortcut, footer link to Primeasure

## Expected Usage
1) Enter scope IP, connect (serial number appears on success; stub mode applies if COM is absent).
2) Choose/toggle channels and measurements (all start selected; selecting none uses all).
3) Fetch measurements to populate the matrix (timestamp shown).
4) Optionally send SCPI commands and read responses; clear the matrix if needed.
5) Open the logs folder for diagnostics (paths resolved from config).
