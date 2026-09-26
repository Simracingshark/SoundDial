#pragma once

#include <Arduino.h>
#include <DNSServer.h>
#include <WebServer.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include "DeviceSettings.h"
#include "HardwareConfig.h"

class NetworkLink {
 public:
  void begin(DeviceSettings* settings, const String& deviceId);
  void update(bool allowPcConnection = true);
  bool connected() { return client_.connected(); }
  bool wifiConnected() const { return WiFi.status() == WL_CONNECTED; }
  bool portalActive() const { return portalActive_; }
  String addressText() const;
  String statusTitle();
  String statusDetail();
  String accessPointName() const;
  uint8_t ledState();
  bool readLine(String& line);
  bool sendLine(const String& line);
  void startPortal();
  void stopPortal();
  void reconnect();

 private:
  DeviceSettings* settings_ = nullptr;
  String deviceId_;
  WiFiUDP udp_;
  WiFiClient client_;
  DNSServer dns_;
  WebServer web_{80};
  String input_, discoveredHost_;
  uint16_t discoveredPort_ = Hardware::TCP_PORT;
  uint32_t wifiAttemptAt_=0, discoveryAt_=0, tcpAttemptAt_=0;
  bool portalActive_=false, webConfigured_=false, webStarted_=false, scanRunning_=false, wifiError_=false;
  uint32_t restartAt_=0;
  void connectWifi();
  void discover();
  void connectTcp();
  void configurePortal();
  void beginScan();
  String page();
};
