# SoundDial

SoundDial is a desktop volume controller built around an ESP32-C3 SuperMini, a rotary encoder, a 1.3-inch OLED and four WS2812 LEDs. It controls individual Windows applications and audio endpoints through USB or Wi-Fi, and includes a separate Media mode for playback, linked volume targets and timeline seeking. It can be assembled as a simple USB-powered controller or as a portable battery-powered version.

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
- Eight on-device settings sections instead of one long flat list

## Controls

### Main mixer

- Rotate: change volume
- Click: mute or unmute
- Hold and rotate: select a target
- Long hold: open device settings

### Media mode

- Rotate: change the selected Media target or linked group
- Single click: Play/Pause
- Double click: mute or unmute
- Triple click: next track
- Four clicks: previous track
- Hold and rotate: select Media targets and build a linked group
- Medium hold and release: enter or leave Seek mode
- Rotate in Seek: move by the configured 5, 10, 15 or 30 second step
- Hold for 3 seconds: return to the main mixer

## On-device settings

The 29 settings are grouped into `GENERAL`, `DISPLAY`, `LIGHTS`, `CONTROLS`,
`MEDIA/SEEK`, `BATTERY`, `NETWORK` and `SYSTEM`. Rotate to choose a section and
click to open it. A long hold returns from an item list to the section list; a
second long hold returns to the main mixer. `EXIT SETTINGS` provides an explicit
save-and-exit item.

## Choose a build

### USB-only build

This is the simplest version and is recommended for a first build. USB provides both power and communication. Use the ESP32-C3, OLED, encoder, four WS2812 LEDs, wiring, fasteners and printed parts listed below. Do **not** install the battery, LX-LCBST, latching power switch or GPIO0 voltage divider. Set `BATTERY VIEW` to `OFF` in the device settings.

### Portable battery build

Start with the USB-only parts and add a protected 1S 3.7 V Li-ion/LiPo cell, the LX-LCBST charger/boost module, a 7×7 mm latching power switch and, if battery indication is wanted, two equal 220 kΩ resistors for the GPIO0 divider. The battery and divider are optional features; they are not required for normal wired operation. USB may still be used for data and firmware upload, while Wi-Fi allows the controller to be used without a data cable.

## Hardware

### Core parts — both versions

- ESP32-C3 SuperMini
- 1.3-inch 128×64 I2C OLED, address `0x3C` (SSD1306 tested)
- HW-040 / KY-040 rotary encoder module with push switch
- 4× WS2812 addressable RGB LEDs
- USB data cable
- Common 5 V and GND distribution
- M3 fasteners
- Opaque filament for the body and knob
- Transparent, natural or milky filament for the diffuser

### Portable-version additions

- LX-LCBST 1S Li-ion/LiPo charger and adjustable boost-converter module, adjusted to exactly 5.0 V before connecting the electronics
- Small protected 1S 3.7 V Li-ion/LiPo battery sized to fit the enclosure
- 7×7 mm latching pushbutton switch used as the main power switch in the boosted 5 V line
- Optional battery indicator: 2× 220 kΩ resistors and GPIO0

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
| Battery divider midpoint (portable version, optional) | GPIO0 |

All modules must share GND. ESP32 signal pins use 3.3 V logic; never feed 5 V into a GPIO.

### USB-only power path

Connect the ESP32-C3 to the PC with a USB data cable. Distribute the board's 5 V rail to the WS2812 LEDs and use 3V3 for the OLED. All grounds must be common. No charger, battery, external power switch or battery divider is required.

### Portable battery power path

Connect the 1S battery to the LX-LCBST battery pads, adjust the module output to exactly 5.0 V **before** connecting the electronics, and route the boosted 5 V output through the 7×7 mm latching switch to the device's 5 V rail. The LX-LCBST USB-C connector is used to charge the battery. Observe battery polarity and verify the labels on your particular module because inexpensive board revisions and clones may differ.

### Battery meter and Media seek

For battery measurement connect two equal 220 kΩ resistors in series from the raw 1S battery positive terminal to battery negative/GND, and connect GPIO0 to their midpoint. Do not connect the boosted 5 V output to GPIO0. The approximately 440 kΩ divider draws about 9.5 µA at 4.2 V. The reading is filtered in software, so a capacitor is not required.

In Media mode, hold the encoder for roughly one second and release to enter Seek. Rotate to seek by the configured step. Repeat the medium hold, or wait for the configurable Seek timeout, to return to the normal Media screen. The step can be 5, 10, 15 or 30 seconds; the exit timeout can be 3, 5, 10 seconds or disabled. The existing three-second hold still returns to the main mixer. Set `seek_apps = ["firefox", "chrome", "msedge"]` in the `[media]` section of `config.toml` to choose which applications may receive seek commands. Process names are written without `.exe`; applications omitted from this list cannot steal seeking while they run in the background.

Device Info is split into three readable pages: Network, Battery and Device. Rotate the encoder to change pages and click to return to Settings.

The divider is required even though a 1S cell reaches only about 4.2 V: ESP32-C3
GPIO inputs are 3.3 V devices. Two equal 220 kΩ resistors reduce 4.2 V to about
2.1 V at GPIO0 while drawing roughly 9.5 µA. The meter is an indicator only; it
does not replace battery protection.

For the USB-only build, omit the battery, LX-LCBST module, latching power switch and voltage divider. Power SoundDial from USB and set `BATTERY VIEW` to `OFF`.

Battery percentage uses the configurable 0% and 100% thresholds from the `[battery]` section of `config.toml`. The defaults are `min_voltage = 3.30` and `max_voltage = 4.20`. The PC application transfers valid threshold changes to the controller automatically and the ESP32 stores them in non-volatile memory. `display = "percent"`, `"icon"` or `"off"` selects the compact battery indicator shown on Main, Media and Seek; the Battery page in Device Info always shows the exact percentage and measured voltages. The battery is measured by the ESP32, so the indicator remains available without a connected PC.

Players such as Spotify sometimes allow seeking without publishing the track duration. Seek then remains usable and shows the current position followed by `--:--`. Closing the selected browser media session clears the stale source from the Seek screen automatically.

## Repository and release layout

- `firmware/` — PlatformIO project for the ESP32-C3
- `README.md` — project overview, wiring, setup and controls
- `LICENSE-CODE.md` — firmware and application code license
- `LICENSE-HARDWARE.md` — hardware, documentation and media license

The v2 source archive also contains the Windows companion application source and editable STEP models. Ready-to-use packages, multilingual manuals and firmware images are uploaded to GitHub Releases instead of being duplicated in the source branch. Printable files are distributed through MakerWorld.

## Firmware build and upload

1. Install Visual Studio Code, PlatformIO and the Espressif32 platform.
2. Open the `firmware` directory as a PlatformIO project.
3. Select the `esp32-c3-supermini` environment.
4. Change or remove the `COM12` upload/monitor port in `platformio.ini` if your board uses another port.
5. Build and upload the firmware.

Use `hardware-test` for the display, encoder and LED hardware test build.

The default display controller is SSD1306. For a compatible SH1106 module, replace the SSD1306 build flag with `OLED_CONTROLLER_SH1106=1`.

## Windows app

The companion application targets Windows and is distributed as a self-contained .NET 10 build.

For normal use, download the self-contained application archive from the Releases page, extract it to a permanent folder and run `SoundDial.exe`. Copy `config.example.toml` to `config.toml` before making personal changes.

Application names in the configuration are written without `.exe`, for example `spotify`, `firefox` or `chrome`. Colors may be names or HEX values such as `#1DB954`.

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
