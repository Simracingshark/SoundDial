#include <Arduino.h>
#include <esp_system.h>
#include "DeviceSettings.h"
#include "Display.h"
#include "EncoderInput.h"
#include "HardwareConfig.h"
#include "LedRing.h"
#include "NetworkLink.h"

#if !defined(SOUNDDIAL_HARDWARE_TEST)

enum class Screen : uint8_t { Main, Media, MediaSeek, TargetPicker, MediaTargetPicker, SettingsSections, Menu, ProfilePicker, DeviceInfo, ConfirmRestart, ConfirmReset };

struct PcState {
  int profile=0,target=0,volume=0;
  bool muted=false,available=false,connected=false;
  uint8_t red=53,green=167,blue=255,stepOverride=0;
  String name="WAITING FOR PC",subtitle="USB OR WIFI",link="NONE";
};

DeviceSettings settings;
PuckDisplay display;
EncoderInput encoder;
LedRing lights;
NetworkLink network;
PcState state,mediaState;
Screen screen=Screen::Main;
String deviceId, serialInput, message,trackTitle,trackArtist,actionMessage;
String profiles[Hardware::MAX_PROFILES],targets[Hardware::MAX_TARGETS],mediaTargets[Hardware::MAX_MEDIA_TARGETS];
uint8_t profileCount=0,targetCount=0,mediaTargetCount=0,menuSection=0,menuIndex=0,pickerIndex=0,mediaPickerIndex=0,deviceInfoPage=0;
uint8_t linkedMask=0;
bool menuEditing=false,displaySleeping=false,displayPresent=false,hostDashboardArmed=false,dialBatteryConfigLocallyChanged=false;
uint32_t lastInteraction=0,lastStateAt=0,lastHelloAt=0,messageUntil=0,trackUntil=0,actionUntil=0,volumeCardUntil=0,menuSavedUntil=0;
uint32_t mainPreviewUntil=0,mediaPreviewUntil=0;
int mainPreviewVolume=0,mediaPreviewVolume=0;
bool volumeCardMedia=false;
uint8_t batteryPercent=0;
float batteryVoltage=0.0f,batteryAdcVoltage=0.0f;
bool batteryValid=false,seekAvailable=false;
uint32_t batteryReadAt=0,seekLastUsedAt=0,seekRefreshAt=0;
uint32_t seekPositionSeconds=0,seekDurationSeconds=0;
String seekSource="MEDIA";
uint8_t hostBatteryMode=0,hostBatteryPercent=0;
bool hostBatteryAvailable=false,hostPluggedIn=false,hostIdleDashboard=false,hostClockEnabled=false,hostStatusReceived=false;
uint16_t hostIdleSeconds=20;
uint32_t hostLocalSeconds=0,hostStatusAt=0;
constexpr uint32_t kSettingsIdleMs=30000UL;

void readBattery();

constexpr uint8_t kMenuCount=32;
const char* kMenuItems[kMenuCount]={
  "PROFILE","AUTO PROFILE","VOLUME STEP","CONFIRM TARGET",
  "TRACK POPUP","MEDIA TIMEOUT","SEEK HOLD","SEEK STEP","SEEK TIMEOUT",
  "SCREEN SLEEP","INVERT OLED","SCREEN OFF","LED BRIGHT","LIGHT MODE","RAINBOW SPEED","RAINBOW DIR","RAINBOW WIDTH",
  "REVERSE KNOB","ACCELERATION",
  "DIAL BATTERY","EMPTY VOLTAGE","FULL VOLTAGE",
  "LAPTOP BATTERY","IDLE DASH","CLOCK","IDLE AFTER",
  "CONNECTION","WIFI SETUP","WIFI RETRY","DEVICE INFO","RESTART","FACTORY RESET"
};

struct MenuSectionDef { const char* name; uint8_t first; uint8_t count; };
constexpr uint8_t kMenuSectionCount=7;
const MenuSectionDef kMenuSections[kMenuSectionCount]={
  {"SOUND",0,4},
  {"MEDIA",4,5},
  {"DISPLAY & LED",9,8},
  {"CONTROLS",17,2},
  {"BATTERY",19,3},
  {"HOST",22,4},
  {"CONNECT / SYSTEM",26,6}
};

String decodeText(const String& encoded) {
  String result;result.reserve(encoded.length());
  for(size_t i=0;i<encoded.length();++i){if(encoded[i]=='%'&&i+2<encoded.length()){
    char hex[3]={encoded[i+1],encoded[i+2],0};char* end=nullptr;long value=strtol(hex,&end,16);if(end&&*end==0){result+=static_cast<char>(value);i+=2;continue;}}
    result+=encoded[i];}
  return result;
}

int splitTabs(const String& value,String parts[],int capacity) {
  int count=0,start=0;for(size_t i=0;i<=value.length()&&count<capacity;++i)if(i==value.length()||value[i]=='\t'){parts[count++]=value.substring(start,i);start=i+1;}return count;
}

void parseList(const String& value,String destination[],uint8_t capacity,uint8_t& count) {
  count=0;int start=0;for(size_t i=0;i<=value.length()&&count<capacity;++i)if(i==value.length()||value[i]=='|'){destination[count++]=decodeText(value.substring(start,i));start=i+1;}
}

bool usbPreferred() { return state.link=="USB" && millis()-lastStateAt<5000; }

bool previewActive(uint32_t until) {
  return until != 0 && static_cast<int32_t>(until - millis()) > 0;
}

int visibleVolume(const PcState& current,bool media) {
  const uint32_t until=media?mediaPreviewUntil:mainPreviewUntil;
  if(previewActive(until))return media?mediaPreviewVolume:mainPreviewVolume;
  return current.volume;
}

void previewRotation(PcState& current,bool media,int delta) {
  if(!current.connected||!current.available||delta==0)return;
  int& preview=media?mediaPreviewVolume:mainPreviewVolume;
  uint32_t& until=media?mediaPreviewUntil:mainPreviewUntil;
  const int base=previewActive(until)?preview:current.volume;
  const int step=current.stepOverride?current.stepOverride:settings.volumeStep;
  preview=constrain(base+delta*step,0,100);
  // If the PC never acknowledges the command, the preview automatically
  // disappears and the last confirmed volume becomes visible again.
  // Spotify can take several state polls to report the new value. Keep the
  // locally predicted number visible until the PC confirms it instead of
  // showing the old value creeping upward in several visible jumps.
  until=millis()+900;
}

void sendEvent(const String& value) {
  if(settings.linkMode==LinkMode::Offline)return;
  if(settings.linkMode==LinkMode::Usb || settings.linkMode==LinkMode::Auto) {
    if(usbPreferred() || settings.linkMode==LinkMode::Usb){Serial.println(value);return;}
  }
  if(settings.linkMode==LinkMode::Wifi || settings.linkMode==LinkMode::Auto) {
    if(network.sendLine(value))return;
  }
  Serial.println(value);
}

void sendDeviceConfig(){
  const String value="DEVICE_CONFIG\t"+String((int)settings.linkMode)+"\t"+String(settings.volumeStep)+"\t"+String(settings.targetConfirm?1:0)+"\t"+String(settings.trackPopupSeconds)+"\t"+String(settings.mediaTimeoutSeconds)+"\t"+String(settings.screenTimeoutSeconds)+"\t"+String(settings.ledBrightness)+"\t"+String((int)settings.lightMode)+"\t"+String(settings.oledInverted?1:0)+"\t"+String(settings.encoderReversed?1:0)+"\t"+String(settings.acceleration?1:0)+"\t"+String(settings.solidRed)+"\t"+String(settings.solidGreen)+"\t"+String(settings.solidBlue)+"\t"+String(settings.automaticProfiles?1:0)+"\t"+String(settings.rainbowSpeed)+"\t"+String(settings.rainbowReverse?1:0)+"\t"+String(settings.rainbowSpread)+"\t"+network.addressText()+"\t"+String(settings.batteryMonitor?1:0)+"\t"+String(settings.seekStepSeconds)+"\t"+String(settings.seekTimeoutSeconds)+"\t"+String(settings.seekHoldMs);
  // Device settings are a recovery channel too.  Always answer USB, even if
  // the user has selected WiFi or OFFLINE, and mirror the reply to WiFi when
  // it is currently connected.
  Serial.println(value);
  network.sendLine(value);
}

