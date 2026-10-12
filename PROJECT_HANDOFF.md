# SoundDial project handoff

This file lets a fresh Codex session continue the project without relying on the
original Windows chat history. It describes the current source tree, decisions
already made and the behaviors that must not regress.

## Product summary

SoundDial is a DIY desktop audio controller based on an ESP32-C3 SuperMini. The
physical device has a rotary encoder with push switch, a 1.3-inch 128x64 I2C
OLED and four WS2812 LEDs. It can run as a wired USB device or as a portable
battery-powered device and communicates with the companion application through
USB serial or Wi-Fi.

One ESP32 firmware image is intended to work with both the Windows and Linux
companions. Moving the dial between operating systems must not require flashing
different firmware.

## Current source tree

- `firmware/` - PlatformIO ESP32-C3 firmware.
- `windows-app/` - .NET 10 Windows companion (`SoundDial.exe`).
- `linux-app/` - .NET 10 Linux x64 companion (`sounddial`).
- `hardware/` - STEP files for the enclosure.
- `docs/` - current multilingual PDF manuals.
- `media/` - selected public project artwork.
- `release-assets/` - files used while assembling releases.

At the time this handoff was written, the Git branch is
`feature/battery-meter-seek` and the working tree contains intentional
uncommitted Windows/Linux/README changes. Always inspect `git status` before
working. Do not reset or discard them.

## Hardware contract

| Function | ESP32-C3 pin |
|---|---:|
| Encoder CLK | GPIO3 |
| Encoder DT | GPIO4 |
| Encoder switch | GPIO7 |
| OLED SDA | GPIO5 |
| OLED SCL | GPIO6 |
| WS2812 DIN | GPIO10 |
| Optional battery-divider midpoint | GPIO0 |

The OLED uses 3.3 V, the LEDs use 5 V and every module shares GND. The physical
1.3-inch display is SH1106 at address `0x3C`; that is the default build. A
separate SSD1306 environment is documented in `firmware/platformio.ini`.

For the optional meter, two equal 220 kOhm resistors form a divider:

`battery + -> 220k -> GPIO0 -> 220k -> GND`

The divider is still required for a 1S cell because a fully charged cell is
about 4.2 V while an ESP32-C3 GPIO is a 3.3 V input. The prototype has no LED
data resistor or bulk capacitor; the README describes those only as optional
reliability improvements.

Portable builds additionally use a protected 1S cell, an LX-LCBST charger/boost
module adjusted to 5.0 V and a 7x7 mm latching power switch. Wired builds omit
all of those parts and the divider.

## Controls and on-device UI

Main mixer:

- Rotate: change volume.
- Click: mute/unmute.
- Hold and rotate: select target.
- Long hold: settings.

Media mode:

- Rotate: change the selected media target or linked group.
- One/two/three/four clicks: Play/Pause, next, previous and mute.
- Hold and rotate: select targets and build a linked group.
- Medium hold and release: enter or leave Seek.
- Rotate in Seek: move along the timeline by the configured step.
- Three-second hold: return to the main mixer.

The 32 on-device settings are divided into `SOUND`, `MEDIA`, `DISPLAY & LED`,
`CONTROLS`, `BATTERY`, `HOST` and `CONNECT / SYSTEM`. Device Info belongs under
Connect / System and has readable Network, Battery and Device pages. Do not
flatten the settings back into one long list.

Battery display modes are percent, icon and off. Measurement and rendering work
without the PC application. Minimum and maximum calibration voltages are stored
on the device. They are percentage calibration points, not battery protection
cutoffs.

## Desktop behavior that matters

- Rapid encoder movement must feel immediate. Do not create a long queue of
  incremental volume writes. Coalesce updates and always apply the final value.
- Configuration hot reload must tolerate editors briefly writing invalid or
  incomplete TOML. Keep using the previous valid configuration until a complete
  save can be loaded.
