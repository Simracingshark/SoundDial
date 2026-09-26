#pragma once

#include <Arduino.h>

namespace Hardware {

// ESP32-C3 SuperMini defaults. GPIO18/19 are reserved for native USB.
// GPIO8/9 are avoided because they are commonly used for boot/onboard LED.
constexpr uint8_t PIN_ENCODER_CLK = 3;
constexpr uint8_t PIN_ENCODER_DT = 4;
constexpr uint8_t PIN_ENCODER_SW = 7;
constexpr uint8_t PIN_OLED_SDA = 5;
constexpr uint8_t PIN_OLED_SCL = 6;
constexpr uint8_t PIN_WS2812 = 10;

constexpr uint8_t OLED_ADDRESS = 0x3C;
constexpr uint16_t OLED_WIDTH = 128;
constexpr uint16_t OLED_HEIGHT = 64;

constexpr uint8_t LED_COUNT = 4;
constexpr uint32_t SERIAL_BAUD = 115200;
constexpr uint16_t DISCOVERY_PORT = 45830;
constexpr uint16_t TCP_PORT = 45831;

constexpr uint8_t MAX_PROFILES = 4;
constexpr uint8_t MAX_TARGETS = 8;
constexpr uint8_t MAX_NAME_LENGTH = 24;

}  // namespace Hardware
