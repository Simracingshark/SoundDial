# SoundDial

SoundDial is a desktop volume controller built around an ESP32-C3 SuperMini, a rotary encoder, a 1.3-inch OLED and four WS2812 LEDs. It controls individual Windows applications and audio endpoints through USB or Wi-Fi, and includes a separate Media mode for playback and linked volume targets.

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
- Hold for 3 seconds: return to the main mixer

## Hardware

- ESP32-C3 SuperMini
- 1.3-inch 128×64 I2C OLED, address `0x3C` (SSD1306 tested)
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

All modules must share GND. ESP32 signal pins use 3.3 V logic; never feed 5 V into a GPIO.

### Battery power path

Connect the 1S battery to the LX-LCBST battery pads, adjust the module output to exactly 5.0 V **before** connecting the electronics, and route the boosted 5 V output through the 7×7 mm latching switch to the device's 5 V rail. The LX-LCBST USB-C connector is used to charge the battery. Observe battery polarity and verify the labels on your particular module because inexpensive board revisions and clones may differ.

The photographed prototype connects GPIO10 directly to the first LED. A 220–470 Ω data resistor and a 470–1000 µF capacitor across the LED supply are optional reliability improvements, but they are not installed in the shown build.

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

The default display controller is SSD1306. For a compatible SH1106 module, replace the SSD1306 build flag with `OLED_CONTROLLER_SH1106=1`.

## Windows app

The companion application targets Windows and is distributed as a self-contained .NET 10 build.

For normal use, download the self-contained application archive from the Releases page, extract it to a permanent folder and run `SoundDial.exe`. Copy `config.example.toml` to `config.toml` before making personal changes.

Application names in the configuration are written without `.exe`, for example `spotify`, `firefox` or `chrome`. Colors may be names or HEX values such as `#1DB954`.

### Windows download and security notice

`SoundDial.exe` is a self-contained .NET application and is currently **not digitally signed with a trusted code-signing certificate**. Windows SmartScreen or some antivirus services may therefore show an “unknown publisher” warning or a false-positive detection. The official binaries are distributed only through this repository's Releases page; do not download them from third-party mirrors.

SHA-256 checksums for the v1.0.0 release files:

```text
5FBA721ABC0FC930F522622AB8BCD34BC917F43CCBA6B4661D7BF4E3F5B57367  SoundDial_Windows_App.zip
DF3F84C4D72C264674A6FDD67A12A32902ED67A08541A2ED2A26E6CEA02B7095  SoundDial-ESP32C3-merged.bin
19673D32327A43BD9D68C0FCD3BD46B2D33E42F1BC49E7053AF95F82707BE695  SoundDial-ESP32C3-OTA.bin
```

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
