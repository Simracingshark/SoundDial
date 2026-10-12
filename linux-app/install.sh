#!/usr/bin/env bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
INSTALL_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/sounddial"
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/sounddial"
AUTOSTART_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/autostart"

mkdir -p "$INSTALL_DIR" "$CONFIG_DIR" "$AUTOSTART_DIR"
install -m 755 "$SOURCE_DIR/sounddial" "$INSTALL_DIR/sounddial"
if [[ ! -f "$CONFIG_DIR/config.toml" ]]; then
  install -m 644 "$SOURCE_DIR/config.toml" "$CONFIG_DIR/config.toml"
fi

cat > "$AUTOSTART_DIR/sounddial.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=SoundDial
Comment=SoundDial desktop audio controller
Exec=$INSTALL_DIR/sounddial --config $CONFIG_DIR/config.toml
Terminal=false
X-GNOME-Autostart-enabled=true
EOF

echo "Installed SoundDial to $INSTALL_DIR"
echo "Config: $CONFIG_DIR/config.toml"
echo "Log out and back in, or run: $INSTALL_DIR/sounddial --config $CONFIG_DIR/config.toml"
