# SoundDial for Linux

This is the Linux companion for the same ESP32-C3 firmware used by the Windows
build. The controller does not need to be reflashed when moving between Windows
and Linux.

## Supported

- USB serial and Wi-Fi transport
- PipeWire/PulseAudio master and per-application volume/mute
- MPRIS metadata, Play/Pause, Next/Previous and timeline seeking
- linked Media volume targets
- automatic profiles based on running processes
- configuration hot reload and persistent target selection

Voicemeeter and SteelSeries Sonar are Windows-only. Their targets show offline
on Linux; use ordinary application, `master`, `current_app` or `active_audio`
targets instead.

## Requirements

- A modern x86-64 Linux distribution using PipeWire with `pipewire-pulse`
- `wpctl` (WirePlumber), `pactl` and `playerctl`
- membership in the distro's serial-access group, commonly `dialout`

Ubuntu/Debian:

```bash
sudo apt install wireplumber pulseaudio-utils playerctl
sudo usermod -aG dialout "$USER"
```

Log out and back in after changing the serial group.

## Run and install

Test the portable build:

```bash
chmod +x sounddial
./sounddial --diagnostics
./sounddial --self-test
./sounddial
```

For per-user installation and desktop autostart:

```bash
chmod +x install.sh
./install.sh
```

The installed config is `~/.config/sounddial/config.toml`; logs and saved
selection state are in `~/.local/share/sounddial`.

## Wayland note

Automatic profiles with `trigger = "running"` work on all desktops. Foreground
application detection works on X11 and Hyprland. Generic Wayland intentionally
does not expose the focused process, so `current_app` falls back to the active
audio stream there.
