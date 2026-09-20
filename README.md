# MTS2000 Radio Programmer - Under development

A WPF (.NET) codeplug editor and programming utility for the Motorola MTS2000
("Jedi" series) portable radio.

## What it does

- Edit a codeplug (zones, channels, frequencies, tones, bandwidth, scan settings) in a
  friendly WPF UI.
- Save/load codeplugs as JSON files.
- Connect to a radio over a serial (RIB/USB-serial) cable, enter programming mode, and
  read back the firmware version to prove the link is alive.
- Low-level EEPROM memory read/write primitives for the real SB9600/SBEP transport.

## What it doesn't do (yet)

Decoding the radio's ~33KB EEPROM into the structured channel/zone model above requires
porting the MTS2000's full codeplug block map (block hierarchy, vector references,
per-block checksums, and the "Toolproof" auth-code algorithm). That has not been ported
into this project, so `ReadCodeplugAsync`/`WriteCodeplugAsync` currently throw
`NotSupportedException`. The transport layer they'd sit on (`RadioProgrammingSession`) is
real and working — see [Protocol reference](#protocol-reference) below.

## Project layout

| Project | Purpose |
|---|---|
| `MTS2000.Core` | Models (`Codeplug`, `Zone`, `Channel`, `RadioSettings`), codeplug JSON save/load, and the radio communication layer (`Services/`, `Protocol/`). |
| `MTS2000.App` | WPF UI (MVVM via CommunityToolkit.Mvvm): zone/channel editor, radio settings tab, connection toolbar. |

### Protocol reference

The serial transport (`MTS2000.Core/Protocol`) implements the radio's control-bus
(mode switching, firmware version query) and extended memory-access protocol (EEPROM
read/write) framing. The protocol facts it's based on — frame layout, opcodes, and the
checksum table — were learned from the community reverse-engineering write-up in
[JediComlink](https://github.com/lf73/JediComlink); the code here is an independent,
clean-room implementation of those facts (JediComlink ships with no license, so its
source itself was not copied). It provides:

- `RadioProgrammingSession.EnterProgrammingMode()` / `QueryFirmwareVersion()` / `ResetRadio()`
- `RadioProgrammingSession.ReadEeprom(address, count)` / `WriteEeprom(address, data)`

## Requirements

- Windows
- .NET 10 SDK
- A RIB (Radio Interface Box) or RIB-less cable and USB-to-serial adapter to talk to real
  hardware (not required for editing/saving codeplug JSON files).

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE).

## Build and run

```powershell
dotnet build MTS2000.slnx
dotnet run --project MTS2000.App
```

## Disclaimer

This project is not affiliated with or endorsed by Motorola Solutions. Use only on radios
you own and only on frequencies you are authorized to operate on. The MTS2000 is a Part 90
commercial radio; transmitting on it requires an appropriate FCC license (or, for amateur
use, programming the radio for a band you are licensed in).
