#include "EncoderInput.h"
#include <driver/gpio.h>

void EncoderInput::begin() {
  pinMode(Hardware::PIN_ENCODER_CLK, INPUT_PULLUP); pinMode(Hardware::PIN_ENCODER_DT, INPUT_PULLUP);
  pinMode(Hardware::PIN_ENCODER_SW, INPUT_PULLUP);
  interruptState_ = (digitalRead(Hardware::PIN_ENCODER_CLK) << 1) | digitalRead(Hardware::PIN_ENCODER_DT);
  rawPressed_ = stablePressed_ = digitalRead(Hardware::PIN_ENCODER_SW) == LOW;
  attachInterruptArg(Hardware::PIN_ENCODER_CLK, handleEncoderChange, this, CHANGE);
  attachInterruptArg(Hardware::PIN_ENCODER_DT, handleEncoderChange, this, CHANGE);
}

void IRAM_ATTR EncoderInput::handleEncoderChange(void* argument) {
  static_cast<EncoderInput*>(argument)->captureEncoderTransition();
}

void IRAM_ATTR EncoderInput::captureEncoderTransition() {
  const uint8_t next =
      (gpio_get_level(static_cast<gpio_num_t>(Hardware::PIN_ENCODER_CLK)) << 1) |
      gpio_get_level(static_cast<gpio_num_t>(Hardware::PIN_ENCODER_DT));
  const uint8_t transition = (interruptState_ << 2) | next;
  int8_t movement = 0;
  switch (transition) {
    case 0b0001: case 0b0111: case 0b1110: case 0b1000:
      movement = -1;
      break;
    case 0b0010: case 0b1011: case 0b1101: case 0b0100:
      movement = 1;
      break;
    default:
      break;
  }
  interruptState_ = next;
  if (movement > 0 && pendingQuarterSteps_ < 120) ++pendingQuarterSteps_;
  else if (movement < 0 && pendingQuarterSteps_ > -120) --pendingQuarterSteps_;
}

InputEvent EncoderInput::update(bool reverse, bool acceleration) {
  const uint32_t now = millis();
  noInterrupts();
  const int16_t capturedQuarterSteps = pendingQuarterSteps_;
  pendingQuarterSteps_ = 0;
  interrupts();
  quarterSteps_ += capturedQuarterSteps;
  if (quarterSteps_ >= 4 || quarterSteps_ <= -4) {
    int16_t detents = quarterSteps_ / 4;
    quarterSteps_ -= detents * 4;
    if (reverse) detents = -detents;
    int16_t multiplier = 1;
    if (acceleration && now-lastStepAt_ < 45) multiplier = 3;
    else if(acceleration && now-lastStepAt_ < 90) multiplier = 2;
    const int8_t delta = static_cast<int8_t>(constrain(detents * multiplier, -24, 24));
    lastStepAt_=now;
    if(stablePressed_) rotatedWhileHeld_=true;
    return {stablePressed_ ? InputEventType::HoldRotate : InputEventType::Rotate, delta};
  }

  const bool raw = digitalRead(Hardware::PIN_ENCODER_SW)==LOW;
  if(raw != rawPressed_) { rawPressed_=raw; rawChangedAt_=now; }
  if(raw != stablePressed_ && now-rawChangedAt_ >= 25) {
    stablePressed_=raw;
    if(raw) { pressedAt_=now; longSent_=false; rotatedWhileHeld_=false; }
    else if(!longSent_ && !rotatedWhileHeld_) { ++clickCount_; lastReleaseAt_=now; }
    else if(rotatedWhileHeld_){rotatedWhileHeld_=false;return {InputEventType::HoldRelease,0};}
  }
  if(stablePressed_ && !longSent_ && !rotatedWhileHeld_ && now-pressedAt_ >= 3000) {
    longSent_=true; clickCount_=0; return {InputEventType::LongPress,0};
  }
  if(clickCount_ && now-lastReleaseAt_ >= 330) {
    const uint8_t count=clickCount_; clickCount_=0;
    if(count>=4)return {InputEventType::QuadrupleClick,0};
    if(count==3)return {InputEventType::TripleClick,0};
    return {count==2 ? InputEventType::DoubleClick : InputEventType::Click,0};
  }
  return {};
}