void sendHostConfig(){
  const char* mode=hostBatteryMode==1?"ICON":hostBatteryMode==2?"PERCENT":"OFF";
  sendEvent("HOST_CONFIG\t"+String(mode)+"\t"+String(hostIdleDashboard?1:0)+"\t"+
      String(hostClockEnabled?1:0)+"\t"+String(hostIdleSeconds));
  menuSavedUntil=millis()+900;
}

void sendDialBatteryConfig(){
  const char* mode=settings.batteryDisplayMode==1?"ICON":
      settings.batteryDisplayMode==2?"PERCENT":"OFF";
  sendEvent("DIAL_BATTERY_CONFIG\t"+String(settings.batteryMinVoltage,2)+"\t"+
      String(settings.batteryMaxVoltage,2)+"\t"+String(mode));
  menuSavedUntil=millis()+900;
}

void handleLine(String line) {
  line.trim();if(line.isEmpty())return;String p[25];int count=splitTabs(line,p,25);
  if(p[0]=="PING"){sendEvent("HELLO\tSOUNDDIAL\t"+deviceId);return;}
  if(p[0]=="HOST_STATUS"&&count>=9){
    String mode=p[1];mode.toUpperCase();
    hostBatteryAvailable=p[2]=="1";hostBatteryPercent=constrain(p[3].toInt(),0,100);
    hostPluggedIn=p[4]=="1";hostLocalSeconds=constrain(p[5].toInt(),0,86399);
    // A periodic PC status packet must not overwrite a value while the user
    // is turning the knob in the HOST settings editor.
    const bool editingHost=screen==Screen::Menu&&menuEditing&&menuIndex>=22&&menuIndex<=25;
    if(!editingHost){
      hostBatteryMode=mode=="ICON"||mode=="BADGE"?1:
          mode=="PERCENT"||mode=="IDLE"?2:0;
      hostIdleDashboard=p[6]=="1";hostClockEnabled=p[7]=="1";
      hostIdleSeconds=constrain(p[8].toInt(),5,3600);
    }
    hostStatusAt=millis();hostStatusReceived=true;return;
  }
  if(p[0]=="HOST_ALERT"&&count>=2){
    actionMessage=decodeText(p[1]);actionUntil=millis()+5000;
    lastInteraction=millis();
    if(displaySleeping){display.sleep(false);displaySleeping=false;}
    return;
  }
  if(p[0]=="GET_DEVICE_CONFIG"){sendDeviceConfig();return;}
  if(p[0]=="SET_BATTERY_RANGE"&&count>=3){
    const float minimum=p[1].toFloat(),maximum=p[2].toFloat();
    if(minimum>=2.5f&&minimum<=3.9f&&maximum>=3.6f&&maximum<=4.5f&&maximum-minimum>=0.3f){
      settings.batteryMinVoltage=minimum;settings.batteryMaxVoltage=maximum;
      if(count>=4&&!dialBatteryConfigLocallyChanged){
        String view=p[3];view.toUpperCase();
        settings.batteryDisplayMode=view=="OFF"?0:view=="ICON"?1:2;
      }
      // ICON/PERCENT explicitly requests a dial battery reading. Do not let
      // a stale BatteryMonitor=false from the desktop disable GPIO0 later.
      if(settings.batteryDisplayMode!=0)settings.batteryMonitor=true;
      settings.save();readBattery();batteryReadAt=millis();
    }
    return;
  }
  if(p[0]=="SET_DEVICE_CONFIG"&&count>=12){
    settings.linkMode=static_cast<LinkMode>(constrain(p[1].toInt(),0,3));
    const int step=p[2].toInt();settings.volumeStep=(step==1||step==2||step==4||step==5||step==10)?step:4;
    settings.targetConfirm=p[3]=="1";const int popup=p[4].toInt();settings.trackPopupSeconds=(popup==0||popup==3||popup==5||popup==10)?popup:5;
    settings.mediaTimeoutSeconds=constrain(p[5].toInt(),0,3600);settings.screenTimeoutSeconds=constrain(p[6].toInt(),0,3600);
    settings.ledBrightness=constrain(p[7].toInt(),0,160);settings.lightMode=static_cast<LightMode>(constrain(p[8].toInt(),0,3));
    settings.oledInverted=p[9]=="1";settings.encoderReversed=p[10]=="1";settings.acceleration=p[11]=="1";
    if(count>=15){settings.solidRed=constrain(p[12].toInt(),0,255);settings.solidGreen=constrain(p[13].toInt(),0,255);settings.solidBlue=constrain(p[14].toInt(),0,255);}
    if(count>=19){settings.automaticProfiles=p[15]=="1";settings.rainbowSpeed=constrain(p[16].toInt(),0,2);settings.rainbowReverse=p[17]=="1";settings.rainbowSpread=constrain(p[18].toInt(),16,96);}
    if(count>=22){
      settings.batteryMonitor=p[19]=="1"||settings.batteryDisplayMode!=0;
      const int seekStep=p[20].toInt();settings.seekStepSeconds=(seekStep==5||seekStep==10||seekStep==15||seekStep==30)?seekStep:5;
      const int seekExit=p[21].toInt();settings.seekTimeoutSeconds=(seekExit==0||seekExit==3||seekExit==5||seekExit==10)?seekExit:5;
    }
    if(count>=23){const int seekHold=p[22].toInt();settings.seekHoldMs=(seekHold==800||seekHold==1200||seekHold==1500||seekHold==2000)?seekHold:1200;}
    display.invert(settings.oledInverted);settings.save();message="SETTINGS OK";messageUntil=millis()+1800;sendDeviceConfig();return;
  }
  if(p[0]=="STATE"&&count>=12){
    const bool wasConnected=state.connected;
    const int oldProfile=state.profile,oldTarget=state.target;
    state.profile=p[1].toInt();state.target=p[2].toInt();state.volume=constrain(p[3].toInt(),0,100);state.muted=p[4]=="1";state.available=p[5]=="1";
    state.link=p[6];state.red=constrain(p[7].toInt(),0,255);state.green=constrain(p[8].toInt(),0,255);state.blue=constrain(p[9].toInt(),0,255);
    state.name=decodeText(p[10]);state.subtitle=decodeText(p[11]);if(state.subtitle=="~")state.subtitle="";state.stepOverride=count>=13?constrain(p[12].toInt(),0,10):0;state.connected=true;lastStateAt=millis();
    if(oldProfile!=state.profile||oldTarget!=state.target)mainPreviewUntil=0;
    else if(previewActive(mainPreviewUntil)&&abs(state.volume-mainPreviewVolume)<=1)mainPreviewUntil=0;
    if(!wasConnected){actionMessage="CONNECTED";actionUntil=millis()+900;}
    if(screen==Screen::Main)pickerIndex=state.target;return;
  }
  if(p[0]=="MEDIA_STATE"&&count>=10){
    const int oldTarget=mediaState.target;mediaState.target=p[1].toInt();if(screen!=Screen::MediaTargetPicker)mediaPickerIndex=mediaState.target;
    mediaState.volume=constrain(p[2].toInt(),0,100);mediaState.muted=p[3]=="1";mediaState.available=p[4]=="1";
    mediaState.red=constrain(p[5].toInt(),0,255);mediaState.green=constrain(p[6].toInt(),0,255);mediaState.blue=constrain(p[7].toInt(),0,255);
    mediaState.name=decodeText(p[8]);mediaState.subtitle=decodeText(p[9]);if(mediaState.subtitle=="~")mediaState.subtitle="";
    // While the user is marking targets, linkedMask is a local draft. The PC
    // still reports the previously applied mask, so accepting it here would
    // erase every MARKED toggle on the next state refresh (~100 ms).
    if(count>=11&&screen!=Screen::MediaTargetPicker)linkedMask=constrain(p[10].toInt(),0,255);mediaState.stepOverride=count>=12?constrain(p[11].toInt(),0,10):0;
    if(oldTarget!=mediaState.target)mediaPreviewUntil=0;
    else if(previewActive(mediaPreviewUntil)&&abs(mediaState.volume-mediaPreviewVolume)<=1)mediaPreviewUntil=0;
    mediaState.link=state.link;mediaState.connected=true;lastStateAt=millis();return;
  }
  if(p[0]=="TARGETS"&&count>=2){parseList(p[1],targets,Hardware::MAX_TARGETS,targetCount);return;}
  if(p[0]=="MEDIA_TARGETS"&&count>=2){parseList(p[1],mediaTargets,Hardware::MAX_MEDIA_TARGETS,mediaTargetCount);return;}
  if(p[0]=="PROFILES"&&count>=2){parseList(p[1],profiles,Hardware::MAX_PROFILES,profileCount);return;}
  if(p[0]=="MESSAGE"&&count>=2){message=decodeText(p[1]);messageUntil=millis()+2200;return;}
  if(p[0]=="TRACK"&&count>=3&&settings.trackPopupSeconds){trackTitle=decodeText(p[1]);trackArtist=decodeText(p[2]);if(trackTitle.length()){lastInteraction=millis();if(displaySleeping){display.sleep(false);displaySleeping=false;}trackUntil=millis()+settings.trackPopupSeconds*1000UL;}return;}
  if(p[0]=="MEDIA_TIMELINE"&&count>=5){seekAvailable=p[1]=="1";seekPositionSeconds=static_cast<uint32_t>(max(0L,p[2].toInt()));seekDurationSeconds=static_cast<uint32_t>(max(0L,p[3].toInt()));seekSource=decodeText(p[4]);return;}
}

