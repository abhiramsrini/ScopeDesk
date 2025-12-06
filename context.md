# Project Context

## Purpose
ScopeDesk is a .NET 7 WPF client to connect to a LeCroy oscilloscope via ActiveDSO, select channels/measurements, and fetch measurement values with branded styling and rolling logs.

## Branding
- Primary: `#241F61`
- Secondary: `#2F308B`
- Tertiary: `#ED1C24`
- Theme resource dictionary: `ScopeDesk/Resources/Theme.xaml`
- Logo: placeholder slot in header (to be added later)

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
- `Services/ScopeConnectionService`: ActiveDSO connect/disconnect (`MakeConnection("IP:<addr>")`)
- `Services/MeasurementService`: VBS measurement calls mapped to P1–P8 slots (Amplitude, Mean, Rise, Fall, PeakToPeak, Frequency, Width, Period); uses stub values if COM missing
- `ViewModels/MainViewModel`: connection state, selection logic (All channels C1–C4; All measurements or specific), fetch command, status/footer, open logs command
- `MainWindow.xaml`: UI layout with connection panel, channel/measurement selectors, results grid, and log shortcut

## Expected Usage
1) Enter scope IP (LAN), Connect.
2) Choose channels (default: All) and measurements (default: All).
3) Fetch measurements; results render in grid with timestamps.
4) Open logs folder if needed for diagnostics.
