#include <Arduino.h>
#include <esp_system.h>
#include "DeviceSettings.h"
#include "Display.h"
#include "EncoderInput.h"
#include "HardwareConfig.h"
#include "LedRing.h"
#include "NetworkLink.h"

#if !defined(SOUNDDIAL_HARDWARE_TEST)

enum class Screen : uint8_t { Main, Media, TargetPicker, MediaTargetPicker, Menu, ProfilePicker, DeviceInfo, ConfirmRestart, ConfirmReset };

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
String profiles[Hardware::MAX_PROFILES],targets[Hardware::MAX_TARGETS],mediaTargets[Hardware::MAX_TARGETS];
uint8_t profileCount=0,targetCount=0,mediaTargetCount=0,menuIndex=0,pickerIndex=0,mediaPickerIndex=0;
uint8_t linkedMask=0;
bool menuEditing=false,displaySleeping=false,displayPresent=false;
uint32_t lastInteraction=0,lastStateAt=0,lastHelloAt=0,messageUntil=0,trackUntil=0,actionUntil=0,volumeCardUntil=0,menuSavedUntil=0;
uint32_t mainPreviewUntil=0,mediaPreviewUntil=0;
int mainPreviewVolume=0,mediaPreviewVolume=0;
bool volumeCardMedia=false;

constexpr uint8_t kMenuCount=23;
const char* kMenuItems[kMenuCount]={"PROFILE","AUTO PROFILE","CONNECTION","VOLUME STEP","TARGET OK","TRACK POPUP","MEDIA TIME","SCREEN TIME","INVERT OLED","DISPLAY OFF","LED BRIGHT","LIGHT MODE","RAINBOW SPD","RAINBOW DIR","RAINBOW WIDTH","REVERSE KNOB","ACCELERATION","WIFI SETUP","DEVICE INFO","WIFI RETRY","RESTART","FACTORY RESET","EXIT SETTINGS"};

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
  until=millis()+350;
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
  const String value="DEVICE_CONFIG\t"+String((int)settings.linkMode)+"\t"+String(settings.volumeStep)+"\t"+String(settings.targetConfirm?1:0)+"\t"+String(settings.trackPopupSeconds)+"\t"+String(settings.mediaTimeoutSeconds)+"\t"+String(settings.screenTimeoutSeconds)+"\t"+String(settings.ledBrightness)+"\t"+String((int)settings.lightMode)+"\t"+String(settings.oledInverted?1:0)+"\t"+String(settings.encoderReversed?1:0)+"\t"+String(settings.acceleration?1:0)+"\t"+String(settings.solidRed)+"\t"+String(settings.solidGreen)+"\t"+String(settings.solidBlue)+"\t"+String(settings.automaticProfiles?1:0)+"\t"+String(settings.rainbowSpeed)+"\t"+String(settings.rainbowReverse?1:0)+"\t"+String(settings.rainbowSpread)+"\t"+network.addressText();
  // Device settings are a recovery channel too.  Always answer USB, even if
  // the user has selected WiFi or OFFLINE, and mirror the reply to WiFi when
  // it is currently connected.
  Serial.println(value);
  network.sendLine(value);
}