void readUsb() {
  while(Serial.available()){char ch=Serial.read();if(ch=='\n'){String line=serialInput;serialInput="";handleLine(line);}else if(ch!='\r'&&serialInput.length()<1024)serialInput+=ch;}
}

void readNetwork(){String line;while(network.readLine(line))handleLine(line);}

void wakeDisplay(){lastInteraction=millis();if(displaySleeping){display.sleep(false);displaySleeping=false;}}

String connectionValue(){switch(settings.linkMode){case LinkMode::Auto:return "AUTO";case LinkMode::Usb:return "USB";case LinkMode::Wifi:return "WIFI";default:return "OFFLINE";}}
String lightValue(){switch(settings.lightMode){case LightMode::Volume:return "VOLUME";case LightMode::Solid:return "SOLID";case LightMode::Rainbow:return "RAINBOW";default:return "OFF";}}
String rainbowSpeedValue(){return settings.rainbowSpeed==0?"SLOW":settings.rainbowSpeed==2?"FAST":"NORMAL";}
String rainbowWidthValue(){return settings.rainbowSpread<=24?"TIGHT":settings.rainbowSpread<=48?"NORMAL":settings.rainbowSpread<=64?"WIDE":"MAX";}
String timeoutValue(uint16_t seconds){return seconds==0?"NEVER":String(seconds)+" SEC";}
String batteryViewValue(){return settings.batteryDisplayMode==0?"OFF":settings.batteryDisplayMode==1?"ICON":"PERCENT";}
uint8_t linkedMediaCount(){uint8_t count=0;for(uint8_t bit=0;bit<Hardware::MAX_MEDIA_TARGETS;++bit)if(linkedMask&(1U<<bit))++count;return count;}
bool linkedMediaActive(){return linkedMediaCount()>=2;}

uint8_t batteryPercentFor(float volts){
  const float point[]={0.00f,0.20f,0.33f,0.47f,0.60f,0.73f,0.87f,1.00f};
  const uint8_t percent[]={0,10,25,45,65,80,90,100};
  const float normalized=(volts-settings.batteryMinVoltage)/
      (settings.batteryMaxVoltage-settings.batteryMinVoltage);
  if(normalized<=0.0f)return 0;if(normalized>=1.0f)return 100;
  for(uint8_t i=1;i<8;++i)if(normalized<=point[i]){
    const float part=(normalized-point[i-1])/(point[i]-point[i-1]);
    return percent[i-1]+roundf(part*(percent[i]-percent[i-1]));
  }
  return 0;
}

void readBattery(){
  if(!settings.batteryMonitor&&settings.batteryDisplayMode==0){batteryValid=false;return;}
  uint16_t millivoltSamples[33],rawSamples[33];
  for(uint8_t i=0;i<33;++i){
    rawSamples[i]=analogRead(Hardware::PIN_BATTERY_ADC);
    millivoltSamples[i]=analogReadMilliVolts(Hardware::PIN_BATTERY_ADC);
    delayMicroseconds(180);
  }
  for(uint8_t i=1;i<33;++i){
    uint16_t value=millivoltSamples[i];int8_t j=i-1;while(j>=0&&millivoltSamples[j]>value){millivoltSamples[j+1]=millivoltSamples[j];--j;}millivoltSamples[j+1]=value;
    value=rawSamples[i];j=i-1;while(j>=0&&rawSamples[j]>value){rawSamples[j+1]=rawSamples[j];--j;}rawSamples[j+1]=value;
  }
  // Average the quiet centre of the sample set. This rejects spikes even
  // when the divider has no capacitor fitted.
  uint32_t millivoltTotal=0,rawTotal=0;for(uint8_t i=8;i<25;++i){millivoltTotal+=millivoltSamples[i];rawTotal+=rawSamples[i];}
  const float calibratedPinVolts=(millivoltTotal/17.0f)/1000.0f;
  const float rawPinVolts=(rawTotal/17.0f)*2.50f/4095.0f;
  // Some ESP32-C3 Arduino/core combinations return zero from the calibrated
  // millivolt API even though analogRead() is valid. At 11 dB attenuation the
  // C3's useful full-scale range is roughly 2.5 V, which is sufficient for
  // the 2:1 divider. Use the raw ADC as a diagnostic-safe fallback.
  const float calibrated=calibratedPinVolts*Hardware::BATTERY_DIVIDER_RATIO;
  const float raw=rawPinVolts*Hardware::BATTERY_DIVIDER_RATIO;
  const bool calibratedValid=calibrated>1.0f&&calibrated<5.0f;
  const bool rawValid=raw>1.0f&&raw<5.0f;
  float measured=0.0f;
  if(calibratedValid&&rawValid&&batteryValid)
    measured=fabsf(calibrated-batteryVoltage)<=fabsf(raw-batteryVoltage)?calibrated:raw;
  else if(calibratedValid)measured=calibrated;
  else if(rawValid)measured=raw;
  else{
    // Once a real battery reading has been obtained, a transient bad ADC
    // batch must not replace it with CHECK GPIO0 five seconds later.
    if(batteryValid)return;
    batteryAdcVoltage=0.0f;batteryPercent=0;return;
  }
  batteryAdcVoltage=measured/Hardware::BATTERY_DIVIDER_RATIO;
  batteryVoltage=batteryValid?batteryVoltage*0.78f+measured*0.22f:measured;
  batteryValid=batteryVoltage>1.0f&&batteryVoltage<5.0f;
  batteryPercent=batteryValid?batteryPercentFor(batteryVoltage):0;
}

String clockText(uint32_t seconds){
  char value[12];snprintf(value,sizeof(value),"%lu:%02lu",seconds/60,seconds%60);return value;
}

String topClockText(uint32_t seconds){
  // The full value remains on the large timeline row.  The laptop header has
  // a strict four-character zone, so ten minutes and above use a clear 12m.
  if(seconds>=600)return String(seconds/60)+"m";
  return clockText(seconds);
}

