# ScopeDesk

.NET 7 WPF client for LeCroy oscilloscopes with ActiveDSO control, branded UI, rolling logs, and SCPI helpers.

## Stack
- .NET 7 WPF, C#
- MVVM via `CommunityToolkit.Mvvm`
- DI/hosting via `Microsoft.Extensions.Hosting`
- Logging via Serilog (size-based rolling file)
- COM interop to `LeCroy.ActiveDSOCtrl.1`/`LeCroy.ActiveDSOCtrl` (stub mode if missing)

## Features
- Connect/Disconnect to the scope over LAN (`TCPIP:<ip>`), default IP from config, stub mode when ActiveDSO COM is unavailable
- Channel selection (C1–C4) and measurement selection (Amplitude, Mean, Rise Time, Fall Time, Peak-to-Peak, Frequency, Width, Period), all pre-selected
- Fetch measurements into a matrix (measurements as rows, channels as columns) with the last timestamp and a Clear action
- Show scope serial number after connecting, status badge + header message for connection state
- Send ad-hoc SCPI commands (response readback with `CHDR OFF`/`?`) and view responses
- Footer link to Primeasure and button to open the logs folder from the matrix card

## Branding
- Light header with Primeasure logo; primary `#241F61`, secondary `#2F308B`, tertiary `#ED1C24`
- Theme dictionary at `ScopeDesk/Resources/Theme.xaml`; logo at `ScopeDesk/Resources/images/logo.png`

## Configuration (`ScopeDesk/appsettings.json`)
- `Connection:DefaultIp` (default `192.168.0.100`); `DefaultPort` stored but unused in the current TCPIP connection string
- `Logging:*`: level plus rolling file path/size/retention (defaults to `%LocalAppData%/ScopeDesk/logs/scope.log`, ~5 MB per file, keep 10, roll on limit)
- `Theme:*`: primary/secondary/tertiary colors

## Building
1) Ensure .NET SDK 7.x and LeCroy ActiveDSO control are installed/registered on Windows.
2) Restore/build:
```
dotnet restore
dotnet build ScopeDesk/ScopeDesk.csproj
```

## Running
```
dotnet run --project ScopeDesk/ScopeDesk.csproj
```
Provide the scope IP, connect (serial number shown on success), select/toggle channels and measurements, then fetch. Use "Clear" to reset the matrix and "Open Logs Folder" for diagnostics.

## Packaging (Windows EXE)
Publish a desktop build:
- Framework-dependent (requires .NET 7 desktop runtime on the target):
  ```
  dotnet publish ScopeDesk/ScopeDesk.csproj -c Release -r win10-x64 --self-contained false
  ```
- Self-contained (runtime bundled):
  ```
  dotnet publish ScopeDesk/ScopeDesk.csproj -c Release -r win10-x64 --self-contained true
  ```
The executable will be at `ScopeDesk/bin/Release/net7.0-windows/win10-x64/publish/ScopeDesk.exe`; copy that folder to the target PC or create a shortcut to the EXE.

## Notes
- Measurement calls map to VBS param engine slots P1–P8; stub values are returned when the COM object isn’t available.
- SCPI helper writes the command, issues `CHDR OFF`, then `?` to read the response.
- Serilog config auto-creates the log directory before writing.