void handleLine(String line) {
  line.trim();if(line.isEmpty())return;String p[20];int count=splitTabs(line,p,20);
  if(p[0]=="PING"){sendEvent("HELLO\tSOUNDDIAL\t"+deviceId);return;}
  if(p[0]=="GET_DEVICE_CONFIG"){sendDeviceConfig();return;}
  if(p[0]=="SET_DEVICE_CONFIG"&&count>=12){
    settings.linkMode=static_cast<LinkMode>(constrain(p[1].toInt(),0,3));
    const int step=p[2].toInt();settings.volumeStep=(step==1||step==2||step==4||step==5||step==10)?step:4;
    settings.targetConfirm=p[3]=="1";const int popup=p[4].toInt();settings.trackPopupSeconds=(popup==0||popup==3||popup==5||popup==10)?popup:5;
    settings.mediaTimeoutSeconds=constrain(p[5].toInt(),0,3600);settings.screenTimeoutSeconds=constrain(p[6].toInt(),0,3600);
    settings.ledBrightness=constrain(p[7].toInt(),0,160);settings.lightMode=static_cast<LightMode>(constrain(p[8].toInt(),0,3));
    settings.oledInverted=p[9]=="1";settings.encoderReversed=p[10]=="1";settings.acceleration=p[11]=="1";
    if(count>=15){settings.solidRed=constrain(p[12].toInt(),0,255);settings.solidGreen=constrain(p[13].toInt(),0,255);settings.solidBlue=constrain(p[14].toInt(),0,255);}
    if(count>=19){settings.automaticProfiles=p[15]=="1";settings.rainbowSpeed=constrain(p[16].toInt(),0,2);settings.rainbowReverse=p[17]=="1";settings.rainbowSpread=constrain(p[18].toInt(),16,96);}
    display.invert(settings.oledInverted);settings.save();message="SETTINGS OK";messageUntil=millis()+1800;sendDeviceConfig();return;
  }
  if(p[0]=="STATE"&&count>=12){
    const bool wasConnected=state.connected;
    const int oldProfile=state.profile,oldTarget=state.target;
    state.profile=p[1].toInt();state.target=p[2].toInt();state.volume=constrain(p[3].toInt(),0,100);state.muted=p[4]=="1";state.available=p[5]=="1";
    state.link=p[6];state.red=constrain(p[7].toInt(),0,255);state.green=constrain(p[8].toInt(),0,255);state.blue=constrain(p[9].toInt(),0,255);
    state.name=decodeText(p[10]);state.subtitle=decodeText(p[11]);if(state.subtitle=="~")state.subtitle="";state.stepOverride=count>=13?constrain(p[12].toInt(),0,10):0;state.connected=true;lastStateAt=millis();
    if(oldProfile!=state.profile||oldTarget!=state.target)mainPreviewUntil=0;
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
    mediaState.link=state.link;mediaState.connected=true;lastStateAt=millis();return;
  }
  if(p[0]=="TARGETS"&&count>=2){parseList(p[1],targets,Hardware::MAX_TARGETS,targetCount);return;}
  if(p[0]=="MEDIA_TARGETS"&&count>=2){parseList(p[1],mediaTargets,Hardware::MAX_TARGETS,mediaTargetCount);return;}
  if(p[0]=="PROFILES"&&count>=2){parseList(p[1],profiles,Hardware::MAX_PROFILES,profileCount);return;}
  if(p[0]=="MESSAGE"&&count>=2){message=decodeText(p[1]);messageUntil=millis()+2200;return;}
  if(p[0]=="TRACK"&&count>=3&&settings.trackPopupSeconds){trackTitle=decodeText(p[1]);trackArtist=decodeText(p[2]);if(trackTitle.length()){lastInteraction=millis();if(displaySleeping){display.sleep(false);displaySleeping=false;}trackUntil=millis()+settings.trackPopupSeconds*1000UL;}return;}
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
String timeoutValue(uint16_t seconds){return seconds==0?"ALWAYS":String(seconds)+" SEC";}
uint8_t linkedMediaCount(){uint8_t count=0;for(uint8_t bit=0;bit<Hardware::MAX_TARGETS;++bit)if(linkedMask&(1U<<bit))++count;return count;}
bool linkedMediaActive(){return linkedMediaCount()>=2;}

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
  display.textMedium(max(0,(120-display.textWidthMedium(fitted))/2),y,fitted);
}

void listPosition(uint8_t index,uint8_t count) {
  if(count<2)return;
  constexpr int16_t top=18,bottom=62,height=bottom-top+1;
  display.line(123,top,123,bottom);
  const int16_t thumbHeight=max(4,height/count);
  const int16_t travel=height-thumbHeight;
  const int16_t thumbY=top+(travel*index)/(count-1);
  display.rect(122,thumbY,3,thumbHeight,true);
}

void showAction(const String& value,uint16_t duration=850){actionMessage=value;actionUntil=millis()+duration;}

