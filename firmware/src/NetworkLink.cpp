#include "NetworkLink.h"
#include <Update.h>

void NetworkLink::begin(DeviceSettings* settings,const String& deviceId) {
  settings_=settings; deviceId_=deviceId; WiFi.persistent(false); WiFi.setSleep(false);
  // Arduino-ESP32 creates the lwIP TCP/IP mailbox when WiFi mode is enabled.
  // Opening UDP before this point asserts inside lwIP and reboots the C3.
  WiFi.mode(WIFI_STA);
  WiFi.setHostname("SoundDial");
  udp_.begin(Hardware::DISCOVERY_PORT+1);
  configurePortal();
  web_.begin();
  webStarted_=true;
  if(settings_->wifiSsid.length()) connectWifi(); else startPortal();
}

void NetworkLink::connectWifi() {
  if(portalActive_) stopPortal();
  WiFi.mode(WIFI_STA); WiFi.begin(settings_->wifiSsid.c_str(),settings_->wifiPassword.c_str()); wifiAttemptAt_=millis();
}

void NetworkLink::reconnect() { client_.stop(); discoveredHost_=""; wifiError_=false; WiFi.disconnect(); connectWifi(); }

void NetworkLink::update(bool allowPcConnection) {
  web_.handleClient();
  if(restartAt_&&static_cast<int32_t>(millis()-restartAt_)>=0){ESP.restart();return;}
  if(portalActive_){
    const int scanResult=WiFi.scanComplete();
    if(scanRunning_&&scanResult!=WIFI_SCAN_RUNNING)scanRunning_=false;
    dns_.processNextRequest();return;
  }
  if(WiFi.status()!=WL_CONNECTED) {
    if(settings_->wifiSsid.isEmpty()){startPortal();return;}
    if(millis()-wifiAttemptAt_>15000){wifiError_=true;WiFi.disconnect();connectWifi();}
    return;
  }
  wifiError_=false;
  if(!client_.connected()&&!allowPcConnection)return;
  if(!client_.connected()) {
    client_.stop();
    if(millis()-discoveryAt_>2500) discover();
    int size=udp_.parsePacket();
    if(size>0) {
      char reply[96]{}; int read=udp_.read(reply,sizeof(reply)-1); if(read>0){reply[read]=0;String value(reply);value.trim();
        if(value.startsWith("SOUNDDIAL_HOST\t")){int last=value.lastIndexOf('\t');discoveredPort_=value.substring(last+1).toInt();discoveredHost_=udp_.remoteIP().toString();}}
    }
    if(millis()-tcpAttemptAt_>1500) connectTcp();
  }
}

void NetworkLink::discover() {
  discoveryAt_=millis(); String request="SOUNDDIAL_DISCOVER\t"+deviceId_+"\t"+settings_->pairingToken;
  udp_.beginPacket(IPAddress(255,255,255,255),Hardware::DISCOVERY_PORT);udp_.print(request);udp_.endPacket();
}

void NetworkLink::connectTcp() {
  tcpAttemptAt_=millis(); String host=settings_->pcAddress.length()?settings_->pcAddress:discoveredHost_; if(host.isEmpty())return;
  const uint16_t port=settings_->pcAddress.length()?Hardware::TCP_PORT:discoveredPort_;
  if(client_.connect(host.c_str(),port,500)){client_.setNoDelay(true);sendLine("HELLO\tSOUNDDIAL\t"+deviceId_+"\t"+settings_->pairingToken);sendLine("GET_STATE");}
}

bool NetworkLink::readLine(String& line) {
  while(client_.connected()&&client_.available()) {char ch=client_.read();if(ch=='\n'){line=input_;input_="";line.trim();return true;}if(ch!='\r'&&input_.length()<1024)input_+=ch;}
  return false;
}
bool NetworkLink::sendLine(const String& line){if(!client_.connected())return false;client_.print(line);client_.print('\n');return true;}
String NetworkLink::addressText() const {if(portalActive_)return "192.168.4.1";if(WiFi.status()==WL_CONNECTED)return WiFi.localIP().toString();return "NO WIFI";}

String NetworkLink::accessPointName() const {
  return "SoundDial Setup";
}

String NetworkLink::statusTitle() {
  if(portalActive_)return scanRunning_?"WIFI SCAN":"WIFI SETUP";
  if(wifiError_)return "WIFI ERROR";
  if(WiFi.status()!=WL_CONNECTED)return "WIFI LINK";
  if(!client_.connected())return "FIND PC";
  return "CONNECTED";
}

uint8_t NetworkLink::ledState() {
  if(portalActive_)return scanRunning_?1:2;
  if(wifiError_)return 5;
  if(WiFi.status()!=WL_CONNECTED)return 3;
  if(!client_.connected())return 4;
  return 0;
}

String NetworkLink::statusDetail() {
  if(portalActive_)return scanRunning_?accessPointName():"192.168.4.1";
  if(WiFi.status()!=WL_CONNECTED)return settings_&&settings_->wifiSsid.length()?settings_->wifiSsid:"NO NETWORK";
  return WiFi.localIP().toString();
}

static String htmlEscape(String value) {
  value.replace("&","&amp;");value.replace("\"","&quot;");value.replace("'","&#39;");
  value.replace("<","&lt;");value.replace(">","&gt;");return value;
}

void NetworkLink::beginScan() {
  WiFi.scanDelete();
  WiFi.scanNetworks(true,true);
  scanRunning_=true;
}