void renderBatteryBadge(int16_t centerX=64){
  if(!settings.batteryMonitor||settings.batteryDisplayMode==0)return;
  if(settings.batteryDisplayMode==2){
    const String value=batteryValid?String(batteryPercent)+"%":"--%";
    display.textMedium(max(0,centerX-display.textWidthMedium(value)/2),0,value);
    return;
  }
  const int16_t x=centerX-9;
  constexpr int16_t y=3,w=17,h=9;
  display.rect(x,y,w,h);display.rect(x+w,y+2,2,h-4,true);
  if(!batteryValid){
    display.line(x+3,y+2,x+w-4,y+h-3);
    display.line(x+w-4,y+2,x+3,y+h-3);
    return;
  }
  const int16_t fill=constrain((batteryPercent*(w-4)+50)/100,0,w-4);
  if(fill>0)display.rect(x+2,y+2,fill,h-4,true);
}

bool hostStatusFresh(){return hostStatusReceived&&millis()-hostStatusAt<60000UL;}

String hostBatteryText(bool longLabel=false){
  if(!hostStatusFresh()||!hostBatteryAvailable||hostBatteryMode==0)return "";
  String value=longLabel?"LAPTOP ":"L";
  if(!longLabel&&hostPluggedIn)value+="+";
  value+=String(hostBatteryPercent);
  if(longLabel||!hostPluggedIn)value+="%";
  if(longLabel&&hostPluggedIn)value+=" +";
  return value;
}

String topModeLabel(String value){
  value.toUpperCase();
  // Six-character LINK16 no longer fits beside two enlarged battery slots.
  // L16 remains unambiguous and preserves the readable 7x10 font everywhere.
  if(value.startsWith("LINK "))return "L"+value.substring(5);
  return value;
}

// Compact labelled gauge for the top status row.  A laptop and the
// dial use the same visual language, while L/D keeps them unambiguous.
void renderTopBatteryGauge(int16_t x,char label,bool valid,uint8_t percent){
  display.textStatus(x,2,String(label));
  constexpr int16_t y=3,w=18,h=8;
  const int16_t batteryX=x+8;
  display.rect(batteryX,y,w,h);
  if(!valid){
    display.line(batteryX+3,y+2,batteryX+w-4,y+h-3);
    display.line(batteryX+w-4,y+2,batteryX+3,y+h-3);
    return;
  }
  const int16_t fill=constrain((percent*(w-4)+50)/100,0,w-4);
  if(fill>0)display.rect(batteryX+2,y+2,fill,h-4,true);
}

void renderTopBatteryPercent(int16_t x,char label,bool valid,uint8_t percent){
  String value=String(label);
  value+=valid?String(percent):String("--");
  // At 100 the value is already unambiguous and omitting '%' keeps every
  // battery inside the same four-character slot: +100 / L100 / D100.
  if(!valid||percent<100)value+="%";
  display.textStatusCompact(x,2,value);
}

void renderTopStatus(const String& fallback,const String& rightText="",bool compactRight=false){
  const bool showHost=hostBatteryMode!=0&&hostStatusFresh()&&hostBatteryAvailable;
  const String mode=topModeLabel(fallback);
  if(!showHost){
    // Desktop layout: there is enough room for the readable 8x14 font.  The
    // dial battery is centred on the full OLED, independent of label width.
    display.textMedium(0,0,mode);
    if(settings.batteryMonitor&&settings.batteryDisplayMode!=0)
      renderBatteryBadge(64);
    if(rightText.length()){
      // Four characters (64%, MUTE, 1:43) fit the approved large layout.
      // Longer seek times fall back only the value itself to the condensed
      // font; MEDIA/SEEK/USB and the centred battery stay full-sized.
      if(compactRight&&rightText.length()>4){
        const int16_t width=display.textWidthCondensed(rightText);
        display.textCondensed(max(84,128-width),0,rightText);
      }else{
        const int16_t width=display.textWidthMedium(rightText);
        display.textMedium(max(84,128-width),0,rightText);
      }
    }
    return;
  }

  // Laptop layout: four fixed, non-overlapping zones use one 6x10 font.
  // A plus replaces L while charging, so that state stays visible.
  display.textStatus(0,2,mode);
  constexpr int16_t hostX=43,dialX=74;
  const char hostLabel=hostPluggedIn?'+':'L';
  if(hostBatteryMode==1)renderTopBatteryGauge(hostX,hostLabel,true,hostBatteryPercent);
  else renderTopBatteryPercent(hostX,hostLabel,true,hostBatteryPercent);
  if(settings.batteryMonitor&&settings.batteryDisplayMode!=0){
    if(settings.batteryDisplayMode==1)renderTopBatteryGauge(dialX,'D',batteryValid,batteryPercent);
    else renderTopBatteryPercent(dialX,'D',batteryValid,batteryPercent);
  }
  if(rightText.length()){
    const int16_t width=display.textWidthStatusCompact(rightText);
    // All top-right values are at most four characters: 100%, MUTE, 9:59 or
    // the compact minute form used after ten minutes.
    display.textStatusCompact(max(101,128-width),2,rightText);
  }
}

bool hostDashboardActive(){
  if(!hostDashboardArmed||!hostStatusFresh()||!hostIdleDashboard||!state.connected)return false;
  if(millis()-lastInteraction<hostIdleSeconds*1000UL)return false;
  return hostClockEnabled||hostBatteryMode!=0||
      (settings.batteryMonitor&&settings.batteryDisplayMode!=0);
}

void renderHostDashboard(){
  display.clear();
  if(hostClockEnabled){
    const uint32_t seconds=(hostLocalSeconds+(millis()-hostStatusAt)/1000UL)%86400UL;
    char value[6];snprintf(value,sizeof(value),"%02lu:%02lu",seconds/3600UL,(seconds/60UL)%60UL);
    display.centered(2,value,2);display.line(10,20,117,20);
  }else{
    display.centeredMedium(0,"HOST STATUS");display.line(0,15,127,15);
  }
  String laptop=hostBatteryText(true);
  String dial;
  if(settings.batteryMonitor&&settings.batteryDisplayMode!=0)
    dial=batteryValid?String("DIAL ")+String(batteryPercent)+"%":"DIAL --%";
  if(laptop.length()&&dial.length()){
    display.centeredMedium(25,laptop);display.centeredMedium(44,dial);
  }else if(laptop.length()||dial.length()){
    display.centeredMedium(36,laptop.length()?laptop:dial);
  }else{
    display.centeredMedium(36,state.link);
  }
  display.show();
}

// The medium font is 14 pixels tall like the old 2x font, but it is narrow
// enough for fourteen characters. Long labels scroll instead of colliding
// with neighbouring UI elements or disappearing outside the visible matrix.
String uiText(const String& value) {
  String source=value;
  source.toUpperCase();
  constexpr size_t visibleCharacters=14;
  if(source.length()<=visibleCharacters)return source;
  String loop=source+"   "+source;
  const size_t positions=source.length()+3;
  const size_t offset=(millis()/450)%positions;
  return loop.substring(offset,offset+visibleCharacters);
}

String panelText(const String& value) {
  String source=value;
  source.toUpperCase();
  constexpr size_t visibleCharacters=13;
  if(source.length()<=visibleCharacters)return source;
  String loop=source+"   "+source;
  const size_t positions=source.length()+3;
  const size_t offset=(millis()/450)%positions;
  return loop.substring(offset,offset+visibleCharacters);
}

void centeredPanel(int16_t y,const String& value) {
  const String fitted=panelText(value);
  display.textMedium(max(0,(128-display.textWidthMedium(fitted))/2),y,fitted);
}

String menuLine(const String& value){
  String source=value;source.toUpperCase();
  constexpr size_t visibleCharacters=14;
  if(source.length()<=visibleCharacters)return source;
  String loop=source+"   "+source;
  const size_t positions=source.length()+3;
  const size_t offset=(millis()/450)%positions;
  return loop.substring(offset,offset+visibleCharacters);
}