String menuValue(uint8_t index) {
  switch(index){
    case 0:return profileCount?profiles[min((int)state.profile,(int)profileCount-1)]:"DEFAULT";
    case 1:return settings.automaticProfiles?"ON":"OFF";case 2:return connectionValue();case 3:return String(settings.volumeStep)+"%";
    case 4:return settings.targetConfirm?"YES":"NO";case 5:return settings.trackPopupSeconds?String(settings.trackPopupSeconds)+" SEC":"OFF";
    case 6:return timeoutValue(settings.mediaTimeoutSeconds);case 7:return timeoutValue(settings.screenTimeoutSeconds);
    case 8:return settings.oledInverted?"YES":"NO";case 9:return "PRESS";
    case 10:return String(settings.ledBrightness);case 11:return lightValue();
    case 12:return rainbowSpeedValue();case 13:return settings.rainbowReverse?"COUNTER":"CLOCKWISE";case 14:return rainbowWidthValue();
    case 15:return settings.encoderReversed?"YES":"NO";case 16:return settings.acceleration?"YES":"NO";
    case 17:return network.portalActive()?"ACTIVE":"START";case 18:return network.addressText();case 22:return "MAIN SCREEN";
    default:return "PRESS";
  }
}

void renderActionToast() {
  display.clear();
  display.centeredMedium(7,"SOUNDDIAL");
  display.line(12,24,111,24);
  display.centeredMedium(35,uiText(actionMessage));
  display.show();
}

void renderVolumeCard(PcState& current,bool media) {
  display.clear();
  const uint8_t linked=media?linkedMediaCount():0;
  const String heading=media&&linked>=2?"LINK "+String(linked):current.name;
  display.centeredMedium(0,uiText(heading));
  display.line(0,15,123,15);
  const int shown=visibleVolume(current,media);
  const String value=current.muted?"MUTE":String(shown)+"%";
  display.centered(22,value,2);
  display.progress(4,53,118,9,shown);
  display.show();
}

void renderMain() {
  display.clear();
  if(messageUntil>millis()){
    display.centeredMedium(0,"MESSAGE");display.line(0,15,123,15);
    display.centeredMedium(27,uiText(message));display.show();return;
  }
  if(!state.connected&&settings.linkMode!=LinkMode::Offline){
    String title=settings.linkMode==LinkMode::Usb?"USB WAIT":network.statusTitle();
    if(title=="CONNECTED")title="WAIT FOR PC";
    const uint8_t dots=(millis()/350)%4;for(uint8_t i=0;i<dots&&title.length()<14;++i)title+='.';
    display.centeredMedium(0,uiText(title));
    display.centeredMedium(20,settings.linkMode==LinkMode::Usb?"START PC APP":uiText(network.statusDetail()));
    display.centeredMedium(40,network.portalActive()?"CONNECT AP":network.connected()?"PC APP CLOSED":"WAIT FOR PC");
    display.show();return;
  }
  display.textMedium(1,0,uiText(state.link));
  const int shown=visibleVolume(state,false);
  String volume=state.muted?"MUTE":String(shown)+"%";
  display.textMedium(max(0,125-display.textWidthMedium(volume)),0,volume);
  display.line(0,15,123,15);
  if(state.available&&state.subtitle.length()==0){
    display.centeredMedium(27,uiText(state.name));
  }else{
    display.centeredMedium(18,uiText(state.name));
    display.centeredMedium(35,state.available?uiText(state.subtitle):"NOT RUN");
  }
  display.progress(4,53,118,9,shown);
  display.show();
}

void renderMedia() {
  display.clear();
  const uint8_t linked=linkedMediaCount();
  display.textMedium(1,0,linked>=2?"LINK "+String(linked):"MEDIA");
  if(!mediaState.connected){
    display.line(0,15,123,15);
    display.centeredMedium(22,"PC APP CLOSED");
    display.centeredMedium(42,"WAITING");
    display.show();return;
  }
  const int shown=visibleVolume(mediaState,true);
  String volume=mediaState.muted?"MUTE":String(shown)+"%";
  display.textMedium(max(0,125-display.textWidthMedium(volume)),0,volume);
  display.line(0,15,123,15);
  display.centeredMedium(18,uiText(mediaState.name));
  display.centeredMedium(35,mediaState.available?(linkedMediaActive()?uiText(mediaState.subtitle):"CLICK PLAY"):"NOT RUN");
  display.progress(4,53,118,9,shown);
  display.show();
}

