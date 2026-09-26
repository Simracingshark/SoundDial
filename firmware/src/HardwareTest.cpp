#include <Arduino.h>

#if defined(SOUNDDIAL_HARDWARE_TEST)

#include "Display.h"
#include "EncoderInput.h"
#include "HardwareConfig.h"
#include "LedRing.h"

PuckDisplay testDisplay;
EncoderInput testEncoder;
LedRing testLights;

int32_t encoderPosition = 0;
uint8_t colorIndex = 0;
uint8_t testVolume = 25;
bool displayFound = false;
bool buttonWasPressed = false;
uint32_t eventNumber = 0;

struct TestColor {
  const char* name;
  uint8_t red;
  uint8_t green;
  uint8_t blue;
};

const TestColor testColors[] = {
    {"RED", 255, 0, 0},
    {"GREEN", 0, 255, 0},
    {"BLUE", 0, 70, 255},
    {"WHITE", 255, 255, 255},
};

void drawTestScreen(const char* lastEvent) {
  if (!displayFound) return;

  testDisplay.clear();
  testDisplay.text(4, 1, "ENC " + String(encoderPosition), 2);
  testDisplay.text(4, 18,
                   testEncoder.pressed() ? "BTN DOWN" : "BTN UP", 2);
  testDisplay.text(4, 35, "C " + String(testColors[colorIndex].name), 2);
  testDisplay.progress(4, 53, 120, 10, testVolume);
  testDisplay.show();
}

void showStartupPattern() {
  if (displayFound) {
    testDisplay.clear();
    testDisplay.rect(0, 0, 128, 64);
    testDisplay.line(0, 0, 127, 63);
    testDisplay.line(127, 0, 0, 63);
    testDisplay.centered(8, "DISPLAY", 2);
    testDisplay.centered(27, "OK", 2);
    testDisplay.centered(50, "4 LED TEST");
    testDisplay.show();
  }

  // Four easily distinguishable steps verify the real number and order of LEDs.
  for (uint8_t count = 1; count <= Hardware::LED_COUNT; ++count) {
    testLights.render(count * 100 / Hardware::LED_COUNT, false, true,
                      0, 100, 255, 70, 0);
    delay(450);
  }
  delay(500);
}

void setup() {
  Serial.begin(Hardware::SERIAL_BAUD);
  testEncoder.begin();
  testLights.begin();
  displayFound = testDisplay.begin();
  if (displayFound) testDisplay.setContrast(190);

  showStartupPattern();
  drawTestScreen(displayFound ? "ROTATE OR CLICK" : "OLED NOT FOUND");

  Serial.println();
  Serial.println("SoundDial hardware test started");
  Serial.printf("OLED: %s, LEDs: %u\n", displayFound ? "OK" : "NOT FOUND",
                Hardware::LED_COUNT);
  Serial.printf("CLK=%u DT=%u SW=%u SDA=%u SCL=%u LED=%u\n",
                Hardware::PIN_ENCODER_CLK, Hardware::PIN_ENCODER_DT,
                Hardware::PIN_ENCODER_SW, Hardware::PIN_OLED_SDA,
                Hardware::PIN_OLED_SCL, Hardware::PIN_WS2812);
}

void loop() {
  const InputEvent event = testEncoder.update(false, false);
  const bool pressedNow = testEncoder.pressed();

  if (event.type == InputEventType::Rotate ||
      event.type == InputEventType::HoldRotate) {
    encoderPosition += event.delta;
    testVolume = constrain(static_cast<int>(testVolume) + event.delta * 25,
                           0, 100);
    ++eventNumber;
    drawTestScreen(event.delta > 0 ? "ROTATE RIGHT" : "ROTATE LEFT");
    Serial.printf("#%lu ROTATE %d position=%ld brightness=%u%%\n",
                  static_cast<unsigned long>(eventNumber), event.delta,
                  static_cast<long>(encoderPosition), testVolume);
  } else if (event.type == InputEventType::Click) {
    colorIndex = (colorIndex + 1) % (sizeof(testColors) / sizeof(testColors[0]));
    ++eventNumber;
    drawTestScreen("CLICK - NEXT COLOR");
    Serial.printf("#%lu CLICK color=%s\n",
                  static_cast<unsigned long>(eventNumber),
                  testColors[colorIndex].name);
  } else if (event.type == InputEventType::DoubleClick) {
    testVolume = testVolume == 100 ? 25 : 100;
    ++eventNumber;
    drawTestScreen("DOUBLE - FULL LED");
    Serial.printf("#%lu DOUBLE brightness=%u%%\n",
                  static_cast<unsigned long>(eventNumber), testVolume);
  } else if (event.type == InputEventType::LongPress) {
    encoderPosition = 0;
    testVolume = 25;
    ++eventNumber;
    drawTestScreen("HOLD - RESET");
    Serial.printf("#%lu HOLD reset counters\n",
                  static_cast<unsigned long>(eventNumber));
  }

  if (pressedNow != buttonWasPressed) {
    buttonWasPressed = pressedNow;
    drawTestScreen(pressedNow ? "BUTTON DOWN" : "BUTTON UP");
  }

  const TestColor& color = testColors[colorIndex];
  testLights.render(testVolume, false, true, color.red, color.green,
                    color.blue, 80, 0);
  delay(2);
}

#endif