void renderListRow(int16_t y,const String& value,bool selected){
  if(selected){
    display.pixel(0,y+6);display.pixel(1,y+5);display.pixel(1,y+6);
    display.pixel(1,y+7);display.pixel(2,y+6);
  }
  display.textMenu(6,y,menuLine(value));
}

void showAction(const String& value,uint16_t duration=850){actionMessage=value;actionUntil=millis()+duration;}

String menuValue(uint8_t index) {
  switch(index){
    case 0:return profileCount?profiles[min((int)state.profile,(int)profileCount-1)]:"DEFAULT";
    case 1:return settings.automaticProfiles?"ON":"OFF";case 2:return String(settings.volumeStep)+"%";case 3:return settings.targetConfirm?"YES":"NO";
    case 4:return settings.trackPopupSeconds?String(settings.trackPopupSeconds)+" SEC":"OFF";case 5:return timeoutValue(settings.mediaTimeoutSeconds);
    case 6:return settings.seekHoldMs==800?"0.8 SEC":String(settings.seekHoldMs/1000.0f,1)+" SEC";
    case 7:return String(settings.seekStepSeconds)+" SEC";case 8:return settings.seekTimeoutSeconds?String(settings.seekTimeoutSeconds)+" SEC":"NEVER";
    case 9:return timeoutValue(settings.screenTimeoutSeconds);case 10:return settings.oledInverted?"YES":"NO";case 11:return "PRESS";
    case 12:return String(settings.ledBrightness);case 13:return lightValue();case 14:return rainbowSpeedValue();
    case 15:return settings.rainbowReverse?"COUNTER":"CLOCKWISE";case 16:return rainbowWidthValue();
    case 17:return settings.encoderReversed?"YES":"NO";case 18:return settings.acceleration?"YES":"NO";
    case 19:return batteryViewValue();case 20:return String(settings.batteryMinVoltage,2)+" V";case 21:return String(settings.batteryMaxVoltage,2)+" V";
    case 22:return hostBatteryMode==1?"ICON":hostBatteryMode==2?"PERCENT":"OFF";
    case 23:return hostIdleDashboard?"ON":"OFF";case 24:return hostClockEnabled?"ON":"OFF";
    case 25:return String(hostIdleSeconds)+" SEC";
    case 26:return connectionValue();case 27:return network.portalActive()?"ACTIVE":"START";case 29:return network.addressText();
    default:return "PRESS";
  }
}

bool menuItemVisible(uint8_t index){
  if(index>=14&&index<=16)return settings.lightMode==LightMode::Rainbow;
  if(index>=20&&index<=21)return settings.batteryDisplayMode!=0;
  return true;
}

uint8_t visibleMenuCount(const MenuSectionDef& section){
  uint8_t count=0;
  for(uint8_t i=0;i<section.count;++i)if(menuItemVisible(section.first+i))++count;
  return count;
}

uint8_t menuItemAt(const MenuSectionDef& section,uint8_t position){
  uint8_t visible=0;
  for(uint8_t i=0;i<section.count;++i){
    const uint8_t index=section.first+i;
    if(!menuItemVisible(index))continue;
    if(visible++==position)return index;
  }
  return section.first;
}

uint8_t menuItemPosition(const MenuSectionDef& section,uint8_t index){
  uint8_t visible=0;
  for(uint8_t i=0;i<section.count;++i){
    const uint8_t candidate=section.first+i;
    if(!menuItemVisible(candidate))continue;
    if(candidate==index)return visible;
    ++visible;
  }
  return 0;
}

void renderActionToast() {
  display.clear();
  display.centeredMedium(7,"SOUNDDIAL");
  display.line(12,24,115,24);
  display.centeredMedium(35,uiText(actionMessage));
  display.show();
}

void renderVolumeCard(PcState& current,bool media) {
  display.clear();
  const uint8_t linked=media?linkedMediaCount():0;
  const String heading=media&&linked>=2?"LINK "+String(linked):current.name;
  display.centeredMedium(0,uiText(heading));
  display.line(0,15,127,15);
  const int shown=visibleVolume(current,media);
  const String value=current.muted?"MUTE":String(shown)+"%";
  display.centered(22,value,2);
  display.progress(4,51,120,8,shown);
  display.show();
}

void renderMain() {
  display.clear();
  if(messageUntil>millis()){
    display.centeredMedium(0,"MESSAGE");display.line(0,15,127,15);
    display.centeredMedium(27,uiText(message));display.show();return;
  }
  if(!state.connected&&settings.linkMode!=LinkMode::Offline){
    String title=settings.linkMode==LinkMode::Usb?"USB WAIT":network.statusTitle();
    if(title=="CONNECTED")title="WAIT FOR PC";
    const uint8_t dots=(millis()/350)%4;for(uint8_t i=0;i<dots&&title.length()<14;++i)title+='.';
    display.textMedium(1,0,settings.linkMode==LinkMode::Usb?"USB":"WIFI");renderBatteryBadge();
    display.centeredMedium(20,uiText(title));
    display.centeredMedium(40,settings.linkMode==LinkMode::Usb?"START PC APP":network.portalActive()?"CONNECT AP":network.connected()?"PC APP CLOSED":uiText(network.statusDetail()));
    display.show();return;
  }
  const int shown=visibleVolume(state,false);
  String volume=state.muted?"MUTE":String(shown)+"%";
  renderTopStatus(uiText(state.link),volume);
  display.line(0,15,127,15);
  if(state.available&&state.subtitle.length()==0){
    display.centeredMedium(27,uiText(state.name));
  }else{
    display.centeredMedium(18,uiText(state.name));
    display.centeredMedium(35,state.available?uiText(state.subtitle):"NOT RUN");
  }
  display.progress(4,51,120,8,shown);
  display.show();
}

void renderMedia() {
  display.clear();
  const uint8_t linked=linkedMediaCount();
  const String mode=linked>=2?"LINK "+String(linked):"MEDIA";
  if(!mediaState.connected){
    renderTopStatus(mode);
    display.line(0,15,127,15);
    display.centeredMedium(22,"PC APP CLOSED");
    display.centeredMedium(42,"WAITING");
    display.show();return;
  }
  const int shown=visibleVolume(mediaState,true);
  String volume=mediaState.muted?"MUTE":String(shown)+"%";
  renderTopStatus(mode,volume);
  display.line(0,15,127,15);
  display.centeredMedium(18,uiText(mediaState.name));
  display.centeredMedium(35,mediaState.available?(linkedMediaActive()?uiText(mediaState.subtitle):"CLICK PLAY"):"NOT RUN");
  display.progress(4,51,120,8,shown);
  display.show();
}

void renderSeek(){
  display.clear();renderTopStatus("SEEK",topClockText(seekPositionSeconds),true);
  display.line(0,15,127,15);
  display.centeredMedium(19,seekAvailable?uiText(seekSource):"NO SEEK");
  display.centeredMedium(37,seekAvailable?(seekDurationSeconds?(clockText(seekPositionSeconds)+" / "+clockText(seekDurationSeconds)):(clockText(seekPositionSeconds)+" / --:--")):"HOLD BACK");
  const uint8_t progress=seekDurationSeconds?constrain((seekPositionSeconds*100UL)/seekDurationSeconds,0UL,100UL):0;
  display.progress(4,51,120,8,progress);display.show();
}

void renderTrack(){display.clear();display.centeredMedium(0,"NOW PLAY");display.line(0,15,127,15);display.centeredMedium(19,uiText(trackTitle));display.centeredMedium(39,uiText(trackArtist.length()?trackArtist:"UNKNOWN"));display.show();}

