#include "DeviceSettings.h"

void DeviceSettings::load() {
  // Open read/write once so a fresh board gets the namespace without an
  // alarming NVS NOT_FOUND message. This does not write any keys by itself.
  // Keep the legacy NVS namespace so an update does not erase saved settings.
  Preferences p; p.begin("audiopuck", false);
  wifiSsid = p.getString("ssid", "");
  wifiPassword = p.getString("pass", "");
  pairingToken = p.getString("token", "change-me");
  pcAddress = p.getString("host", "");
  linkMode = static_cast<LinkMode>(min(p.getUChar("link", 0), static_cast<uint8_t>(3)));
  lightMode = static_cast<LightMode>(min(p.getUChar("light", 0), static_cast<uint8_t>(3)));
  oledBrightness = p.getUChar("oled", 160);
  ledBrightness = p.getUChar("led", 48);
  solidRed = p.getUChar("solid_r", 0);
  solidGreen = p.getUChar("solid_g", 170);
  solidBlue = p.getUChar("solid_b", 255);
  rainbowSpeed = min(p.getUChar("rain_spd", 1), static_cast<uint8_t>(2));
  rainbowSpread = constrain(p.getUChar("rain_w", 64), 16, 96);
  volumeStep = p.getUChar("step", 4);
  if(volumeStep!=1&&volumeStep!=2&&volumeStep!=4&&volumeStep!=5&&volumeStep!=10)volumeStep=4;
  trackPopupSeconds=p.getUChar("tracksec",5);
  if(trackPopupSeconds!=0&&trackPopupSeconds!=3&&trackPopupSeconds!=5&&trackPopupSeconds!=10)trackPopupSeconds=5;
  screenTimeoutSeconds = p.getUShort("timeout", 30);
  mediaTimeoutSeconds = p.getUShort("mediatime", 30);
  oledInverted = p.getBool("invert", false);
  encoderReversed = p.getBool("reverse", false);
  acceleration = p.getBool("accel", true);
  targetConfirm = p.getBool("targetok", true);
  automaticProfiles = p.getBool("autoprof", true);
  rainbowReverse = p.getBool("rain_rev", false);
  p.end();
}

void DeviceSettings::save() const {
  Preferences p; p.begin("audiopuck", false);
  p.putString("ssid", wifiSsid); p.putString("pass", wifiPassword);
  p.putString("token", pairingToken); p.putString("host", pcAddress);
  p.putUChar("link", static_cast<uint8_t>(linkMode)); p.putUChar("light", static_cast<uint8_t>(lightMode));
  p.putUChar("oled", oledBrightness); p.putUChar("led", ledBrightness);
  p.putUChar("solid_r", solidRed);p.putUChar("solid_g", solidGreen);p.putUChar("solid_b", solidBlue);
  p.putUChar("rain_spd", rainbowSpeed);p.putUChar("rain_w", rainbowSpread);
  p.putUChar("step", volumeStep);
  p.putUChar("tracksec", trackPopupSeconds);
  p.putUShort("timeout", screenTimeoutSeconds);p.putUShort("mediatime",mediaTimeoutSeconds);p.putBool("invert", oledInverted);
  p.putBool("reverse", encoderReversed); p.putBool("accel", acceleration);p.putBool("targetok",targetConfirm);p.putBool("autoprof",automaticProfiles);p.putBool("rain_rev",rainbowReverse); p.end();
}

void DeviceSettings::factoryReset() { Preferences p; p.begin("audiopuck", false); p.clear(); p.end(); }
