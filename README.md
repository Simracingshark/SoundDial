# SoundDial

SoundDial is a desktop volume controller built around an ESP32-C3 SuperMini, a rotary encoder, a 1.3-inch OLED and four WS2812 LEDs. It controls individual applications and audio endpoints through USB or Wi-Fi, and includes a separate Media mode for playback, seeking and linked volume targets. The same firmware works with the Windows and Linux companion applications.

This repository contains the complete ESP32 firmware source. Ready-to-use Windows and ESP32 binaries plus the multilingual PDF manuals are provided on the [Releases](https://github.com/Simracingshark/SoundDial/releases) page. The printable model and build presentation are available on [MakerWorld](https://makerworld.com/en/models/3362242-sounddial-esp32-smart-pc-volume-knob).

## Main features

- Main mixer with per-application and per-endpoint volume control
- Separate Media mode with Play/Pause, Mute, Next and Previous controls
- Linked Media targets with independent relative levels
- SteelSeries Sonar and Voicemeeter support
- Automatic game profiles
- USB serial and Wi-Fi connection modes
- Browser-based Wi-Fi setup and OTA firmware update
- OLED menus, connection state, selected target, volume and mute state
- Display sleep and wake by rotating the encoder
- WS2812 volume, solid-color and configurable rainbow modes
- Settings available on the dial and in the Windows application
- Persistent device settings and remembered selected target
- Optional Media timeline seeking with configurable hold time, step and timeout
- Optional battery voltage/percentage display that also works without the PC app
- Optional Windows laptop battery, clock, idle dashboard and low-battery alerts
- Optional laptop-screen brightness target, hidden automatically on desktop PCs
- Up to 16 mixer targets per profile
- Seven grouped on-device settings sections instead of one long flat list
- Windows companion plus an experimental Linux companion for PipeWire/PulseAudio and MPRIS

## Quick start

1. Download the Windows ZIP and the ESP32 firmware from the latest GitHub
   Release. Extract the Windows ZIP to a permanent folder.
2. For a first flash use the `merged.bin` image at address `0x0`. For later
   updates use the application-only `OTA.bin` through PlatformIO or the
   SoundDial browser update page.
3. Start `SoundDial.exe`. The included `config.toml` is ready to edit; keep it
   beside the executable. The unsigned executable may trigger an
   unknown-publisher warning.
4. Connect SoundDial over USB. With `port = "auto"` the companion searches for
   it automatically. Wi-Fi can be configured later through the captive portal.
5. Rotate the knob on the Main screen, hold and rotate to choose a target, then
   release. Double-click to open Media. Long-hold to open Settings.

The USB-only build needs only the ESP32, OLED, encoder and LEDs. The portable
build uses the same firmware and additionally needs a protected 1S cell,
LX-LCBST charger/boost module, latching power switch and, for battery metering,
the two-resistor GPIO0 divider described below.

## Controls

### Main mixer

- Rotate: change volume
- Click: mute or unmute
- Hold and rotate: select a target
- Long hold: open device settings

Targets come from the active profile in `config.toml`. Hold the knob and rotate
until the required target is shown, then release (or click when confirmation is
enabled). `current_app` follows the foreground application, `master` controls
the Windows master output and `brightness` appears only on supported laptops.

### Media mode

- Rotate: change the selected Media target or linked group
- Single click: Play/Pause
- Double click: next track
- Triple click: previous track
- Four clicks: mute or unmute
- Hold and rotate: select Media targets and build a linked group
- Medium hold and release: enter or leave Seek mode
- Rotate in Seek: move by the configured 5, 10, 15 or 30 second step
- Hold for 3 seconds: return to the main mixer

While choosing a Media target, double-click individual entries to mark two or
more targets and click to activate the linked group. A normal click without a
linked group selects one target. Seek is available only for processes listed in
`[media].seek_apps`; medium-hold and release enters or leaves Seek.

## On-device settings

The 32 settings are grouped into `SOUND`, `MEDIA`, `DISPLAY & LED`, `CONTROLS`,
`BATTERY`, `HOST` and `CONNECT / SYSTEM`. Rotate to choose a section and click
to open it. A medium hold returns one level; a long hold exits to the main
screen. Settings close automatically after 30 seconds without input.

Important items:

- `DIAL BATTERY`: `OFF`, `ICON` or `PERCENT` for the SoundDial cell.
- `EMPTY VOLTAGE` / `FULL VOLTAGE`: percentage calibration points.
- `LAPTOP BATTERY`: `OFF`, `ICON` or `PERCENT` for the Windows laptop.
- `IDLE DASH`, `CLOCK` and `IDLE AFTER`: optional idle status page.
- `SEEK HOLD`, `SEEK STEP` and `SEEK TIMEOUT`: Media seek behavior.
- `CONNECTION`: automatic, USB, Wi-Fi or offline operation.
- `DEVICE INFO`: rotate through Network, Battery and Device pages.

## Hardware

- ESP32-C3 SuperMini
- 1.3-inch 128×64 I2C OLED, address `0x3C` (SH1106 used by the published build)
- HW-040 / KY-040 rotary encoder module with push switch
- 4× WS2812 addressable RGB LEDs
- LX-LCBST 1S Li-ion/LiPo charger and adjustable boost-converter module, set to 5.0 V output
- Small 1S 3.7 V Li-ion/LiPo battery sized to fit the enclosure
- 7×7 mm latching pushbutton switch used as the main power switch in the boosted 5 V line
- USB data cable
- Common 5 V and GND distribution
- M3 fasteners
- Opaque filament for the body and knob
- Transparent, natural or milky filament for the diffuser

### Pinout

| Function | ESP32-C3 pin |
|---|---:|
| Encoder CLK | GPIO3 |
| Encoder DT | GPIO4 |
| Encoder switch | GPIO7 |
| OLED SDA | GPIO5 |
| OLED SCL | GPIO6 |
| OLED power | 3V3 |
| WS2812 DIN | GPIO10 |
| WS2812 power | 5 V |
| Battery divider midpoint (experimental branch) | GPIO0 |

All modules must share GND. ESP32 signal pins use 3.3 V logic; never feed 5 V into a GPIO.

### Battery power path

Connect the 1S battery to the LX-LCBST battery pads, adjust the module output to exactly 5.0 V **before** connecting the electronics, and route the boosted 5 V output through the 7×7 mm latching switch to the device's 5 V rail. The LX-LCBST USB-C connector is used to charge the battery. Observe battery polarity and verify the labels on your particular module because inexpensive board revisions and clones may differ.

The photographed prototype connects GPIO10 directly to the first LED. A 220–470 Ω data resistor and a 470–1000 µF capacitor across the LED supply are optional reliability improvements, but they are not installed in the shown build.

### Battery meter and Media seek

For battery measurement connect two equal 220 kΩ resistors in series from the raw 1S battery positive terminal to battery negative/GND, and connect GPIO0 to their midpoint. Do not connect the boosted 5 V output to GPIO0. The approximately 440 kΩ divider draws about 9.5 µA at 4.2 V. The reading is filtered in software, so a capacitor is not required for testing.

In Media mode, hold the encoder for roughly one second and release to enter Seek. Rotate to seek by the configured step. Repeat the medium hold, or wait for the configurable Seek timeout, to return to the normal Media screen. The step can be 5, 10, 15 or 30 seconds; the exit timeout can be 3, 5, 10 seconds or disabled. The existing three-second hold still returns to the main mixer. Set `seek_apps = ["firefox", "chrome", "msedge"]` in the `[media]` section of `config.toml` to choose which applications may receive seek commands. Process names are written without `.exe`; applications omitted from this list cannot steal seeking while they run in the background.

Device Info is split into three readable pages: Network, Battery and Device. Rotate the encoder to change pages and click to return to Settings.

The divider is required even though a 1S cell reaches only about 4.2 V: ESP32-C3
GPIO inputs are 3.3 V devices. Two equal 220 kΩ resistors reduce 4.2 V to about
2.1 V at GPIO0 while drawing roughly 9.5 µA. The meter is an indicator only; it
does not replace battery protection.

For a wired-only build, omit the battery, LX-LCBST module, latching power switch
and voltage divider. Power SoundDial from USB and set `BATTERY VIEW` to `OFF`.

Battery percentage uses the configurable 0% and 100% thresholds from the `[battery]` section of `config.toml`. The defaults are `min_voltage = 3.30` and `max_voltage = 4.20`. The PC application transfers valid threshold changes to the controller automatically and the ESP32 stores them in non-volatile memory. `display = "percent"`, `"icon"` or `"off"` selects the compact battery indicator shown on Main, Media and Seek; the Battery page in Device Info always shows the exact percentage and measured voltages. The battery is measured by the ESP32, so the indicator remains available without a connected PC.

Players such as Spotify sometimes allow seeking without publishing the track duration. Seek then remains usable and shows the current position followed by `--:--`. Closing the selected browser media session clears the stale source from the Seek screen automatically.

## Repository layout

- `firmware/` — PlatformIO project for the ESP32-C3
- `README.md` — project overview, wiring, setup and controls
- `LICENSE-CODE.md` — firmware and application code license
- `LICENSE-HARDWARE.md` — hardware, documentation and media license

Release packages, manuals and ready-to-flash images are uploaded to GitHub Releases instead of being duplicated in the source branch. Printable files are distributed through MakerWorld.

## Firmware build and upload

1. Install Visual Studio Code, PlatformIO and the Espressif32 platform.
2. Open the `firmware` directory as a PlatformIO project.
3. Select the `esp32-c3-supermini` environment.
4. Change or remove the `COM12` upload/monitor port in `platformio.ini` if your board uses another port.
5. Build and upload the firmware.

Use `hardware-test` for the display, encoder and LED hardware test build.

The published build defaults to SH1106, matching the photographed 1.3-inch module. A separate `esp32-c3-supermini-ssd1306` environment is provided for SSD1306 modules.

## Windows app

The companion application targets Windows and is distributed as a self-contained .NET 10 build.

For normal use, download the self-contained application archive from the Releases page, extract it to a permanent folder and run `SoundDial.exe`. Copy `config.example.toml` to `config.toml` before making personal changes.

Application names in the configuration are written without `.exe`, for example `spotify`, `firefox` or `chrome`. Colors may be names or HEX values such as `#1DB954`.

The application reloads `config.toml` after it is saved. The main sections are:

- `[mixer]`: default Main-screen targets and colors.
- `[steps]`: optional per-target volume step overrides.
- `[media]`: Media volume targets and the seek allowlist.
- `[battery]`: SoundDial cell calibration and display style.
- `[host_status]`: laptop battery, clock, idle page and warnings.
- `[voicemeeter]`: optional Windows-only Voicemeeter targets.
- `[profile.*]`: optional automatic profiles, including game profiles.

Removing every process from a gaming profile's `activate_when` list prevents
that profile from activating automatically. Up to 16 targets may be used in a
profile.

The `[host_status]` section controls the optional laptop battery (`off`, `icon`
or `percent`), clock, idle dashboard and warnings. Use
`test_battery_percent = -2` for the real Windows battery, `-1` to force desktop
mode, or `0`–`100` only for display testing. Add `brightness = "yellow"` to a
profile to expose laptop-screen brightness; the target is hidden when Windows
does not report a controllable built-in display.

## Linux app

The experimental Linux companion is in `linux-app/`. It uses the same ESP32
firmware and supports USB/Wi-Fi transport, PipeWire/PulseAudio volume through
`wpctl`/`pactl`, and MPRIS media control through `playerctl`. SteelSeries Sonar
and Voicemeeter remain Windows-only. See `linux-app/README.md` for prerequisites,
installation and the current Wayland/X11 limitations.

### Windows download and security notice

`SoundDial.exe` is a self-contained .NET application and is currently **not digitally signed with a trusted code-signing certificate**. Windows SmartScreen or some antivirus services may therefore show an “unknown publisher” warning or a false-positive detection. The official binaries are distributed only through this repository's Releases page; do not download them from third-party mirrors.

Every release package includes a `SHA256SUMS.txt` file generated from the final
artifacts. Use that file instead of checksums copied from an older release.

To verify a downloaded file in PowerShell, run `Get-FileHash .\filename -Algorithm SHA256` and compare the result with the matching value above. A checksum confirms that the file matches the published release; it is not a substitute for a trusted digital signature.

## Wi-Fi setup

If no saved network is available, SoundDial starts its setup access point. Connect to it, open the captive portal and choose the Wi-Fi network. The pairing token must match the token in the Windows app configuration. The default example value is `change-me`; change it for a real installation.

USB can remain connected while Wi-Fi is enabled. The Windows app can use automatic connection selection.

## Documentation

- User Manual: Ukrainian + English
- User Manual: Ukrainian + English + Russian
- Technical Overview: Ukrainian + English
- Technical Overview: Ukrainian + English + Russian

All PDF editions are attached to the GitHub Release together with the ready-to-use software and firmware. They include first setup, configuration, autostart, controls, troubleshooting, wiring and printing notes.

## Credits

Concept, enclosure, CAD, printing, electronics integration, assembly and hardware testing by **Simracingshark**. Firmware and the Windows companion app were created with AI assistance and then tested and tuned on the finished device.

## License

- Firmware and Windows application source: GNU GPL v3 or later
- CAD models, documentation and media: CC BY-NC-SA 4.0

See `LICENSE-CODE.md` and `LICENSE-HARDWARE.md`.