void renderPicker(const char* title,String values[],uint8_t count,uint8_t selected) {
  display.clear();
  const bool mediaPicker=String(title).startsWith("MEDIA");
  const uint8_t linkedCount=linkedMediaCount();
  String heading=String(title).startsWith("SELECT P")?"PROFILE ":mediaPicker&&linkedCount?"LINK ":mediaPicker?"MEDIA ":"TARGET ";
  if(mediaPicker&&linkedCount)heading+=String(linkedCount)+" ";
  heading+=count?String(selected+1)+"/"+String(count):"0/0";
  centeredPanel(0,heading);display.line(0,15,127,15);
  if(!count){display.centeredMedium(25,"EMPTY");display.show();return;}
  centeredPanel(18,values[selected]);
  if(mediaPicker)centeredPanel(34,linkedMask&(1U<<selected)?"MARKED":"TURN/DBL");
  else centeredPanel(34,"TURN");
  const bool profilePicker=String(title).startsWith("SELECT P");
  centeredPanel(50,mediaPicker&&linkedCount>=2?"CLICK GO":(profilePicker||settings.targetConfirm)?"CLICK OK":"RELEASE");
  display.show();
}

void renderSettingsSections(){
  display.clear();
  centeredPanel(0,"SETTINGS "+String(menuSection+1)+"/"+String(kMenuSectionCount));
  display.line(0,15,127,15);
  const uint8_t previous=(menuSection+kMenuSectionCount-1)%kMenuSectionCount;
  const uint8_t next=(menuSection+1)%kMenuSectionCount;
  renderListRow(17,kMenuSections[previous].name,false);
  renderListRow(33,kMenuSections[menuSection].name,true);
  renderListRow(49,kMenuSections[next].name,false);
  display.show();
}

void renderMenu() {
  const MenuSectionDef& section=kMenuSections[menuSection];
  const uint8_t count=visibleMenuCount(section);
  const uint8_t position=menuItemPosition(section,menuIndex);
  display.clear();
  if(!menuEditing){
    centeredPanel(0,String(section.name)+" "+String(position+1)+"/"+String(count));
    display.line(0,15,127,15);
    if(count==1){
      renderListRow(33,kMenuItems[menuIndex],true);
    }else if(count==2){
      const uint8_t other=menuItemAt(section,(position+1)%count);
      renderListRow(24,kMenuItems[menuIndex],true);
      renderListRow(42,kMenuItems[other],false);
    }else{
      const uint8_t previous=menuItemAt(section,(position+count-1)%count);
      const uint8_t next=menuItemAt(section,(position+1)%count);
      renderListRow(17,kMenuItems[previous],false);
      renderListRow(33,kMenuItems[menuIndex],true);
      renderListRow(49,kMenuItems[next],false);
    }
  }else{
    const String header="EDIT "+String(section.name)+" "+String(position+1)+"/"+String(count);
    centeredPanel(0,header);display.line(0,15,127,15);
    centeredPanel(18,kMenuItems[menuIndex]);
    centeredPanel(34,menuValue(menuIndex));
    centeredPanel(48,"CLICK SAVE");
  }
  display.show();
}

void renderDeviceInfo(){
  display.clear();
  if(deviceInfoPage==0){
    display.centeredMedium(0,"NETWORK 1/3");display.line(0,15,127,15);
    centeredPanel(20,network.addressText());
    centeredPanel(42,state.connected?state.link:"OFFLINE");
  }else if(deviceInfoPage==1){
    display.centeredMedium(0,"BATTERY 2/3");display.line(0,15,127,15);
    if(!settings.batteryMonitor){
      display.centeredMedium(23,"MONITOR OFF");
      display.centeredMedium(43,"ENABLE IN SET");
    }else if(batteryValid){
      display.centeredMedium(17,"CELL "+String(batteryVoltage,2)+"V");
      display.centeredMedium(32,"PIN  "+String(batteryAdcVoltage,2)+"V");
      display.centeredMedium(47,"CHARGE "+String(batteryPercent)+"%");
    }else{
      display.centeredMedium(23,"NO READING");
      display.centeredMedium(43,"CHECK GPIO0");
    }
  }else{
    display.centeredMedium(0,"DEVICE 3/3");display.line(0,15,127,15);
    display.centeredMedium(21,"SOUNDDIAL");
    centeredPanel(40,"ID "+deviceId.substring(max(0,(int)deviceId.length()-8)));
  }
  display.show();
}
void renderRestart(){display.clear();display.centeredMedium(0,"RESTART?");display.line(0,15,127,15);display.centeredMedium(23,"CLICK YES");display.centeredMedium(43,"HOLD BACK");display.show();}
void renderConfirm(){display.clear();display.centeredMedium(0,"RESET?");display.line(0,15,127,15);display.centeredMedium(23,"CLICK YES");display.centeredMedium(43,"HOLD BACK");display.show();}

void render(){if(!displayPresent||displaySleeping)return;if(actionUntil>millis()){renderActionToast();return;}if(volumeCardUntil>millis()&&((screen==Screen::Media)==volumeCardMedia)&&(screen==Screen::Main||screen==Screen::Media)){renderVolumeCard(volumeCardMedia?mediaState:state,volumeCardMedia);return;}if(trackUntil>millis()&&(screen==Screen::Main||screen==Screen::Media)){renderTrack();return;}if((screen==Screen::Main||screen==Screen::Media)&&messageUntil<=millis()&&hostDashboardActive()){renderHostDashboard();return;}switch(screen){case Screen::Main:renderMain();break;case Screen::Media:renderMedia();break;case Screen::MediaSeek:renderSeek();break;case Screen::TargetPicker:renderPicker("SELECT TARGET",targets,targetCount,pickerIndex);break;case Screen::MediaTargetPicker:renderPicker("MEDIA TARGET",mediaTargets,mediaTargetCount,mediaPickerIndex);break;case Screen::SettingsSections:renderSettingsSections();break;case Screen::ProfilePicker:renderPicker("SELECT PROFILE",profiles,profileCount,pickerIndex);break;case Screen::Menu:renderMenu();break;case Screen::DeviceInfo:renderDeviceInfo();break;case Screen::ConfirmRestart:renderRestart();break;case Screen::ConfirmReset:renderConfirm();break;}}

void adjustMenu(int delta) {
  if(!menuEditing){
    const MenuSectionDef& section=kMenuSections[menuSection];
    const uint8_t count=visibleMenuCount(section);
    const int movement=min(3,abs(delta))*(delta>0?1:-1);
    const int position=menuItemPosition(section,menuIndex);
    const int nextPosition=((position+movement)%count+count)%count;
    menuIndex=menuItemAt(section,nextPosition);
    return;
  }
  switch(menuIndex){
    case 2:{const uint8_t choices[]={1,2,4,5,10};int i=0;while(i<4&&choices[i]!=settings.volumeStep)++i;i=constrain(i+(delta>0?1:-1),0,4);settings.volumeStep=choices[i];break;}
    case 4:{const uint8_t choices[]={0,3,5,10};int i=0;while(i<3&&choices[i]!=settings.trackPopupSeconds)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.trackPopupSeconds=choices[i];break;}
    case 5:{const uint16_t choices[]={10,30,60,0};int i=0;while(i<3&&choices[i]!=settings.mediaTimeoutSeconds)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.mediaTimeoutSeconds=choices[i];break;}
    case 6:{const uint16_t choices[]={800,1200,1500,2000};int i=0;while(i<3&&choices[i]!=settings.seekHoldMs)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.seekHoldMs=choices[i];break;}
    case 7:{const uint8_t choices[]={5,10,15,30};int i=0;while(i<3&&choices[i]!=settings.seekStepSeconds)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.seekStepSeconds=choices[i];break;}
    case 8:{const uint8_t choices[]={3,5,10,0};int i=0;while(i<3&&choices[i]!=settings.seekTimeoutSeconds)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.seekTimeoutSeconds=choices[i];break;}
    case 9:{const uint16_t choices[]={0,10,20,30,60,120,300};int i=0;while(i<6&&choices[i]!=settings.screenTimeoutSeconds)++i;i=constrain(i+(delta>0?1:-1),0,6);settings.screenTimeoutSeconds=choices[i];break;}
    case 12:settings.ledBrightness=constrain((int)settings.ledBrightness+(delta>0?8:-8),0,160);break;
    case 13:{const LightMode choices[]={LightMode::Volume,LightMode::Solid,LightMode::Rainbow,LightMode::Off};int i=0;while(i<3&&choices[i]!=settings.lightMode)++i;i=(i+4+(delta>0?1:-1))%4;settings.lightMode=choices[i];break;}
    case 14:settings.rainbowSpeed=(settings.rainbowSpeed+3+(delta>0?1:-1))%3;break;
    case 16:{const uint8_t choices[]={24,48,64,96};int i=0;while(i<3&&choices[i]!=settings.rainbowSpread)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.rainbowSpread=choices[i];break;}
    case 19:settings.batteryDisplayMode=(settings.batteryDisplayMode+3+(delta>0?1:-1))%3;settings.batteryMonitor=true;break;
    case 20:{const float next=roundf((settings.batteryMinVoltage+(delta>0?0.01f:-0.01f))*100.0f)/100.0f;settings.batteryMinVoltage=constrain(next,2.50f,settings.batteryMaxVoltage-0.30f);readBattery();batteryReadAt=millis();break;}
    case 21:{const float next=roundf((settings.batteryMaxVoltage+(delta>0?0.01f:-0.01f))*100.0f)/100.0f;settings.batteryMaxVoltage=constrain(next,settings.batteryMinVoltage+0.30f,4.50f);readBattery();batteryReadAt=millis();break;}
    case 22:hostBatteryMode=(hostBatteryMode+3+(delta>0?1:-1))%3;break;
    case 25:{const uint16_t choices[]={10,20,30,60,120};int i=0;while(i<4&&choices[i]!=hostIdleSeconds)++i;i=constrain(i+(delta>0?1:-1),0,4);hostIdleSeconds=choices[i];break;}
    case 26:settings.linkMode=static_cast<LinkMode>((static_cast<int>(settings.linkMode)+(delta>0?1:3))%4);break;
  }
}