void renderTrack(){display.clear();display.centeredMedium(0,"NOW PLAY");display.line(0,15,123,15);display.centeredMedium(19,uiText(trackTitle));display.centeredMedium(39,uiText(trackArtist.length()?trackArtist:"UNKNOWN"));display.show();}

void renderPicker(const char* title,String values[],uint8_t count,uint8_t selected) {
  display.clear();
  const bool mediaPicker=String(title).startsWith("MEDIA");
  const uint8_t linkedCount=linkedMediaCount();
  String heading=String(title).startsWith("SELECT P")?"PROFILE ":mediaPicker&&linkedCount?"LINK ":mediaPicker?"MEDIA ":"TARGET ";
  if(mediaPicker&&linkedCount)heading+=String(linkedCount)+" ";
  heading+=count?String(selected+1)+"/"+String(count):"0/0";
  centeredPanel(0,heading);display.line(0,15,119,15);
  if(!count){display.centeredMedium(25,"EMPTY");display.show();return;}
  centeredPanel(18,values[selected]);
  if(mediaPicker)centeredPanel(34,linkedMask&(1U<<selected)?"MARKED":"TURN/DBL");
  else centeredPanel(34,"TURN");
  const bool profilePicker=String(title).startsWith("SELECT P");
  centeredPanel(50,mediaPicker&&linkedCount>=2?"CLICK GO":(profilePicker||settings.targetConfirm)?"CLICK OK":"RELEASE");
  listPosition(selected,count);
  display.show();
}

void renderMenu() {
  display.clear();
  char counter[11];snprintf(counter,sizeof(counter),"%s %02u/%02u",menuEditing?"EDIT":"SET",menuIndex+1,kMenuCount);
  centeredPanel(0,counter);display.line(0,15,119,15);
  centeredPanel(18,kMenuItems[menuIndex]);
  const String value=panelText(menuValue(menuIndex));
  centeredPanel(34,value);
  if(menuEditing)display.rect(0,32,120,18);
  const bool direct=menuIndex==9||menuIndex==17||menuIndex==18||menuIndex==19||menuIndex==20||menuIndex==21;
  const bool opens=menuIndex==0;
  centeredPanel(50,menuSavedUntil>millis()?"SAVED":menuEditing?"CLICK SAVE":menuIndex==18?"OPEN INFO":menuIndex==22?"CLICK EXIT":direct?"CLICK RUN":opens?"CLICK OPEN":"CLICK EDIT");
  listPosition(menuIndex,kMenuCount);
  display.show();
}

void renderDeviceInfo(){display.clear();display.centeredMedium(0,"DEVICE INFO");display.line(0,15,123,15);display.centeredMedium(17,uiText(network.addressText()));display.centeredMedium(33,uiText("ID "+deviceId.substring(max(0,(int)deviceId.length()-8))));display.centeredMedium(49,"CLICK BACK");display.show();}
void renderRestart(){display.clear();display.centeredMedium(0,"RESTART?");display.line(0,15,123,15);display.centeredMedium(23,"CLICK YES");display.centeredMedium(43,"HOLD BACK");display.show();}
void renderConfirm(){display.clear();display.centeredMedium(0,"RESET?");display.line(0,15,123,15);display.centeredMedium(23,"CLICK YES");display.centeredMedium(43,"HOLD BACK");display.show();}

void render(){if(!displayPresent||displaySleeping)return;if(actionUntil>millis()){renderActionToast();return;}if(volumeCardUntil>millis()&&((screen==Screen::Media)==volumeCardMedia)&&(screen==Screen::Main||screen==Screen::Media)){renderVolumeCard(volumeCardMedia?mediaState:state,volumeCardMedia);return;}if(trackUntil>millis()&&(screen==Screen::Main||screen==Screen::Media)){renderTrack();return;}switch(screen){case Screen::Main:renderMain();break;case Screen::Media:renderMedia();break;case Screen::TargetPicker:renderPicker("SELECT TARGET",targets,targetCount,pickerIndex);break;case Screen::MediaTargetPicker:renderPicker("MEDIA TARGET",mediaTargets,mediaTargetCount,mediaPickerIndex);break;case Screen::ProfilePicker:renderPicker("SELECT PROFILE",profiles,profileCount,pickerIndex);break;case Screen::Menu:renderMenu();break;case Screen::DeviceInfo:renderDeviceInfo();break;case Screen::ConfirmRestart:renderRestart();break;case Screen::ConfirmReset:renderConfirm();break;}}

