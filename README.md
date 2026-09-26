# SoundDial

![SoundDial](media/SoundDial_Hero_Dark.png)

SoundDial is a desktop volume controller built around an ESP32-C3 SuperMini, a rotary encoder, a 1.3-inch OLED and four WS2812 LEDs. It controls individual Windows applications and audio endpoints through USB or Wi-Fi, and includes a separate Media mode for playback and linked volume targets.

The repository contains the complete firmware source, Windows companion-app source, editable STEP models and multilingual documentation. Ready-to-use Windows and ESP32 binaries are provided on the **Releases** page.

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

The photographed prototype connects GPIO10 directly to the first LED. A 220–470 Ω data resistor and a 470–1000 µF capacitor across the LED supply are optional reliability improvements, but they are not installed in the shown build.

## Repository layout

- `firmware/` — PlatformIO project for the ESP32-C3
- `windows-app/` — .NET Windows companion application
- `hardware/` — five editable STEP parts and the full assembly
- `docs/` — user manuals and technical overviews
- `media/` — project image used by this README

The local `release-assets/` directory is intentionally excluded from Git. Its files are uploaded to GitHub Releases instead.

## Firmware build and upload

1. Install Visual Studio Code, PlatformIO and the Espressif32 platform.
2. Open the `firmware` directory as a PlatformIO project.
3. Select the `esp32-c3-supermini` environment.
4. Change or remove the `COM12` upload/monitor port in `platformio.ini` if your board uses another port.
5. Build and upload the firmware.

Use `hardware-test` for the display, encoder and LED hardware test build.

The default display controller is SSD1306. For a compatible SH1106 module, replace the SSD1306 build flag with `OLED_CONTROLLER_SH1106=1`.

## Windows app build

The application targets Windows and .NET 10.

```powershell
dotnet restore windows-app/AudioPuck.PC.csproj
dotnet publish windows-app/AudioPuck.PC.csproj -c Release -r win-x64 --self-contained true
```

For normal use, download the self-contained application archive from the Releases page, extract it to a permanent folder and run `SoundDial.exe`. Copy `config.example.toml` to `config.toml` before making personal changes.

Application names in the configuration are written without `.exe`, for example `spotify`, `firefox` or `chrome`. Colors may be names or HEX values such as `#1DB954`.

## Wi-Fi setup

If no saved network is available, SoundDial starts its setup access point. Connect to it, open the captive portal and choose the Wi-Fi network. The pairing token must match the token in the Windows app configuration. The default example value is `change-me`; change it for a real installation.

USB can remain connected while Wi-Fi is enabled. The Windows app can use automatic connection selection.

## Documentation

- User Manual: Ukrainian + English
- User Manual: Ukrainian + English + Russian
- Technical Overview: Ukrainian + English
- Technical Overview: Ukrainian + English + Russian

The Ukrainian + English PDFs are stored in `docs`. The larger three-language editions are attached to the GitHub Release together with the ready-to-use software and firmware. All editions include first setup, configuration, autostart, controls, troubleshooting, wiring and printing notes.

## Credits

Concept, enclosure, CAD, printing, electronics integration, assembly and hardware testing by **Simracingshark**. Firmware and the Windows companion app were created with AI assistance and then tested and tuned on the finished device.

## License

- Firmware and Windows application source: GNU GPL v3 or later
- CAD models, documentation and media: CC BY-NC-SA 4.0

See `LICENSE-CODE.md` and `LICENSE-HARDWARE.md`.
