# Project Context

## Purpose
ScopeDesk is a .NET 7 WPF client to connect to a LeCroy oscilloscope via ActiveDSO, select channels/measurements, and fetch measurement values with branded styling and rolling logs.

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
- COM interop: LeCroy.ActiveDSO (via ProgID `LeCroy.ActiveDSO`)

## Configuration Defaults
- Default IP: `192.168.0.100`
- Logging: `%LocalAppData%/ScopeDesk/logs/scope.log`, ~5 MB per file, retain 10

## Key Components
- `Services/ScopeConnectionService`: ActiveDSO connect/disconnect (`MakeConnection("IP:<addr>")`), SCPI send helper
- `Services/MeasurementService`: VBS measurement calls mapped to P1–P8 slots (Amplitude, Mean, Rise, Fall, PeakToPeak, Frequency, Width, Period); uses stub values if COM missing
- `ViewModels/MainViewModel`: connection state, checkbox-based channel/measurement selection (all preselected), fetch command, SCPI command/response, status, open logs command
- `MainWindow.xaml`: UI layout with header/logo, connection + SCPI panel, checkbox selectors, results grid, footer with Primeasure link

## Expected Usage
1) Enter scope IP (LAN), Connect.
2) Choose/toggle channels and measurements (all start selected).
3) Fetch measurements; results render in grid with timestamps.
4) Optionally send SCPI command and read response.
5) Open logs folder if needed for diagnostics.