void adjustMenu(int delta) {
  if(!menuEditing){const int movement=min(3,abs(delta))*(delta>0?1:-1);menuIndex=(menuIndex+kMenuCount+movement)%kMenuCount;return;}
  switch(menuIndex){
    case 1:settings.automaticProfiles=!settings.automaticProfiles;break;
    case 2:settings.linkMode=static_cast<LinkMode>((static_cast<int>(settings.linkMode)+(delta>0?1:3))%4);break;
    case 3:{const uint8_t choices[]={1,2,4,5,10};int i=0;while(i<4&&choices[i]!=settings.volumeStep)++i;i=constrain(i+(delta>0?1:-1),0,4);settings.volumeStep=choices[i];break;}
    case 4:settings.targetConfirm=!settings.targetConfirm;break;
    case 5:{const uint8_t choices[]={0,3,5,10};int i=0;while(i<3&&choices[i]!=settings.trackPopupSeconds)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.trackPopupSeconds=choices[i];break;}
    case 6:{const uint16_t choices[]={10,30,60,0};int i=0;while(i<3&&choices[i]!=settings.mediaTimeoutSeconds)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.mediaTimeoutSeconds=choices[i];break;}
    case 7:{const uint16_t choices[]={0,10,20,30,60,120,300};int i=0;while(i<6&&choices[i]!=settings.screenTimeoutSeconds)++i;i=constrain(i+(delta>0?1:-1),0,6);settings.screenTimeoutSeconds=choices[i];break;}
    case 8:settings.oledInverted=!settings.oledInverted;display.invert(settings.oledInverted);break;
    case 10:settings.ledBrightness=constrain((int)settings.ledBrightness+(delta>0?8:-8),0,160);break;
    case 11:{const LightMode choices[]={LightMode::Volume,LightMode::Solid,LightMode::Rainbow,LightMode::Off};int i=0;while(i<3&&choices[i]!=settings.lightMode)++i;i=(i+4+(delta>0?1:-1))%4;settings.lightMode=choices[i];break;}
    case 12:settings.rainbowSpeed=(settings.rainbowSpeed+3+(delta>0?1:-1))%3;break;
    case 13:settings.rainbowReverse=!settings.rainbowReverse;break;
    case 14:{const uint8_t choices[]={24,48,64,96};int i=0;while(i<3&&choices[i]!=settings.rainbowSpread)++i;i=constrain(i+(delta>0?1:-1),0,3);settings.rainbowSpread=choices[i];break;}
    case 15:settings.encoderReversed=!settings.encoderReversed;break;case 16:settings.acceleration=!settings.acceleration;break;
  }
}

void activateMenu() {
  switch(menuIndex){
    case 0:screen=Screen::ProfilePicker;pickerIndex=state.profile;menuEditing=false;break;
    case 1:case 2:case 3:case 4:case 5:case 6:case 7:case 8:case 10:case 11:case 12:case 13:case 14:case 15:case 16:menuEditing=!menuEditing;if(!menuEditing){settings.save();sendDeviceConfig();menuSavedUntil=millis()+900;}break;
    case 9:screen=Screen::Main;display.sleep(true);displaySleeping=true;break;
    case 17:network.startPortal();screen=Screen::Main;break;case 18:screen=Screen::DeviceInfo;break;case 19:network.reconnect();screen=Screen::Main;break;
    case 20:screen=Screen::ConfirmRestart;break;case 21:screen=Screen::ConfirmReset;break;
    case 22:settings.save();message="SETTINGS SAVED";messageUntil=millis()+1200;menuEditing=false;screen=Screen::Main;break;
  }
}

