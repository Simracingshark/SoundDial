#pragma once

#include <Arduino.h>
#include <Preferences.h>

enum class LinkMode : uint8_t { Auto, Usb, Wifi, Offline };
enum class LightMode : uint8_t { Volume, Solid, Off, Rainbow };

struct DeviceSettings {
  String wifiSsid;
  String wifiPassword;
  String pairingToken = "change-me";
  String pcAddress;
  LinkMode linkMode = LinkMode::Auto;
  LightMode lightMode = LightMode::Volume;
  uint8_t oledBrightness = 160;
  uint8_t ledBrightness = 48;
  uint8_t solidRed = 0;
  uint8_t solidGreen = 170;
  uint8_t solidBlue = 255;
  uint8_t rainbowSpeed = 1;
  uint8_t rainbowSpread = 64;
  uint8_t volumeStep = 4;
  uint8_t trackPopupSeconds = 5;
  uint16_t screenTimeoutSeconds = 30;
  uint16_t mediaTimeoutSeconds = 30;
  bool oledInverted = false;
  bool encoderReversed = false;
  bool acceleration = true;
  bool targetConfirm = true;
  bool automaticProfiles = true;
  bool rainbowReverse = false;

  void load();
  void save() const;
  void factoryReset();
};
