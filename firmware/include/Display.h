#pragma once

#include <Arduino.h>
#include <Wire.h>
#include "HardwareConfig.h"

class PuckDisplay {
 public:
  bool begin();
  void clear();
  void show();
  void setContrast(uint8_t value);
  void sleep(bool enabled);
  void invert(bool enabled);
  void pixel(int16_t x, int16_t y, bool on = true);
  void line(int16_t x0, int16_t y0, int16_t x1, int16_t y1);
  void rect(int16_t x, int16_t y, int16_t w, int16_t h, bool fill = false);
  void text(int16_t x, int16_t y, const String& value, uint8_t scale = 1);
  int16_t textWidth(const String& value, uint8_t scale = 1) const;
  void centered(int16_t y, const String& value, uint8_t scale = 1);
  void textMedium(int16_t x, int16_t y, const String& value);
  int16_t textWidthMedium(const String& value) const;
  void centeredMedium(int16_t y, const String& value);
  void progress(int16_t x, int16_t y, int16_t w, int16_t h, uint8_t percent);

 private:
  uint8_t buffer_[Hardware::OLED_WIDTH * Hardware::OLED_HEIGHT / 8]{};
  uint8_t sentBuffer_[Hardware::OLED_WIDTH * Hardware::OLED_HEIGHT / 8]{};
  bool hasSentFrame_ = false;
  void command(uint8_t value);
  void data(const uint8_t* bytes, size_t length);
  void character(int16_t x, int16_t y, char value, uint8_t scale);
  void characterMedium(int16_t x, int16_t y, char value);
};