void handleInput(InputEvent event) {
  if(event.type==InputEventType::None)return;
  trackUntil=0;
  if(displaySleeping){wakeDisplay();return;}wakeDisplay();
  if(screen==Screen::Main){
    if(event.type==InputEventType::Rotate){previewRotation(state,false,event.delta);sendEvent("ROTATE\t"+String(event.delta)+"\t"+String(settings.volumeStep));volumeCardMedia=false;volumeCardUntil=millis()+950;}
    else if(event.type==InputEventType::HoldRotate&&targetCount){pickerIndex=(pickerIndex+targetCount+(event.delta>0?1:-1))%targetCount;screen=Screen::TargetPicker;}
    else if(event.type==InputEventType::Click){sendEvent("CLICK");showAction(state.muted?"UNMUTED":"MUTED");}
    else if(event.type==InputEventType::DoubleClick){screen=Screen::Media;lastInteraction=millis();}
    else if(event.type==InputEventType::TripleClick)sendEvent("TRIPLE");
    else if(event.type==InputEventType::LongPress){screen=Screen::Menu;menuIndex=0;menuEditing=false;}
  } else if(screen==Screen::Media){
    if(event.type==InputEventType::Rotate){previewRotation(mediaState,true,event.delta);sendEvent("MEDIA_ROTATE\t"+String(event.delta)+"\t"+String(settings.volumeStep));volumeCardMedia=true;volumeCardUntil=millis()+950;}
    else if(event.type==InputEventType::HoldRotate&&mediaTargetCount){mediaPickerIndex=(mediaPickerIndex+mediaTargetCount+(event.delta>0?1:-1))%mediaTargetCount;screen=Screen::MediaTargetPicker;}
    else if(event.type==InputEventType::Click){sendEvent("MEDIA_PLAY");showAction("PLAY / PAUSE");}
    else if(event.type==InputEventType::DoubleClick){sendEvent("MEDIA_MUTE");showAction(mediaState.muted?"UNMUTED":"MUTED");}
    else if(event.type==InputEventType::TripleClick){sendEvent("MEDIA_NEXT");showAction("NEXT TRACK");}
    else if(event.type==InputEventType::QuadrupleClick){sendEvent("MEDIA_PREV");showAction("PREVIOUS");}
    else if(event.type==InputEventType::LongPress)screen=Screen::Main;
  } else if(screen==Screen::TargetPicker){
    if(event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)pickerIndex=(pickerIndex+targetCount+(event.delta>0?1:-1))%targetCount;
    else if(event.type==InputEventType::Click){sendEvent("TARGET\t"+String(pickerIndex));screen=Screen::Main;}
    else if(event.type==InputEventType::HoldRelease&&!settings.targetConfirm){sendEvent("TARGET\t"+String(pickerIndex));screen=Screen::Main;}
    else if(event.type==InputEventType::LongPress){screen=Screen::Main;}
  } else if(screen==Screen::MediaTargetPicker){
    if((event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)&&mediaTargetCount)mediaPickerIndex=(mediaPickerIndex+mediaTargetCount+(event.delta>0?1:-1))%mediaTargetCount;
    else if(event.type==InputEventType::DoubleClick){linkedMask^=(1U<<mediaPickerIndex);}
    else if(event.type==InputEventType::Click){uint8_t count=0;for(uint8_t bit=0;bit<Hardware::MAX_TARGETS;++bit)if(linkedMask&(1U<<bit))++count;if(count>=2)sendEvent("MEDIA_LINK_TARGETS\t"+String(linkedMask));else{linkedMask=0;sendEvent("MEDIA_SELECT\t"+String(mediaPickerIndex));}screen=Screen::Media;}
    else if(event.type==InputEventType::HoldRelease&&!settings.targetConfirm&&linkedMask==0){sendEvent("MEDIA_SELECT\t"+String(mediaPickerIndex));screen=Screen::Media;}
    else if(event.type==InputEventType::LongPress){if(linkedMask){linkedMask=0;sendEvent("MEDIA_LINK_TARGETS\t0");}screen=Screen::Media;}
  } else if(screen==Screen::ProfilePicker){
    if((event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)&&profileCount)pickerIndex=(pickerIndex+profileCount+(event.delta>0?1:-1))%profileCount;
    else if(event.type==InputEventType::Click){linkedMask=0;sendEvent("PROFILE\t"+String(pickerIndex));screen=Screen::Main;}
    else if(event.type==InputEventType::LongPress)screen=Screen::Menu;
  } else if(screen==Screen::Menu){
    if(event.type==InputEventType::Rotate||event.type==InputEventType::HoldRotate)adjustMenu(event.delta);
    else if(event.type==InputEventType::Click)activateMenu();
    else if(event.type==InputEventType::LongPress){settings.save();message="SETTINGS SAVED";messageUntil=millis()+1200;screen=Screen::Main;menuEditing=false;}
  } else if(screen==Screen::DeviceInfo){
    if(event.type==InputEventType::Click||event.type==InputEventType::LongPress)screen=Screen::Menu;
  } else if(screen==Screen::ConfirmRestart){
    if(event.type==InputEventType::Click){settings.save();delay(200);ESP.restart();}
    else if(event.type==InputEventType::LongPress)screen=Screen::Menu;
  } else if(screen==Screen::ConfirmReset){
    if(event.type==InputEventType::Click){settings.factoryReset();delay(300);ESP.restart();}
    else if(event.type==InputEventType::LongPress)screen=Screen::Menu;
  }
}