- Selected targets and relevant settings persist across desktop restarts.
- Seek is allowlisted with `[media].seek_apps`. Background Spotify must not steal
  browser seeking unless the user explicitly includes and prioritizes it.
- Firefox, Chromium/Chrome and Edge browser media sessions have regressed before.
  Check stale-session cleanup, closed-tab behavior and timeline refresh whenever
  seek code changes.
- Spotify may accept seek commands without exposing duration. In that case seek
  may work while the display shows an unknown duration.
- Seek time must refresh without requiring the encoder to move.
- Game profiles can use `foreground` or `running`. Removing all games from
  `activate_when` effectively prevents the gaming profile from activating.
- Voicemeeter aliases are Windows-only. The user specifically needs AUX-style
  routing to be represented correctly when Windows target mapping is changed.

## Linux companion

`linux-app/` is intended as the Linux counterpart of the stable Windows client.
It keeps the same configuration concepts and shares the platform-neutral models,
config loader, protocol, network transport, target controller and logging source
from `windows-app/` through linked compile items in the project file.

Implemented scope:

- USB serial and Wi-Fi transport.
- PipeWire/PulseAudio volume and mute through `wpctl`/`pactl`.
- MPRIS metadata and Play/Pause/Next/Previous/seek through `playerctl`.
- Linked media volume targets.
- Automatic profiles based on processes.
- Hot reload and persistent selected target.

Known platform boundary:

- Voicemeeter and SteelSeries Sonar do not exist on Linux.
- Foreground-window detection works on X11 and Hyprland. Generic Wayland does
  not reliably expose the focused process, so `current_app` falls back to the
  active audio stream and `trigger = "running"` is the portable profile mode.
- The Linux client has compiled and passed its self-tests, but still needs a real
  Linux desktop test with the physical dial, PipeWire and actual media players.

Linux prerequisites and installation commands are in `linux-app/README.md`.
The prepared portable package is generated outside this repository as
`SoundDial_Linux_v1.zip`; do not assume that archive is newer than the source.

## Build and test commands

From `windows-app/` with .NET 10 installed:

```text
dotnet build
dotnet run -- --self-test
dotnet run -- --diagnostics
```

From `linux-app/` on Linux with .NET 10 installed:

```text
dotnet build
dotnet run -- --self-test
dotnet run -- --diagnostics
```

The published Linux executable is self-contained x64. Run-time integration also
needs `wpctl`, `pactl`, `playerctl` and serial-port permission, commonly through
the `dialout` group.

From `firmware/` with PlatformIO installed:

```text
pio run -e esp32-c3-supermini
pio run -e hardware-test
```

The committed `platformio.ini` currently names `COM12` for the owner's Windows
machine. Change or override that port on another computer; it is not a universal
device identifier.

## Documentation and publication decisions

- Public documentation is in English plus selected Ukrainian/Russian editions.
- The User Manual should teach operation and first use. The Technical Overview
  should focus on parts, wiring, construction and technical behavior; do not make
  both PDFs near-duplicates.
- Keep photographs at their natural aspect ratio and avoid side bars that clash
  with the page background. Do not fabricate OLED song titles or distort the
  device in generated hero images.
- Describe two builds clearly: USB-only base build, and the additional parts for
  the optional portable build.
- MakerWorld should contain print files, concise build information, selected
  manuals/photos and a link to GitHub. GitHub Releases should contain desktop
  packages, firmware artifacts, manuals and checksums.
- Nothing should be uploaded or published until the owner explicitly says to do
  it after testing.

## Recommended first task on Linux

1. Read `AGENTS.md`, this file, the root README and `linux-app/README.md`.
2. Run `git status` and preserve the existing working tree.
3. Install the Linux prerequisites and run `--self-test` and `--diagnostics`.
4. Start the client with an explicit test config if needed.
5. Test USB first, then Wi-Fi, master volume, per-app volume, media buttons,
   Firefox/Chromium seek, Spotify behavior, config reload and process profiles.
6. Record every platform-specific failure before changing shared code.

