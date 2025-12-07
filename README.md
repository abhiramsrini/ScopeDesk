# ScopeDesk

WPF/.NET 7 desktop app to control a LeCroy oscilloscope via ActiveDSO with a branded UI and size-based rolling logs.

## Stack
- .NET 7 WPF, C#
- MVVM via `CommunityToolkit.Mvvm`
- DI/hosting via `Microsoft.Extensions.Hosting`
- Logging via Serilog (rolling file, size-based retention)
- COM interop to `LeCroy.ActiveDSO`

## Features
- Connect/Disconnect to scope over LAN (default IP configurable)
- Channel selection via checkboxes (C1–C4), all selected by default
- Measurement selection via checkboxes (Amplitude, Mean, Rise Time, Fall Time, Peak-to-Peak, Frequency, Width, Period), all selected by default
- Fetch measurements and render tabular results with timestamps
- Send ad-hoc SCPI commands and view responses
- Open logs folder shortcut

## Branding
- Header is light with the Primeasure logo; primary `#241F61`, secondary `#2F308B`, tertiary `#ED1C24`
- Theme defined in `ScopeDesk/Resources/Theme.xaml`; logo at `Resources/images/logo.png`

## Configuration
`ScopeDesk/appsettings.json`:
- `Connection:DefaultIp` (default `192.168.0.100`)
- Logging path/size/retention
- Theme colors (override if needed)

## Building
1) Ensure .NET SDK 7.x and ActiveDSO are installed/registered on Windows.
2) Restore/build:
```
dotnet restore
dotnet build ScopeDesk/ScopeDesk.csproj
```

## Running
```
dotnet run --project ScopeDesk/ScopeDesk.csproj
```
Provide a valid scope IP, connect, select channels/measurements, then fetch.

## Notes
- Measurement calls use ActiveDSO VBS commands (P1–P8 slots) when COM is available; stub values are used if ActiveDSO isn’t present.
- Logs: `%LocalAppData%/ScopeDesk/logs` by default; 5 MB per file, keep last 10.