void setup() {
  Serial.begin(Hardware::SERIAL_BAUD);settings.load();
  // Migrate the earlier experimental 1/24/72 values back to a safe level.
  settings.oledBrightness=255;
  uint64_t chip=ESP.getEfuseMac();char id[13];snprintf(id,sizeof(id),"%08lX",(uint32_t)chip);deviceId=id;
  encoder.begin();lights.begin();displayPresent=display.begin();
  if(displayPresent){
    display.setContrast(settings.oledBrightness);display.invert(settings.oledInverted);display.clear();
    display.centeredMedium(5,"SOUNDDIAL");display.centeredMedium(27,"STARTING");
    for(uint8_t progress=25;progress<=100;progress+=25){display.progress(14,52,98,7,progress);display.show();delay(55);}
  }
  network.begin(&settings,deviceId);lastInteraction=millis();delay(250);
}

void loop() {
  const bool allowWifiPc=settings.linkMode==LinkMode::Wifi||(settings.linkMode==LinkMode::Auto&&!usbPreferred());
  network.update(allowWifiPc);readUsb();readNetwork();handleInput(encoder.update(settings.encoderReversed,settings.acceleration));
  if(millis()-lastStateAt>5000){state.connected=false;mediaState.connected=false;}
  if(millis()-lastHelloAt>2000){lastHelloAt=millis();Serial.println("HELLO\tSOUNDDIAL\t"+deviceId);if(network.connected())network.sendLine("GET_STATE");}
  if(displayPresent&&settings.screenTimeoutSeconds&&(screen==Screen::Main||screen==Screen::Media)&&!displaySleeping&&millis()-lastInteraction>settings.screenTimeoutSeconds*1000UL){display.sleep(true);displaySleeping=true;}
  if(settings.mediaTimeoutSeconds&&screen==Screen::Media&&!linkedMediaActive()&&millis()-lastInteraction>settings.mediaTimeoutSeconds*1000UL)screen=Screen::Main;
  static uint32_t displayAt=0,lightsAt=0;
  if(millis()-displayAt>=33){displayAt=millis();render();}
  if(millis()-lightsAt>=25){lightsAt=millis();const bool mediaVisual=screen==Screen::Media||screen==Screen::MediaTargetPicker;PcState& visual=mediaVisual?mediaState:state;if(visual.connected){const bool solid=settings.lightMode==LightMode::Solid;lights.render(visibleVolume(visual,mediaVisual),visual.muted,true,solid?settings.solidRed:visual.red,solid?settings.solidGreen:visual.green,solid?settings.solidBlue:visual.blue,settings.ledBrightness,static_cast<uint8_t>(settings.lightMode),settings.rainbowSpeed,settings.rainbowReverse,settings.rainbowSpread);}else{const uint8_t networkState=network.ledState();lights.renderNetwork(networkState==0?4:networkState,settings.ledBrightness,static_cast<uint8_t>(settings.lightMode));}}
  delay(1);
}

#endif
