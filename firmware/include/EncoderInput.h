#pragma once

#include <Arduino.h>
#include "HardwareConfig.h"

enum class InputEventType : uint8_t { None, Rotate, Click, DoubleClick, TripleClick, QuadrupleClick, LongPress, HoldRotate, HoldRelease };
struct InputEvent {
  InputEventType type;
  int8_t delta;
  InputEvent(InputEventType eventType = InputEventType::None, int8_t eventDelta = 0)
      : type(eventType), delta(eventDelta) {}
};

class EncoderInput {
 public:
  void begin();
  InputEvent update(bool reverse, bool acceleration);
  bool pressed() const { return stablePressed_; }

 private:
  static void IRAM_ATTR handleEncoderChange(void* argument);
  void IRAM_ATTR captureEncoderTransition();

  volatile uint8_t interruptState_ = 0;
  volatile int16_t pendingQuarterSteps_ = 0;
  int16_t quarterSteps_ = 0;
  bool rawPressed_ = false, stablePressed_ = false, longSent_ = false, rotatedWhileHeld_ = false;
  uint32_t rawChangedAt_ = 0, pressedAt_ = 0, lastReleaseAt_ = 0, lastStepAt_ = 0;
  uint8_t clickCount_ = 0;
};
