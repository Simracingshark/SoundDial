#pragma once

#include <Arduino.h>
#include "HardwareConfig.h"

class LedRing {
 public:
  void begin();
  void render(uint8_t volume, bool muted, bool connected, uint8_t red, uint8_t green, uint8_t blue, uint8_t brightness, uint8_t mode, uint8_t rainbowSpeed = 1, bool rainbowReverse = false, uint8_t rainbowSpread = 64);
  void renderNetwork(uint8_t status, uint8_t brightness, uint8_t mode);
  void off();
 private:
  void send(const uint8_t pixels[Hardware::LED_COUNT][3]);
};