void NetworkLink::configurePortal() {
  if(webConfigured_)return;webConfigured_=true;
  web_.on("/",HTTP_GET,[this](){web_.send(200,"text/html",page());});
  web_.on("/update",HTTP_GET,[this](){
    web_.send(200,"text/html",F("<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width'><style>body{font:18px sans-serif;max-width:520px;margin:35px auto;background:#111;color:#eee;padding:20px}input,button{box-sizing:border-box;width:100%;padding:14px;margin:10px 0;background:#222;color:white;border:1px solid #555;border-radius:8px}button{background:#35a7ff;color:#111;font-weight:bold}</style><h1>SoundDial firmware update</h1><p>Select the application-only <b>firmware.bin</b> file. Do not use the merged image here.</p><form method=post action=/update enctype=multipart/form-data><input type=file name=firmware accept=.bin required><button>Upload and restart</button></form>"));
  });
  web_.on("/update",HTTP_POST,[this](){
    const bool success=!Update.hasError();
    web_.sendHeader("Connection","close");
    web_.send(success?200:500,"text/html",success
      ? F("<meta charset=utf-8><h2>Update complete. SoundDial is restarting...</h2>")
      : F("<meta charset=utf-8><h2>Update failed. The previous firmware is still active.</h2>"));
    if(success)restartAt_=millis()+900;
  },[this](){
    HTTPUpload& upload=web_.upload();
    if(upload.status==UPLOAD_FILE_START){
      if(!Update.begin(UPDATE_SIZE_UNKNOWN))Update.printError(Serial);
    }else if(upload.status==UPLOAD_FILE_WRITE){
      if(Update.write(upload.buf,upload.currentSize)!=upload.currentSize)Update.printError(Serial);
    }else if(upload.status==UPLOAD_FILE_END){
      if(!Update.end(true))Update.printError(Serial);
    }else if(upload.status==UPLOAD_FILE_ABORTED){
      Update.abort();
    }
  });
  web_.on("/rescan",HTTP_GET,[this](){beginScan();web_.sendHeader("Location","/",true);web_.send(302,"text/plain","");});
  web_.on("/save",HTTP_POST,[this](){
    String manual=web_.arg("manual");manual.trim();settings_->wifiSsid=manual.length()?manual:web_.arg("ssid");settings_->wifiPassword=web_.arg("pass");settings_->pairingToken=web_.arg("token");settings_->pcAddress=web_.arg("host");settings_->save();
    web_.send(200,"text/html","<meta charset=utf-8><h2>Saved. SoundDial is restarting...</h2>");delay(500);ESP.restart();});
  web_.onNotFound([this](){web_.sendHeader("Location","/",true);web_.send(302,"text/plain","");});
}

void NetworkLink::startPortal() {
  if(portalActive_)return;client_.stop();WiFi.disconnect();WiFi.mode(WIFI_AP_STA);
  String ap=accessPointName();WiFi.softAP(ap.c_str(),"sounddial");
  dns_.start(53,"*",WiFi.softAPIP());configurePortal();if(!webStarted_){web_.begin();webStarted_=true;}portalActive_=true;beginScan();
}
void NetworkLink::stopPortal(){if(!portalActive_)return;dns_.stop();WiFi.scanDelete();scanRunning_=false;WiFi.softAPdisconnect(true);portalActive_=false;}

String NetworkLink::page() {
  String result=F("<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width'>");
  if(scanRunning_)result+=F("<meta http-equiv=refresh content='2'>");
  result+=F("<style>body{font:18px sans-serif;max-width:520px;margin:35px auto;background:#111;color:#eee;padding:20px}input,select{box-sizing:border-box;width:100%;padding:12px;margin:6px 0 18px;background:#222;color:white;border:1px solid #555;border-radius:8px}button,a{display:inline-block;padding:14px 24px;background:#35a7ff;color:#111;text-decoration:none;border:0;border-radius:9px;font-weight:bold;margin-right:8px}</style><h1>SoundDial setup</h1>");
  if(scanRunning_)result+=F("<p>Scanning for Wi-Fi networks...</p>");
  result+=F("<form method=post action=/save><label>Wi-Fi network</label><select name=ssid>");
  const int found=WiFi.scanComplete();
  if(found>0){
    for(int i=0;i<found;++i){String ssid=WiFi.SSID(i);if(ssid.isEmpty())continue;bool duplicate=false;for(int j=0;j<i;++j)if(WiFi.SSID(j)==ssid){duplicate=true;break;}if(duplicate)continue;
      result+=F("<option value='");result+=htmlEscape(ssid);result+="'";if(ssid==settings_->wifiSsid)result+=F(" selected");result+=F(">");result+=htmlEscape(ssid);result+=F(" (");result+=String(WiFi.RSSI(i));result+=F(" dBm)</option>");}
  }else result+=F("<option value=''>No networks found yet</option>");
  result+=F("</select><a href=/rescan>Scan again</a><p><label>Hidden network (optional)</label><input name=manual placeholder='Type only for a hidden Wi-Fi'></p><label>Wi-Fi password</label><input name=pass type=password><label>Pairing token (same as config.toml)</label><input name=token value='");result+=htmlEscape(settings_->pairingToken);
  result+=F("'><label>PC IP (optional; empty = automatic discovery)</label><input name=host value='");result+=htmlEscape(settings_->pcAddress);result+=F("'><button>Save and restart</button></form><p><a href=/update>Firmware update</a></p>");return result;
}