void saveMenuSettings(){settings.save();sendDeviceConfig();menuSavedUntil=millis()+900;}
void saveActiveMenuSettings(){
  if(menuIndex>=22&&menuIndex<=25)sendHostConfig();
  else if(menuIndex>=19&&menuIndex<=21){
    settings.save();dialBatteryConfigLocallyChanged=true;sendDialBatteryConfig();
  }else saveMenuSettings();
}

void activateMenu() {
  switch(menuIndex){
    case 0:screen=Screen::ProfilePicker;pickerIndex=state.profile;menuEditing=false;break;
    case 1:settings.automaticProfiles=!settings.automaticProfiles;saveMenuSettings();break;
    case 3:settings.targetConfirm=!settings.targetConfirm;saveMenuSettings();break;
    case 10:settings.oledInverted=!settings.oledInverted;display.invert(settings.oledInverted);saveMenuSettings();break;
    case 15:settings.rainbowReverse=!settings.rainbowReverse;saveMenuSettings();break;
    case 17:settings.encoderReversed=!settings.encoderReversed;saveMenuSettings();break;
    case 18:settings.acceleration=!settings.acceleration;saveMenuSettings();break;
    case 23:hostIdleDashboard=!hostIdleDashboard;sendHostConfig();break;
    case 24:hostClockEnabled=!hostClockEnabled;sendHostConfig();break;
    case 2:case 4:case 5:case 6:case 7:case 8:case 9:case 12:case 13:case 14:case 16:case 19:case 20:case 21:case 22:case 25:case 26:
      menuEditing=!menuEditing;if(!menuEditing)saveActiveMenuSettings();break;
    case 11:screen=Screen::Main;display.sleep(true);displaySleeping=true;break;
    case 27:network.startPortal();screen=Screen::Main;break;
    case 28:network.reconnect();screen=Screen::Main;break;
    case 29:deviceInfoPage=0;screen=Screen::DeviceInfo;break;
    case 30:screen=Screen::ConfirmRestart;break;
    case 31:screen=Screen::ConfirmReset;break;
  }
}

void handleInput(InputEvent event) {
  if(event.type==InputEventType::None)return;
  hostDashboardArmed=true;
  trackUntil=0;
  if(displaySleeping){wakeDisplay();return;}wakeDisplay();
  if(screen==Screen::Main){
    if(event.type==InputEventType::Rotate){previewRotation(state,false,event.delta);sendEvent("ROTATE\t"+String(event.delta)+"\t"+String(settings.volumeStep));volumeCardMedia=false;volumeCardUntil=millis()+950;}
    else if(event.type==InputEventType::HoldRotate&&targetCount){pickerIndex=(pickerIndex+targetCount+(event.delta>0?1:-1))%targetCount;screen=Screen::TargetPicker;}
    else if(event.type==InputEventType::Click){sendEvent("CLICK");showAction(state.muted?"UNMUTED":"MUTED");}
    else if(event.type==InputEventType::DoubleClick){screen=Screen::Media;lastInteraction=millis();}
    else if(event.type==InputEventType::TripleClick)sendEvent("TRIPLE");
    else if(event.type==InputEventType::LongPress){screen=Screen::SettingsSections;menuSection=0;menuEditing=false;}
  } else if(screen==Screen::Media){
    if(event.type==InputEventType::Rotate){previewRotation(mediaState,true,event.delta);sendEvent("MEDIA_ROTATE\t"+String(event.delta)+"\t"+String(settings.volumeStep));volumeCardMedia=true;volumeCardUntil=millis()+950;}
    else if(event.type==InputEventType::HoldRotate&&mediaTargetCount){mediaPickerIndex=(mediaPickerIndex+mediaTargetCount+(event.delta>0?1:-1))%mediaTargetCount;screen=Screen::MediaTargetPicker;}
    else if(event.type==InputEventType::Click){sendEvent("MEDIA_PLAY");showAction("PLAY / PAUSE");}
    else if(event.type==InputEventType::DoubleClick){sendEvent("MEDIA_NEXT");showAction("NEXT TRACK");}
    else if(event.type==InputEventType::TripleClick){sendEvent("MEDIA_PREV");showAction("PREVIOUS");}
    else if(event.type==InputEventType::QuadrupleClick){sendEvent("MEDIA_MUTE");showAction(mediaState.muted?"UNMUTED":"MUTED");}
    else if(event.type==InputEventType::MediumPress){volumeCardUntil=0;seekLastUsedAt=millis();seekRefreshAt=millis();screen=Screen::MediaSeek;sendEvent("MEDIA_TIMELINE_REQUEST");}
    else if(event.type==InputEventType::LongPress)screen=Screen::Main;
  } else if(screen==Screen::MediaSeek){
    if(event.type==InputEventType::Rotate){seekLastUsedAt=millis();sendEvent("MEDIA_SEEK\t"+String(event.delta*settings.seekStepSeconds));}
    else if(event.type==InputEventType::MediumPress){screen=Screen::Media;volumeCardUntil=0;}
    else if(event.type==InputEventType::Click){sendEvent("MEDIA_PLAY");showAction("PLAY / PAUSE");}
    else if(event.type==InputEventType::DoubleClick){sendEvent("MEDIA_NEXT");showAction("NEXT TRACK");}
    else if(event.type==InputEventType::TripleClick){sendEvent("MEDIA_PREV");showAction("PREVIOUS");}
    else if(event.type==InputEventType::QuadrupleClick){sendEvent("MEDIA_MUTE");showAction(mediaState.muted?"UNMUTED":"MUTED");}
    else if(event.type==InputEventType::LongPress){screen=Screen::Main;volumeCardUntil=0;}
  } else if(screen==Screen::TargetPicker){
    if(event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)pickerIndex=(pickerIndex+targetCount+(event.delta>0?1:-1))%targetCount;
    else if(event.type==InputEventType::Click){sendEvent("TARGET\t"+String(pickerIndex));screen=Screen::Main;}
    else if(event.type==InputEventType::HoldRelease&&!settings.targetConfirm){sendEvent("TARGET\t"+String(pickerIndex));screen=Screen::Main;}
    else if(event.type==InputEventType::LongPress){screen=Screen::Main;}
  } else if(screen==Screen::MediaTargetPicker){
    if((event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)&&mediaTargetCount)mediaPickerIndex=(mediaPickerIndex+mediaTargetCount+(event.delta>0?1:-1))%mediaTargetCount;
    else if(event.type==InputEventType::DoubleClick){linkedMask^=(1U<<mediaPickerIndex);}
    else if(event.type==InputEventType::Click){uint8_t count=0;for(uint8_t bit=0;bit<Hardware::MAX_MEDIA_TARGETS;++bit)if(linkedMask&(1U<<bit))++count;if(count>=2)sendEvent("MEDIA_LINK_TARGETS\t"+String(linkedMask));else{linkedMask=0;sendEvent("MEDIA_SELECT\t"+String(mediaPickerIndex));}screen=Screen::Media;}
    else if(event.type==InputEventType::HoldRelease&&!settings.targetConfirm&&linkedMask==0){sendEvent("MEDIA_SELECT\t"+String(mediaPickerIndex));screen=Screen::Media;}
    else if(event.type==InputEventType::LongPress){if(linkedMask){linkedMask=0;sendEvent("MEDIA_LINK_TARGETS\t0");}screen=Screen::Media;}
  } else if(screen==Screen::ProfilePicker){
    if((event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)&&profileCount)pickerIndex=(pickerIndex+profileCount+(event.delta>0?1:-1))%profileCount;
    else if(event.type==InputEventType::Click){linkedMask=0;sendEvent("PROFILE\t"+String(pickerIndex));screen=Screen::Main;}
    else if(event.type==InputEventType::MediumPress)screen=Screen::Menu;
    else if(event.type==InputEventType::LongPress){settings.save();screen=Screen::Main;}
  } else if(screen==Screen::SettingsSections){
    if(event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)menuSection=(menuSection+kMenuSectionCount+(event.delta>0?1:-1))%kMenuSectionCount;
    else if(event.type==InputEventType::Click){menuIndex=kMenuSections[menuSection].first;menuEditing=false;screen=Screen::Menu;}
    else if(event.type==InputEventType::MediumPress||event.type==InputEventType::LongPress){settings.save();screen=Screen::Main;}
  } else if(screen==Screen::Menu){
    if(event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)adjustMenu(event.delta);
    else if(event.type==InputEventType::Click)activateMenu();
    else if(event.type==InputEventType::MediumPress){if(menuEditing)saveActiveMenuSettings();menuEditing=false;screen=Screen::SettingsSections;}
    else if(event.type==InputEventType::LongPress){if(menuEditing)saveActiveMenuSettings();else settings.save();menuEditing=false;screen=Screen::Main;}
  } else if(screen==Screen::DeviceInfo){
    if(event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)deviceInfoPage=(deviceInfoPage+3+(event.delta>0?1:-1))%3;
    else if(event.type==InputEventType::Click||event.type==InputEventType::MediumPress)screen=Screen::Menu;
    else if(event.type==InputEventType::LongPress){settings.save();screen=Screen::Main;}
  } else if(screen==Screen::ConfirmRestart){
    if(event.type==InputEventType::Click){settings.save();delay(200);ESP.restart();}
    else if(event.type==InputEventType::MediumPress)screen=Screen::Menu;
    else if(event.type==InputEventType::LongPress)screen=Screen::Main;
  } else if(screen==Screen::ConfirmReset){
    if(event.type==InputEventType::Click){settings.factoryReset();delay(300);ESP.restart();}
    else if(event.type==InputEventType::MediumPress)screen=Screen::Menu;
    else if(event.type==InputEventType::LongPress)screen=Screen::Main;
  }
}

bool settingsScreenActive(){
  return screen==Screen::SettingsSections||screen==Screen::Menu||
      screen==Screen::ProfilePicker||screen==Screen::DeviceInfo||
      screen==Screen::ConfirmRestart||screen==Screen::ConfirmReset;
}

void setup() {
  Serial.begin(Hardware::SERIAL_BAUD);settings.load();
  pinMode(Hardware::PIN_BATTERY_ADC,INPUT);analogReadResolution(12);analogSetPinAttenuation(Hardware::PIN_BATTERY_ADC,ADC_11db);
  // Migrate the earlier experimental 1/24/72 values back to a safe level.
  settings.oledBrightness=255;
  uint64_t chip=ESP.getEfuseMac();char id[13];snprintf(id,sizeof(id),"%08lX",(uint32_t)chip);deviceId=id;
  encoder.begin();lights.begin();displayPresent=display.begin();
  if(displayPresent){
    display.setContrast(settings.oledBrightness);display.invert(settings.oledInverted);display.clear();
    display.centeredMedium(5,"SOUNDDIAL");display.centeredMedium(27,"STARTING");
    for(uint8_t progress=25;progress<=100;progress+=25){display.progress(15,52,98,7,progress);display.show();delay(55);}
  }
  network.begin(&settings,deviceId);lastInteraction=millis();delay(250);
  readBattery();batteryReadAt=millis();
}

void loop() {
  const bool allowWifiPc=settings.linkMode==LinkMode::Wifi||(settings.linkMode==LinkMode::Auto&&!usbPreferred());
  const bool inSettings=settingsScreenActive();
  const uint16_t holdThreshold=inSettings?600:settings.seekHoldMs;
  network.update(allowWifiPc);readUsb();readNetwork();handleInput(encoder.update(settings.encoderReversed,settings.acceleration,holdThreshold));
  if(settingsScreenActive()&&millis()-lastInteraction>=kSettingsIdleMs){
    if(screen==Screen::Menu&&menuEditing)saveActiveMenuSettings();else settings.save();
    menuEditing=false;screen=Screen::Main;
    message="SETTINGS CLOSED";messageUntil=millis()+1200;
  }
  if(millis()-lastStateAt>5000){state.connected=false;mediaState.connected=false;}
  if(millis()-lastHelloAt>2000){lastHelloAt=millis();Serial.println("HELLO\tSOUNDDIAL\t"+deviceId);if(network.connected())network.sendLine("GET_STATE");}
  if(displayPresent&&settings.screenTimeoutSeconds&&(screen==Screen::Main||screen==Screen::Media)&&!displaySleeping&&millis()-lastInteraction>settings.screenTimeoutSeconds*1000UL){display.sleep(true);displaySleeping=true;}
  if(settings.mediaTimeoutSeconds&&screen==Screen::Media&&!linkedMediaActive()&&millis()-lastInteraction>settings.mediaTimeoutSeconds*1000UL)screen=Screen::Main;
  if(screen==Screen::MediaSeek&&settings.seekTimeoutSeconds&&millis()-seekLastUsedAt>settings.seekTimeoutSeconds*1000UL){screen=Screen::Media;volumeCardUntil=0;}
  if(screen==Screen::MediaSeek&&millis()-seekRefreshAt>=1000){seekRefreshAt=millis();sendEvent("MEDIA_TIMELINE_REQUEST");}
  if(millis()-batteryReadAt>=5000){batteryReadAt=millis();readBattery();}
  static uint32_t displayAt=0,lightsAt=0;
  if(millis()-displayAt>=33){displayAt=millis();render();}
  if(millis()-lightsAt>=25){lightsAt=millis();const bool mediaVisual=screen==Screen::Media||screen==Screen::MediaSeek||screen==Screen::MediaTargetPicker;PcState& visual=mediaVisual?mediaState:state;if(visual.connected){const bool solid=settings.lightMode==LightMode::Solid;lights.render(visibleVolume(visual,mediaVisual),visual.muted,true,solid?settings.solidRed:visual.red,solid?settings.solidGreen:visual.green,solid?settings.solidBlue:visual.blue,settings.ledBrightness,static_cast<uint8_t>(settings.lightMode),settings.rainbowSpeed,settings.rainbowReverse,settings.rainbowSpread);}else{const uint8_t networkState=network.ledState();lights.renderNetwork(networkState==0?4:networkState,settings.ledBrightness,static_cast<uint8_t>(settings.lightMode));}}
  delay(1);
}

#endif
