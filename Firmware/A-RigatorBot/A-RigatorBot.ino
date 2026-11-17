#include <Wire.h>
#include <ArduinoJson.h>
#include <time.h>

// Firmware version reported by the local status API.
#define ARIGATOR_FIRMWARE_VERSION "0.1.0"

#include <HardwareSerial.h>
#include <DFRobotDFPlayerMini.h>
#include <WiFi.h>
#include <WebServer.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLESecurity.h>
#include <ESPmDNS.h>
#include "RobotWifiConfig.h"
#include "RobotMqttConfig.h"
#include "RobotMqttManager.h"

// ============================================================================
// 1. WI-FI STATUS API
// ============================================================================
// AP credentials are device-specific. The password is generated once and
// persisted in NVS; never print it to the serial console or commit it to Git.
static const char* WIFI_SSID = "A-Rigator";
String apPassword;
RobotApConfigStore apConfigStore;
bool apConfigStoreAvailable = false;
RobotMqttConfigStore mqttConfigStore;
RobotMqttConfig mqttConfig;
RobotMqttManager mqttManager;
bool mqttConfigStoreAvailable = false;
bool mqttConfigured = false;
static const IPAddress AP_IP(192, 168, 4, 1);
static const IPAddress AP_GATEWAY(192, 168, 4, 1);
static const IPAddress AP_SUBNET(255, 255, 255, 0);
static const unsigned long MANUAL_CONTROL_TIMEOUT_MS = 750;
static const unsigned long BOOT_POWER_SETTLE_MS = 1500;
static const uint8_t WIFI_AP_START_RETRIES = 3;
static const unsigned long WIFI_AP_RETRY_DELAY_MS = 400;

WebServer server(80);
RobotWifiCredentialStore wifiCredentialStore;
RobotWifiCredentials savedWifiCredentials;
bool savedWifiCredentialsAvailable = false;
bool stationWasConnected = false;
bool wifiForgetDisconnectPending = false;
unsigned long wifiForgetDisconnectAt = 0;
bool mdnsStarted = false;

// BLE credential writes require an encrypted, bonded link. Just Works does
// not provide MITM protection; production pairing needs physical presence or
// a device-identity mechanism.
static const char* BLE_DEVICE_NAME = "A-Rigator";
static const char* BLE_PROVISIONING_SERVICE_UUID = "7a1c0001-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_DISCOVERY_CHARACTERISTIC_UUID = "7a1c0002-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_SCAN_CHARACTERISTIC_UUID = "7a1c0003-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_CREDENTIAL_CHARACTERISTIC_UUID = "7a1c0004-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_STATUS_CHARACTERISTIC_UUID = "7a1c0005-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_AP_CONFIG_CHARACTERISTIC_UUID = "7a1c0006-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_HARDWARE_ID_CHARACTERISTIC_UUID = "7a1c0008-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_CONTROL_SERVICE_UUID = "7a1c0009-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_CONTROL_COMMAND_UUID = "7a1c000a-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_CONTROL_ACK_UUID = "7a1c000b-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_CONTROL_TELEMETRY_UUID = "7a1c000c-8f2d-4b6a-9c31-4a5249474154";
BLEServer* bleServer = nullptr;
BLEService* bleControlService = nullptr;
BLECharacteristic* bleControlCommandCharacteristic = nullptr;
BLECharacteristic* bleControlAckCharacteristic = nullptr;
BLECharacteristic* bleControlTelemetryCharacteristic = nullptr;
portMUX_TYPE bleControlMux = portMUX_INITIALIZER_UNLOCKED;
String pendingBleControlCommand;
volatile bool bleControlCommandPending = false;
unsigned long lastBleTelemetryAt = 0;
BLEService* bleProvisioningService = nullptr;
BLECharacteristic* bleDiscoveryCharacteristic = nullptr;
BLECharacteristic* bleWifiScanCharacteristic = nullptr;
BLECharacteristic* bleWifiCredentialCharacteristic = nullptr;
BLECharacteristic* bleWifiStatusCharacteristic = nullptr;
BLECharacteristic* bleApConfigCharacteristic = nullptr;
BLECharacteristic* bleHardwareIdCharacteristic = nullptr;
String pendingApPassword;
volatile bool bleApPasswordChangeRequested = false;
String bleCredentialHexBuffer;
int bleCredentialExpectedHexLength = -1;
volatile bool bleWifiConnectRequested = false;
String pendingWifiSsid;
String pendingWifiPassword;
unsigned long wifiProvisioningStartedAt = 0;
bool wifiProvisioningInProgress = false;
static const unsigned long WIFI_PROVISIONING_TIMEOUT_MS = 25000;
volatile bool bleWifiScanRequested = false;
bool bleWifiScanRunning = false;
String bleWifiScanJson = "{\"networks\":[]}";
void handleForward();
void handleReverse();
void handleSteerLeft();
void handleSteerRight();
void handleSteerCenter();
void handleNozzleSweep();
void handleLeftNozzleSweep();
void handleBackNozzleSweep();
void handleRightNozzleSweep();
void handleFullNozzleSweep();
void handleNozzleSweepStop();
void configureManualNozzleSweep(int minimumAngle, int maximumAngle);
void handleSetIrrigationProfile();
void handleIrrigationStart();
void handleIrrigationStop();
void serviceNozzleSweep();
void serviceIrrigationSequence();
void startIrrigationSequence(bool manual = false);
void beginIrrigationPhase();
void handleStop();
void handleAutonomousStart();
void handleAutonomousStop();
void handlePathwayStart();
void handlePathwayStop();
void handleTerrainStart();
void handleTerrainBiasLeft();
void handleTerrainBiasRight();
void handleTerrainBiasCenter();
void handleTerrainStop();
void runTerrainStep();
void handleManualTerrainStart();
void handleManualTerrainStop();
void serviceManualTerrainMode();
void enforceAutonomousControlTimeout();
void handlePumpOn();
void handlePumpOff();
void handleHeartbeat();
void handleResetShield();
void serviceBleWifiProvisioning();
void serviceBleApPasswordChange();
float getObstacleDistanceCM();
void initializeAudio();
void playAudioTrack(uint16_t trackNumber);
void setLedOutput(bool enabled);
void readBatteryVoltage();
void triggerEmergencyStop(const char* source);
bool resetEmergencyStop();
void serviceMqttCommands();
void publishMqttCommandAck(const String& commandId, const char* status, const char* code, const char* message);

// ============================================================================
// BLE DISCOVERY + READ-ONLY WI-FI SCAN
// ============================================================================
class WifiScanCommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    const String command = characteristic->getValue();
    if (command == "SCAN") {
      bleWifiScanRequested = true;
      characteristic->setValue("SCANNING");
    } else if (command.startsWith("GET:")) {
      const int offset = command.substring(4).toInt();
      if (offset < 0 || offset >= (int)bleWifiScanJson.length()) {
        characteristic->setValue("");
      } else {
        const String chunk = bleWifiScanJson.substring(offset, offset + 18);
        characteristic->setValue(chunk.c_str());
      }
    } else {
      characteristic->setValue("ERROR");
    }
  }
};

class ApConfigCommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    const String command = characteristic->getValue();
    if (command == "GET") {
      characteristic->setValue((String("PASSWORD:") + apPassword).c_str());
      return;
    }
    if (!command.startsWith("SET:")) {
      characteristic->setValue("ERROR:COMMAND");
      return;
    }

    const String newPassword = command.substring(4);
    if (newPassword.length() < 8 || newPassword.length() > 63) {
      characteristic->setValue("ERROR:LENGTH");
      return;
    }

    for (size_t i = 0; i < newPassword.length(); ++i) {
      const uint8_t ch = (uint8_t)newPassword[i];
      if (ch < 0x20 || ch == 0x7F) {
        characteristic->setValue("ERROR:FORMAT");
        return;
      }
    }

    pendingApPassword = newPassword;
    bleApPasswordChangeRequested = true;
    characteristic->setValue("QUEUED");
  }
};

String bleControlAssembly;
int bleControlExpectedLength = -1;

class BleControlCommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    const String incoming = characteristic->getValue();
    if (incoming.startsWith("BEGIN:")) {
      const int expected = incoming.substring(6).toInt();
      if (expected < 16 || expected > 240) {
        bleControlExpectedLength = -1;
        bleControlAssembly = "";
        return;
      }
      bleControlExpectedLength = expected;
      bleControlAssembly = "";
      bleControlAssembly.reserve(expected);
      return;
    }

    if (incoming.startsWith("D") && incoming.length() >= 6 && incoming[4] == ':') {
      if (bleControlExpectedLength < 0) return;
      const int offset = incoming.substring(1, 4).toInt();
      const String chunk = incoming.substring(5);
      if (offset != (int)bleControlAssembly.length() ||
          bleControlAssembly.length() + chunk.length() > bleControlExpectedLength) {
        bleControlExpectedLength = -1;
        bleControlAssembly = "";
        return;
      }
      bleControlAssembly += chunk;
      return;
    }

    if (incoming != "COMMIT" || bleControlExpectedLength < 0 ||
        bleControlAssembly.length() != bleControlExpectedLength) {
      bleControlExpectedLength = -1;
      bleControlAssembly = "";
      return;
    }

    portENTER_CRITICAL(&bleControlMux);
    if (!bleControlCommandPending) {
      pendingBleControlCommand = bleControlAssembly;
      bleControlCommandPending = true;
    }
    portEXIT_CRITICAL(&bleControlMux);
    bleControlExpectedLength = -1;
    bleControlAssembly = "";
  }
};

void publishBleControlAck(const String& commandId, const char* status,
                          const char* code, const char* message) {
  if (bleControlAckCharacteristic == nullptr) return;
  StaticJsonDocument<256> doc;
  doc["commandId"] = commandId;
  doc["status"] = status;
  doc["code"] = code;
  doc["message"] = message;
  String payload;
  serializeJson(doc, payload);
  bleControlAckCharacteristic->setValue(payload.c_str());
  bleControlAckCharacteristic->notify();
}

void serviceBleControlCommands() {
  if (!bleControlCommandPending) return;

  String raw;
  portENTER_CRITICAL(&bleControlMux);
  raw = pendingBleControlCommand;
  pendingBleControlCommand = "";
  bleControlCommandPending = false;
  portEXIT_CRITICAL(&bleControlMux);

  StaticJsonDocument<512> doc;
  if (deserializeJson(doc, raw) || !doc.is<JsonObject>()) {
    publishBleControlAck("", "rejected", "INVALID_JSON", "Command JSON is invalid");
    return;
  }

  const int version = doc["version"] | 0;
  const String commandId = doc["commandId"] | "";
  const String command = doc["command"] | "";
  if (version != 1 || commandId.length() < 8 || commandId.length() > 64 || command.length() == 0) {
    publishBleControlAck(commandId, "rejected", "INVALID_ENVELOPE", "Command envelope is invalid");
    return;
  }

  if (emergencyStopLatched && command != "stop" && command != "pump_off") {
    publishBleControlAck(commandId, "rejected", "ESTOP_LATCHED", "Emergency stop is latched");
    return;
  }

  bool supported = true;
  if (command == "forward") {
    setManualMotorDirection(CMD_CW, "forward");
  } else if (command == "reverse") {
    setManualMotorDirection(CMD_CCW, "reverse");
  } else if (command == "stop") {
    handleStop();
  } else if (command == "pump_on") {
    handlePumpOn();
  } else if (command == "pump_off") {
    handlePumpOff();
  } else if (command == "heartbeat") {
    handleHeartbeat();
  } else if (command == "steer_left") {
    handleSteerLeft();
  } else if (command == "steer_right") {
    handleSteerRight();
  } else if (command == "steer_center") {
    handleSteerCenter();
  } else if (command == "nozzle_sweep" || command == "nozzle_sweep_full") {
    handleFullNozzleSweep();
  } else if (command == "nozzle_sweep_left") {
    handleLeftNozzleSweep();
  } else if (command == "nozzle_sweep_back") {
    handleBackNozzleSweep();
  } else if (command == "nozzle_sweep_right") {
    handleRightNozzleSweep();
  } else if (command == "nozzle_sweep_stop") {
    handleNozzleSweepStop();
  } else if (command == "irrigation_start") {
    handleIrrigationStart();
  } else if (command == "irrigation_stop") {
    handleIrrigationStop();
  } else if (command == "autonomous_start") {
    handleAutonomousStart();
  } else if (command == "autonomous_stop") {
    handleAutonomousStop();
  } else if (command == "pathway_start") {
    handlePathwayStart();
  } else if (command == "pathway_stop") {
    handlePathwayStop();
  } else if (command == "terrain_start") {
    handleTerrainStart();
  } else if (command == "terrain_bias_left") {
    handleTerrainBiasLeft();
  } else if (command == "terrain_bias_right") {
    handleTerrainBiasRight();
  } else if (command == "terrain_bias_center") {
    handleTerrainBiasCenter();
  } else if (command == "terrain_stop") {
    handleTerrainStop();
  } else if (command == "manual_terrain_start") {
    handleManualTerrainStart();
  } else if (command == "manual_terrain_stop") {
    handleManualTerrainStop();
  } else if (command == "reset_shield") {
    handleResetShield();
  } else {
    supported = false;
  }

  if (supported) {
    publishBleControlAck(commandId, "executed", "OK", "Command accepted by robot");
  } else {
    publishBleControlAck(commandId, "rejected", "UNSUPPORTED_COMMAND", "Command is not enabled over BLE yet");
  }
}

void publishBleTelemetryIfDue() {
  if (bleControlTelemetryCharacteristic == nullptr ||
      millis() - lastBleTelemetryAt < 1000) return;
  lastBleTelemetryAt = millis();

  StaticJsonDocument<768> doc;
  doc["firmwareVersion"] = ARIGATOR_FIRMWARE_VERSION;
  doc["state"] = currentMode == MODE_AUTONOMOUS ? (pumpState ? "Watering" : "Automatic") :
                 ((manualMotorDir == CMD_CW || manualMotorDir == CMD_CCW) ? "Manual" : "Idle");
  doc["mode"] = currentMode == MODE_AUTONOMOUS ? "Automatic" : "Manual";
  switch (activeAutonomousMode) {
    case AUTO_PATHWAY: doc["autonomousMode"] = "PATHWAY"; break;
    case AUTO_TERRAIN: doc["autonomousMode"] = "TERRAIN"; break;
    case AUTO_GENERIC: doc["autonomousMode"] = "GENERIC"; break;
    default: doc["autonomousMode"] = "NONE"; break;
  }
  doc["pumpIsOn"] = pumpState;
  doc["controlsEnabled"] = controlsEnabled;
  doc["wifiConnected"] = WiFi.status() == WL_CONNECTED;
  doc["wifiIp"] = WiFi.status() == WL_CONNECTED ? WiFi.localIP().toString() : "";
  doc["hostname"] = "A-Rigator";
  doc["obstacleDistanceCm"] = getObstacleDistanceCM();
  doc["obstacleStopThresholdCm"] = OBSTACLE_STOP_CM;
  doc["manualControlTimeoutMs"] = MANUAL_CONTROL_TIMEOUT_MS;
  doc["lineGpio14"] = digitalRead(14) == HIGH;
  doc["lineGpio27"] = digitalRead(27) == HIGH;
  doc["connectionMode"] = "BLE";
  String payload;
  serializeJson(doc, payload);
  bleControlTelemetryCharacteristic->setValue(payload.c_str());
  bleControlTelemetryCharacteristic->notify();
}

String escapeJsonString(const String& value) {
  String escaped;
  escaped.reserve(value.length() + 8);
  for (size_t i = 0; i < value.length(); ++i) {
    const char ch = value[i];
    if (ch == '\\' || ch == '"') {
      escaped += '\\';
      escaped += ch;
    } else if ((uint8_t)ch >= 0x20) {
      escaped += ch;
    }
  }
  return escaped;
}

void serviceBleWifiProvisioning() {
  if (bleWifiConnectRequested && !wifiProvisioningInProgress) {
    bleWifiConnectRequested = false;
    wifiProvisioningInProgress = true;
    wifiProvisioningStartedAt = millis();
    bleWifiStatusCharacteristic->setValue("CONNECTING");
    WiFi.mode(WIFI_AP_STA);
    WiFi.setAutoReconnect(true);
    WiFi.disconnect(false, false);
    WiFi.begin(pendingWifiSsid.c_str(), pendingWifiPassword.c_str());
  }

  if (!wifiProvisioningInProgress) return;

  if (WiFi.status() == WL_CONNECTED) {
    const bool saved = wifiCredentialStore.save(pendingWifiSsid, pendingWifiPassword);
    if (saved) {
      savedWifiCredentials.ssid = pendingWifiSsid;
      savedWifiCredentials.password = pendingWifiPassword;
      savedWifiCredentialsAvailable = true;
      if (mdnsStarted) MDNS.end();
      mdnsStarted = MDNS.begin("arigator");
      if (mdnsStarted) MDNS.addService("http", "tcp", 80);
      const String result = String("OK:") + WiFi.localIP().toString();
      bleWifiStatusCharacteristic->setValue(result.c_str());
      Serial.printf(">> Wi-Fi provisioned. STA IP: %s (mDNS: arigator.local)\n",
                    WiFi.localIP().toString().c_str());
    } else {
      bleWifiStatusCharacteristic->setValue("ERROR:SAVE");
      Serial.println(">> Wi-Fi connected, but credential storage verification failed.");
    }
    wifiProvisioningInProgress = false;
    pendingWifiSsid = "";
    pendingWifiPassword = "";
    return;
  }

  if (millis() - wifiProvisioningStartedAt >= WIFI_PROVISIONING_TIMEOUT_MS) {
    WiFi.disconnect(false, false);
    bleWifiStatusCharacteristic->setValue("ERROR:CONNECT");
    wifiProvisioningInProgress = false;
    pendingWifiSsid = "";
    pendingWifiPassword = "";
    Serial.println(">> Wi-Fi provisioning timed out. A-Rigator AP remains available.");
    if (savedWifiCredentialsAvailable) {
      WiFi.begin(savedWifiCredentials.ssid.c_str(), savedWifiCredentials.password.c_str());
    }
  }
}

void serviceBleApPasswordChange() {
  if (!bleApPasswordChangeRequested) return;

  bleApPasswordChangeRequested = false;
  const String requestedPassword = pendingApPassword;
  pendingApPassword = "";

  if (!apConfigStoreAvailable || requestedPassword.length() < 8 ||
      requestedPassword.length() > 63) {
    bleApConfigCharacteristic->setValue("ERROR:STORAGE");
    return;
  }

  const String previousPassword = apPassword;
  if (!apConfigStore.save(requestedPassword)) {
    bleApConfigCharacteristic->setValue("ERROR:SAVE");
    return;
  }

  // Restart only the access point. The station connection and BLE service
  // remain active, allowing the app to reconnect using the new password.
  WiFi.softAPdisconnect(false);
  delay(250);
  WiFi.softAPConfig(AP_IP, AP_GATEWAY, AP_SUBNET);

  bool apStarted = false;
  for (uint8_t attempt = 1; attempt <= WIFI_AP_START_RETRIES; ++attempt) {
    apStarted = WiFi.softAP(WIFI_SSID, requestedPassword.c_str());
    if (apStarted) break;
    delay(WIFI_AP_RETRY_DELAY_MS);
  }

  if (apStarted) {
    apPassword = requestedPassword;
    bleApConfigCharacteristic->setValue("OK:AP_RESTARTED");
    Serial.println(">> AP password updated and access point restarted.");
    Serial.println(">> The app must reconnect to A-Rigator using the new password.");
    return;
  }

  // Roll back both NVS and the running AP if the new AP could not start.
  apConfigStore.save(previousPassword);
  WiFi.softAPConfig(AP_IP, AP_GATEWAY, AP_SUBNET);
  const bool rollbackStarted = WiFi.softAP(WIFI_SSID, previousPassword.c_str());
  if (rollbackStarted) {
    apPassword = previousPassword;
    bleApConfigCharacteristic->setValue("ERROR:AP_START_ROLLED_BACK");
    Serial.println(">> New AP password failed to start; previous password restored.");
  } else {
    bleApConfigCharacteristic->setValue("ERROR:AP_START");
    Serial.println(">> AP restart and rollback both failed. BLE remains available.");
  }
}

void serviceBleWifiScan() {
  if (bleWifiScanRequested && !bleWifiScanRunning) {
    bleWifiScanRequested = false;
    const int result = WiFi.scanNetworks(true, true);
    if (result == WIFI_SCAN_FAILED) {
      bleWifiScanCharacteristic->setValue("ERROR");
    } else {
      bleWifiScanRunning = true;
      bleWifiScanCharacteristic->setValue("SCANNING");
    }
  }

  if (!bleWifiScanRunning) return;

  const int count = WiFi.scanComplete();
  if (count == WIFI_SCAN_RUNNING) return;

  if (count == WIFI_SCAN_FAILED) {
    bleWifiScanCharacteristic->setValue("ERROR");
  } else {
    String json = "{\"networks\":[";
    int addedNetworks = 0;
    for (int i = 0; i < count && addedNetworks < 12; ++i) {
      String entry = "{\"ssid\":\"";
      entry += escapeJsonString(WiFi.SSID(i));
      entry += "\",\"rssi\":";
      entry += String(WiFi.RSSI(i));
      entry += ",\"secured\":";
      entry += (WiFi.encryptionType(i) == WIFI_AUTH_OPEN ? "false" : "true");
      entry += "}";

      const String candidate = json + (addedNetworks > 0 ? "," : "") + entry + "]}";
      if (candidate.length() > 480) break;

      if (addedNetworks > 0) json += ",";
      json += entry;
      ++addedNetworks;
    }
    json += "]}";
    bleWifiScanJson = json;
    bleWifiScanCharacteristic->setValue((String("DONE:") + String(bleWifiScanJson.length())).c_str());
  }

  WiFi.scanDelete();
  bleWifiScanRunning = false;
}

class WifiCredentialCommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    const String command = characteristic->getValue();
    if (command.startsWith("BEGIN:")) {
      const int expected = command.substring(6).toInt();
      if (expected < 4 || expected > 194 || (expected % 2) != 0) {
        bleCredentialExpectedHexLength = -1;
        bleCredentialHexBuffer = "";
        bleWifiStatusCharacteristic->setValue("ERROR:SIZE");
        return;
      }
      bleCredentialExpectedHexLength = expected;
      bleCredentialHexBuffer = "";
      bleCredentialHexBuffer.reserve(expected);
      bleWifiStatusCharacteristic->setValue("RECEIVING");
      return;
    }

    if (command.startsWith("D") && command.length() >= 6 && command[4] == ':') {
      if (bleCredentialExpectedHexLength < 0) {
        bleWifiStatusCharacteristic->setValue("ERROR:NO_BEGIN");
        return;
      }
      const int offset = command.substring(1, 4).toInt();
      const String chunk = command.substring(5);
      if (offset != (int)bleCredentialHexBuffer.length() ||
          bleCredentialHexBuffer.length() + chunk.length() > bleCredentialExpectedHexLength) {
        bleCredentialExpectedHexLength = -1;
        bleCredentialHexBuffer = "";
        bleWifiStatusCharacteristic->setValue("ERROR:ORDER");
        return;
      }
      for (size_t i = 0; i < chunk.length(); ++i) {
        if (!isHexadecimalDigit(chunk[i])) {
          bleCredentialExpectedHexLength = -1;
          bleCredentialHexBuffer = "";
          bleWifiStatusCharacteristic->setValue("ERROR:FORMAT");
          return;
        }
      }
      bleCredentialHexBuffer += chunk;
      return;
    }

    if (command == "COMMIT") {
      if (bleCredentialExpectedHexLength < 0 ||
          bleCredentialHexBuffer.length() != bleCredentialExpectedHexLength) {
        bleWifiStatusCharacteristic->setValue("ERROR:INCOMPLETE");
        bleCredentialExpectedHexLength = -1;
        bleCredentialHexBuffer = "";
        return;
      }

      uint8_t payload[97];
      const size_t byteCount = bleCredentialHexBuffer.length() / 2;
      for (size_t i = 0; i < byteCount; ++i) {
        payload[i] = (uint8_t)strtol(bleCredentialHexBuffer.substring(i * 2, i * 2 + 2).c_str(), nullptr, 16);
      }
      bleCredentialExpectedHexLength = -1;
      bleCredentialHexBuffer = "";

      if (byteCount < 2) {
        bleWifiStatusCharacteristic->setValue("ERROR:FORMAT");
        return;
      }
      const uint8_t ssidLength = payload[0];
      const uint8_t passwordLength = payload[1];
      if (ssidLength == 0 || ssidLength > 32 || passwordLength > 63 ||
          byteCount != (size_t)(2 + ssidLength + passwordLength) ||
          (passwordLength != 0 && passwordLength < 8)) {
        bleWifiStatusCharacteristic->setValue("ERROR:VALIDATION");
        return;
      }

      for (size_t i = 2; i < byteCount; ++i) {
        if (payload[i] == 0) {
          bleWifiStatusCharacteristic->setValue("ERROR:VALIDATION");
          return;
        }
      }

      pendingWifiSsid = "";
      pendingWifiPassword = "";
      for (uint8_t i = 0; i < ssidLength; ++i) pendingWifiSsid += (char)payload[2 + i];
      for (uint8_t i = 0; i < passwordLength; ++i) pendingWifiPassword += (char)payload[2 + ssidLength + i];

      bleWifiConnectRequested = true;
      bleWifiStatusCharacteristic->setValue("RECEIVED");
      return;
    }

    bleWifiStatusCharacteristic->setValue("ERROR:COMMAND");
  }
};

void startBleDiscovery() {
  BLEDevice::init(BLE_DEVICE_NAME);
  bleServer = BLEDevice::createServer();
  bleProvisioningService = bleServer->createService(BLE_PROVISIONING_SERVICE_UUID);
  bleDiscoveryCharacteristic = bleProvisioningService->createCharacteristic(
    BLE_DISCOVERY_CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_READ
  );
  bleDiscoveryCharacteristic->setValue("A-Rigator Wi-Fi provisioning available");

  // Stable hardware identity; unlike the phone's BLE device ID, this survives
  // reconnects and is derived from the ESP32 eFuse MAC address.
  char hardwareId[13];
  snprintf(hardwareId, sizeof(hardwareId), "%012llX", (unsigned long long)ESP.getEfuseMac());
  bleHardwareIdCharacteristic = bleProvisioningService->createCharacteristic(
    BLE_HARDWARE_ID_CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_READ
  );
  bleHardwareIdCharacteristic->setValue(hardwareId);

  bleWifiScanCharacteristic = bleProvisioningService->createCharacteristic(
    BLE_WIFI_SCAN_CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE
  );
  bleWifiScanCharacteristic->setCallbacks(new WifiScanCommandCallbacks());
  bleWifiScanCharacteristic->setValue("{\"state\":\"idle\",\"networks\":[]}");

  bleWifiCredentialCharacteristic = bleProvisioningService->createCharacteristic(
    BLE_WIFI_CREDENTIAL_CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_WRITE
  );
  bleWifiCredentialCharacteristic->setAccessPermissions(ESP_GATT_PERM_WRITE_ENCRYPTED);
  bleWifiCredentialCharacteristic->setCallbacks(new WifiCredentialCommandCallbacks());

  bleWifiStatusCharacteristic = bleProvisioningService->createCharacteristic(
    BLE_WIFI_STATUS_CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_READ
  );
  bleWifiStatusCharacteristic->setValue("IDLE");

  bleApConfigCharacteristic = bleProvisioningService->createCharacteristic(
    BLE_AP_CONFIG_CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE
  );
  bleApConfigCharacteristic->setAccessPermissions(ESP_GATT_PERM_READ_ENCRYPTED | ESP_GATT_PERM_WRITE_ENCRYPTED);
  bleApConfigCharacteristic->setCallbacks(new ApConfigCommandCallbacks());
  bleApConfigCharacteristic->setValue("READY");

  BLESecurity* security = new BLESecurity();
  security->setAuthenticationMode(true, false, true);
  security->setCapability(ESP_IO_CAP_NONE);

  bleProvisioningService->start();

  // Separate control service keeps the existing Wi-Fi provisioning UUIDs
  // stable while adding command, acknowledgement, and telemetry channels.
  bleControlService = bleServer->createService(BLE_CONTROL_SERVICE_UUID);
  bleControlCommandCharacteristic = bleControlService->createCharacteristic(
    BLE_CONTROL_COMMAND_UUID, BLECharacteristic::PROPERTY_WRITE
  );
  bleControlCommandCharacteristic->setAccessPermissions(ESP_GATT_PERM_WRITE_ENCRYPTED);
  bleControlCommandCharacteristic->setCallbacks(new BleControlCommandCallbacks());

  bleControlAckCharacteristic = bleControlService->createCharacteristic(
    BLE_CONTROL_ACK_UUID, BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
  );
  bleControlAckCharacteristic->setAccessPermissions(ESP_GATT_PERM_READ_ENCRYPTED);
  bleControlAckCharacteristic->setValue("{\\"status\\":\\"ready\\"}");

  bleControlTelemetryCharacteristic = bleControlService->createCharacteristic(
    BLE_CONTROL_TELEMETRY_UUID, BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
  );
  bleControlTelemetryCharacteristic->setAccessPermissions(ESP_GATT_PERM_READ_ENCRYPTED);
  bleControlTelemetryCharacteristic->setValue("{}");
  bleControlService->start();

  BLEAdvertising* advertising = BLEDevice::getAdvertising();
  advertising->addServiceUUID(BLE_PROVISIONING_SERVICE_UUID);
  advertising->addServiceUUID(BLE_CONTROL_SERVICE_UUID);
  advertising->setScanResponse(true);
  BLEDevice::startAdvertising();
  Serial.println(">> BLE discovery advertising started (A-Rigator).");
}

// ============================================================================
// 2. PIN DEFINITIONS
// ============================================================================
#define SDA_PIN             21    // WeMos Shield D2 (SDA)
#define SCL_PIN             22    // WeMos Shield D1 (SCL)
#define SHIELD_RST_PIN      5     // WeMos Shield RST (D5 on ESP32)

#define PUMP_RELAY_PIN      4     // Relay IN (D4 on ESP32)
#define STEER_SERVO_PIN     13    // MG996R Steering Servo (D13 on ESP32)
#define NOZZLE_SERVO_PIN    12    // Nozzle Sweep Servo (D12 on ESP32)

#define LINE_LEFT_PIN       14    // TCRT5000 Left Digital (D14)
#define LINE_RIGHT_PIN      27    // TCRT5000 Right Digital (D27)

// Front obstacle-detection sonar
#define OBSTACLE_TRIG_PIN   18    // JSN-B02 Trigger (D18)
#define OBSTACLE_ECHO_PIN   19    // JSN-B02 Echo (D19)

// Reservoir water-level sonar (GPIOs reserved; placement/calibration pending)
#define WATER_TRIG_PIN      25
#define WATER_ECHO_PIN      26

#define CHASSIS_BTN_PIN     15    // Reserved; physical emergency-stop wiring to be verified

// Added peripherals (ESP32-WROOM-32U)
#define DFPLAYER_RX_PIN     16    // ESP32 RX2 <- DFPlayer TX
#define DFPLAYER_TX_PIN     17    // ESP32 TX2 -> DFPlayer RX
#define LED_MOSFET_PIN      32    // F5305S module signal input
#define BATTERY_ADC_PIN     35    // Voltage sensor analog output (ADC1, input-only)

// ============================================================================
// 3. CONSTANTS & FULL-POWER SETTINGS
// ============================================================================
#define SHIELD_ADDR         0x30
#define MOTOR_A             0x00
#define MOTOR_B             0x01

#define CMD_BRAKE           0x00
#define CMD_CW               0x01
#define CMD_CCW              0x02
#define CMD_STOP            0x03

#define FULL_POWER          100.0

// The factory firmware on some WeMos V1 motor shields can lock its I2C
// interface after ~10 seconds without a command. Refresh the last output
// every 4 seconds to keep the shield responsive without changing motor state.
static const unsigned long SHIELD_KEEPALIVE_INTERVAL_MS = 4000;
unsigned long lastShieldKeepAlive = 0;
bool motorShieldInitialized = false;
uint8_t lastShieldMotorDirection = CMD_BRAKE;
float lastShieldMotorSpeed = 0.0;

#define RELAY_ON            LOW
#define RELAY_OFF           HIGH

#define STEER_CENTER        90
#define STEER_LEFT          45
#define STEER_RIGHT         135

#define NOZZLE_CENTER           90
// Start with a conservative 120-degree command range; validate physical clearance
// before increasing it further on the assembled nozzle mechanism.
#define NOZZLE_MIN_SWEEP        30
#define NOZZLE_MAX_SWEEP        150
#define NOZZLE_LEFT_SECTOR_MAX  70
#define NOZZLE_RIGHT_SECTOR_MIN 110

#define OBSTACLE_STOP_CM    25

// ============================================================================
// 4. STATE VARIABLES
// ============================================================================
enum RobotMode {
  MODE_MANUAL,
  MODE_AUTONOMOUS
};

RobotMode currentMode = MODE_MANUAL;
enum AutonomousMode { AUTO_NONE, AUTO_GENERIC, AUTO_PATHWAY, AUTO_TERRAIN };
AutonomousMode activeAutonomousMode = AUTO_NONE;
int terrainBias = 0; // -1 = left, 0 = neutral, 1 = right
const int TERRAIN_BIAS_OFFSET_DEGREES = 10;
const unsigned long TERRAIN_STEERING_INTERVAL_MS = 650;
const int TERRAIN_STEERING_PATTERN[4] = { STEER_LEFT, STEER_CENTER, STEER_RIGHT, STEER_CENTER };
int terrainPatternIndex = 0;
unsigned long lastTerrainSteeringStep = 0;
uint8_t manualMotorDir = CMD_STOP;
bool pumpState = false;
// Software emergency-stop latch. Physical GPIO15 activation remains disabled
// until the combined chassis button board is electrically verified.
bool emergencyStopLatched = false;
static const bool PHYSICAL_ESTOP_INPUT_ENABLED = false;
bool nozzleSweepActive = false;
bool manualTerrainModeActive = false;
int manualTerrainSteeringAngle = STEER_LEFT;
unsigned long lastManualTerrainSteeringStep = 0;
int irrigationLeftSeconds = 3;
int irrigationBackSeconds = 3;
int irrigationRightSeconds = 3;
int irrigationRepeatCount = 1;
bool irrigationSequenceActive = false;
bool irrigationSequenceManual = false;
String serialCommandBuffer;
bool irrigationSequenceSpraying = false;
bool irrigationZoneLatched = false;
uint8_t irrigationSequencePhase = 0;
int irrigationSequencesCompleted = 0;
unsigned long irrigationPhaseStartedAt = 0;
int nozzleSweepAngle = NOZZLE_MIN_SWEEP;
int nozzleSweepMinimumAngle = NOZZLE_MIN_SWEEP;
int nozzleSweepMaximumAngle = NOZZLE_MAX_SWEEP;
int nozzleSweepDirection = 1;
unsigned long lastNozzleSweepStep = 0;
unsigned long lastAutonomousHeartbeat = 0;
bool autonomousControlLeaseActive = false;
const unsigned long AUTONOMOUS_CONTROL_TIMEOUT_MS = 2500;
unsigned long lastMotorRefresh = 0;
unsigned long lastManualControlRefresh = 0;
bool manualControlLeaseActive = false;
bool controlsEnabled = false;

// Added peripherals: DFPlayer Mini, LED power switch, and battery telemetry.
// Requires the "DFRobotDFPlayerMini" Arduino library.
HardwareSerial audioSerial(2);
DFRobotDFPlayerMini audioPlayer;
bool audioAvailable = false;
bool ledOutputEnabled = false;
float batteryVoltage = 0.0f;
bool batteryVoltageValid = false;
unsigned long lastBatteryRead = 0;
static const unsigned long BATTERY_SAMPLE_INTERVAL_MS = 1000;
static const float BATTERY_DIVIDER_RATIO = 5.0f; // Standard 0-25V sensor module
// Starting 2S LiFePO4 thresholds. Calibrate the voltage sensor before relying
// on these values for safety decisions.
static const float BATTERY_LOW_WARNING_V = 6.0f;
static const float BATTERY_CRITICAL_V = 5.8f;
static const bool BATTERY_CRITICAL_STOP_ENABLED = false; // Enable only after meter calibration
static const unsigned long BATTERY_WARNING_REPEAT_MS = 60000;
bool lowBatteryWarningActive = false;
bool criticalBatteryWarningActive = false;
unsigned long lastBatteryWarningAt = 0;
bool obstacleWasDetected = false;
unsigned long lastObstacleAlertAt = 0;
static const unsigned long OBSTACLE_AUDIO_COOLDOWN_MS = 10000;
static const unsigned long WARNING_AUDIO_PRIORITY_MS = 8000;
unsigned long warningAudioPriorityUntil = 0;

// Forget only the saved router profile. Preserve the A-Rigator AP password and
// keep the local setup hotspot available so the app can provision another network.
bool forgetSavedWifiNetwork(bool disconnectNow = true) {
  setPump(false);
  autonomousControlLeaseActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationZoneLatched = false;
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);

  if (!wifiCredentialStore.clear()) {
    savedWifiCredentialsAvailable = wifiCredentialStore.load(savedWifiCredentials);
    Serial.println(">> Failed to clear saved Wi-Fi credentials.");
    return false;
  }

  savedWifiCredentials.ssid = "";
  savedWifiCredentials.password = "";
  savedWifiCredentialsAvailable = false;

  if (mdnsStarted) {
    MDNS.end();
    mdnsStarted = false;
  }

  // Disconnect and erase the ESP32 station profile while leaving the AP
  // interface running. The independent arigator-ap namespace is untouched.
  WiFi.setAutoReconnect(false);
  if (disconnectNow) {
    WiFi.disconnect(false, true);
  } else {
    // Let the HTTP response reach the app before dropping the router link.
    wifiForgetDisconnectPending = true;
    wifiForgetDisconnectAt = millis() + 750;
  }
  Serial.println(">> Saved router Wi-Fi forgotten. A-Rigator setup AP remains available.");
  return true;
}

void handleForgetWifi() {
  const bool success = forgetSavedWifiNetwork(false);
  if (success) {
    server.send(200, "application/json",
                "{\"success\":true,\"message\":\"Saved router Wi-Fi forgotten; A-Rigator AP preserved\"}");
  } else {
    server.send(500, "application/json",
                "{\"success\":false,\"message\":\"Could not clear saved router Wi-Fi\"}");
  }
}

void handleStatus() {
  const char* state = "Idle";
  const char* mode = currentMode == MODE_AUTONOMOUS ? "Automatic" : "Manual";

  if (currentMode == MODE_AUTONOMOUS) {
	state = pumpState ? "Watering" : "Automatic";
  } else if (manualMotorDir == CMD_CW || manualMotorDir == CMD_CCW) {
	state = "Manual";
  }

	String json = "{\"success\":true,\"firmwareVersion\":\"";
  json += ARIGATOR_FIRMWARE_VERSION;
  json += "\",\"state\":\"";
  json += state;
  json += "\",\"mode\":\"";
  json += mode;
  json += "\",\"autonomousMode\":\"";
  switch (activeAutonomousMode) {
    case AUTO_PATHWAY: json += "PATHWAY"; break;
    case AUTO_TERRAIN: json += "TERRAIN"; break;
    case AUTO_GENERIC: json += "GENERIC"; break;
    default: json += "NONE"; break;
  }
  json += "\",\"pump\":";
  json += pumpState ? "true" : "false";
  json += ",\"batteryVoltage\":";
  json += batteryVoltageValid ? String(batteryVoltage, 2) : "null";
  json += ",\"batteryState\":\"";
  if (!batteryVoltageValid) json += "UNKNOWN";
  else if (batteryVoltage <= BATTERY_CRITICAL_V) json += "CRITICAL";
  else if (batteryVoltage <= BATTERY_LOW_WARNING_V) json += "LOW";
  else json += "NORMAL";
  json += "\",\"batteryLowThresholdV\":";
  json += String(BATTERY_LOW_WARNING_V, 2);
  json += ",\"batteryCriticalThresholdV\":";
  json += String(BATTERY_CRITICAL_V, 2);
  json += ",\"mqttConfigured\":";
  json += mqttConfigured ? "true" : "false";
  json += ",\"mqttConnected\":";
  json += mqttManager.connected() ? "true" : "false";
  json += ",\"robotId\":\"";
  json += mqttConfigured ? escapeJsonString(mqttConfig.robotId) : "";
  json += "\",\"emergencyStopLatched\":";
  json += emergencyStopLatched ? "true" : "false";
  json += ",\"physicalEmergencyStopEnabled\":";
  json += PHYSICAL_ESTOP_INPUT_ENABLED ? "true" : "false";
  json += ",\"ledEnabled\":";
  json += ledOutputEnabled ? "true" : "false";
  json += ",\"audioAvailable\":";
  json += audioAvailable ? "true" : "false";
	json += ",\"controlsEnabled\":";
  json += controlsEnabled ? "true" : "false";
  json += ",\"wifiConnected\":";
  json += WiFi.status() == WL_CONNECTED ? "true" : "false";
  json += ",\"wifiIp\":\"";
  json += WiFi.status() == WL_CONNECTED ? WiFi.localIP().toString() : "";
  json += "\",\"hostname\":\"arigator.local\"";

  // Live sensor telemetry: reservoir sonar pins are reserved but not yet installed.
  const float obstacleDistanceCm = getObstacleDistanceCM();
  json += ",\"obstacleDistanceCm\":";
  json += String(obstacleDistanceCm, 1);
  json += ",\"obstacleEchoDetected\":";
  json += obstacleDistanceCm < 998.0 ? "true" : "false";
  json += ",\"obstacleDetected\":";
  json += obstacleDistanceCm < OBSTACLE_STOP_CM ? "true" : "false";
  json += ",\"obstacleStopThresholdCm\":";
  json += String(OBSTACLE_STOP_CM);
  json += ",\"manualControlTimeoutMs\":";
  json += String(MANUAL_CONTROL_TIMEOUT_MS);
  json += ",\"lineGpio14\":";
  json += digitalRead(14) == HIGH ? "true" : "false";
  json += ",\"lineGpio27\":";
  json += digitalRead(27) == HIGH ? "true" : "false";
  json += "}";
  server.send(200, "application/json", json);
}

void handleNotFound() {
  server.send(404, "application/json", "{\"success\":false,\"message\":\"Not found\"}");
}

void startWifiApi() {
  if (apPassword.length() < 8 || apPassword.length() > 63) {
    Serial.println(">> AP password is unavailable or invalid. AP startup is blocked for safety.");
    controlsEnabled = false;
    return;
  }

  // Keep the local AP available while the ESP32 joins the configured router.
  // The AP password is loaded from NVS and can be changed through encrypted BLE.
  WiFi.mode(savedWifiCredentialsAvailable ? WIFI_AP_STA : WIFI_AP);
  WiFi.softAPConfig(AP_IP, AP_GATEWAY, AP_SUBNET);

  bool apStarted = false;
  for (uint8_t attempt = 1; attempt <= WIFI_AP_START_RETRIES; ++attempt) {
    apStarted = WiFi.softAP(WIFI_SSID, apPassword.c_str());
    if (apStarted) break;
    Serial.printf(">> Wi-Fi AP start attempt %u/%u failed. Retrying...\n", attempt, WIFI_AP_START_RETRIES);
    delay(WIFI_AP_RETRY_DELAY_MS);
  }

  if (!apStarted) {
    Serial.println(">> Wi-Fi AP failed to start; robot serial controls remain available.");
    return;
  }

  controlsEnabled = true;
  server.on("/api/status", HTTP_GET, handleStatus);
  server.on("/api/network/forget", HTTP_POST, handleForgetWifi);
  server.on("/api/safety/emergency-stop", HTTP_POST, []() {
    triggerEmergencyStop("Wi-Fi API");
    sendControlResponse("emergency_stop");
  });
  server.on("/api/safety/emergency-reset", HTTP_POST, []() {
    if (resetEmergencyStop()) sendControlResponse("emergency_reset");
    else server.send(409, "application/json", "{\"success\":false,\"message\":\"Emergency stop is not latched\"}");
  });
  if (controlsEnabled) {
	server.on("/api/control/forward", HTTP_POST, handleForward);
	server.on("/api/control/reverse", HTTP_POST, handleReverse);
	server.on("/api/control/steer/left", HTTP_POST, handleSteerLeft);
	server.on("/api/control/steer/right", HTTP_POST, handleSteerRight);
	server.on("/api/control/steer/center", HTTP_POST, handleSteerCenter);
	server.on("/api/control/nozzle/sweep", HTTP_POST, handleNozzleSweep);
    server.on("/api/control/nozzle/sweep/left", HTTP_POST, handleLeftNozzleSweep);
    server.on("/api/control/nozzle/sweep/back", HTTP_POST, handleBackNozzleSweep);
    server.on("/api/control/nozzle/sweep/right", HTTP_POST, handleRightNozzleSweep);
    server.on("/api/control/nozzle/sweep/full", HTTP_POST, handleFullNozzleSweep);
    server.on("/api/control/nozzle/sweep/stop", HTTP_POST, handleNozzleSweepStop);
    server.on("/api/irrigation/profile", HTTP_POST, handleSetIrrigationProfile);
    server.on("/api/irrigation/start", HTTP_POST, handleIrrigationStart);
    server.on("/api/irrigation/stop", HTTP_POST, handleIrrigationStop);
	server.on("/api/control/stop", HTTP_POST, handleStop);
	server.on("/api/control/autonomous/start", HTTP_POST, handleAutonomousStart);
	server.on("/api/control/autonomous/stop", HTTP_POST, handleAutonomousStop);
	server.on("/api/control/pathway/start", HTTP_POST, handlePathwayStart);
	server.on("/api/control/pathway/stop", HTTP_POST, handlePathwayStop);
    server.on("/api/control/terrain/start", HTTP_POST, handleTerrainStart);
    server.on("/api/control/terrain/bias/left", HTTP_POST, handleTerrainBiasLeft);
    server.on("/api/control/terrain/bias/right", HTTP_POST, handleTerrainBiasRight);
    server.on("/api/control/terrain/bias/center", HTTP_POST, handleTerrainBiasCenter);
    server.on("/api/control/terrain/stop", HTTP_POST, handleTerrainStop);
    server.on("/api/control/manual-terrain/start", HTTP_POST, handleManualTerrainStart);
    server.on("/api/control/manual-terrain/stop", HTTP_POST, handleManualTerrainStop);
	server.on("/api/control/pump/on", HTTP_POST, handlePumpOn);
	server.on("/api/control/pump/off", HTTP_POST, handlePumpOff);
	server.on("/api/control/heartbeat", HTTP_POST, handleHeartbeat);
	server.on("/api/control/shield/reset", HTTP_POST, handleResetShield);
  }
  server.onNotFound(handleNotFound);
  server.begin();

  if (savedWifiCredentialsAvailable) {
    WiFi.setAutoReconnect(true);
    WiFi.begin(savedWifiCredentials.ssid.c_str(),
               savedWifiCredentials.password.c_str());
    Serial.println(">> Attempting to connect to the saved Wi-Fi network in the background.");
  }

  Serial.println(">> WPA2 AP and manual-control API ready.");
  Serial.print(">> SSID: ");
  Serial.println(WIFI_SSID);
  Serial.print(">> Status URL: http://");
  Serial.print(WiFi.softAPIP());
  Serial.println("/api/status");
  if (controlsEnabled) {
	Serial.println(">> Manual commands require recurring app heartbeats; lease timeout stops motor and pump.");
  }
}

// ============================================================================
// 5. HARDWARE DRIVERS
// ============================================================================
void initShieldFrequency(uint32_t freq) {
  Wire.beginTransmission(SHIELD_ADDR);
  Wire.write(((byte)(freq >> 24)) & (byte)0x0F);
  Wire.write((byte)(freq >> 16));
  Wire.write((byte)(freq >> 8));
  Wire.write((byte)freq);
  byte err = Wire.endTransmission();
  Serial.printf(">> Frequency Init (1000 Hz): %s\n", (err == 0) ? "SUCCESS" : "FAILED");
}

void sendRawPacket(uint8_t channel, uint8_t dir, float speed_percent) {
  uint16_t pwm_val = (uint16_t)(speed_percent * 100.0);
  Wire.beginTransmission(SHIELD_ADDR);
  Wire.write(channel | 0x10);
  Wire.write(dir);
  Wire.write((uint8_t)(pwm_val >> 8));
  Wire.write((uint8_t)(pwm_val & 0xFF));
  Wire.endTransmission();
}

void driveMotor(uint8_t dir, float speed_percent) {
  // Remember the actual output so the keep-alive repeats the current command,
  // whether the robot is moving, coasting, or braking.
  lastShieldMotorDirection = dir;
  lastShieldMotorSpeed = speed_percent;
  sendRawPacket(MOTOR_A, dir, speed_percent);
  sendRawPacket(MOTOR_B, dir, speed_percent);
  lastShieldKeepAlive = millis();
}

void refreshMotorShieldKeepAlive() {
  if (!motorShieldInitialized ||
      millis() - lastShieldKeepAlive < SHIELD_KEEPALIVE_INTERVAL_MS) {
    return;
  }

  // Send the last motor state to both channels. Do not use driveMotor()
  // here because this is a refresh, not a new motor-state change.
  sendRawPacket(MOTOR_A, lastShieldMotorDirection, lastShieldMotorSpeed);
  sendRawPacket(MOTOR_B, lastShieldMotorDirection, lastShieldMotorSpeed);
  lastShieldKeepAlive = millis();
}

void initializeMotorShield() {
  motorShieldInitialized = false;
  digitalWrite(SHIELD_RST_PIN, LOW);
  delay(50);
  digitalWrite(SHIELD_RST_PIN, HIGH);
  delay(250);
  initShieldFrequency(1000);
  delay(80);
  initShieldFrequency(1000);
  motorShieldInitialized = true;
  driveMotor(CMD_BRAKE, 0.0);
}

void resetMotorShield() {
  autonomousControlLeaseActive = false;
  activeAutonomousMode = AUTO_NONE;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationZoneLatched = false;
  currentMode = MODE_MANUAL;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  initializeMotorShield();
  Serial.println(">> Motor shield reset and initialized.");
}

void handleResetShield() {
  setPump(false);
  resetMotorShield();
  sendControlResponse("shield_reset");
}

void writeServo(int pin, int angle) {
  int pulse_us = map(constrain(angle, 0, 180), 0, 180, 550, 2450);
  for (int i = 0; i < 15; i++) {
	digitalWrite(pin, HIGH);
	delayMicroseconds(pulse_us);
	digitalWrite(pin, LOW);
	delay(20);
    // Servo routines block the main loop; service the shield here as well
    // so long nozzle sweeps cannot exceed the keep-alive interval.
    refreshMotorShieldKeepAlive();
  }
}

void initializeAudio() {
  audioSerial.begin(9600, SERIAL_8N1, DFPLAYER_RX_PIN, DFPLAYER_TX_PIN);
  if (audioPlayer.begin(audioSerial, true, true)) {
    audioAvailable = true;
    audioPlayer.volume(22); // DFPlayer volume range: 0-30
    Serial.println(">> DFPlayer Mini initialized.");
  } else {
    audioAvailable = false;
    Serial.println(">> DFPlayer Mini not detected; robot will run without audio.");
  }
}

void playAudioTrack(uint16_t trackNumber) {
  if (!audioAvailable || trackNumber == 0) return;
  const bool isWarning = (trackNumber == 5 || trackNumber == 6 || trackNumber == 7);
  const unsigned long now = millis();

  // Warnings can interrupt ordinary announcements. Briefly suppress routine
  // announcements afterward so they cannot immediately overwrite the warning.
  if (!isWarning && (int32_t)(warningAudioPriorityUntil - now) > 0) return;
  if (isWarning) warningAudioPriorityUntil = now + WARNING_AUDIO_PRIORITY_MS;

  audioPlayer.playMp3Folder(trackNumber); // /mp3/0001.mp3, /mp3/0002.mp3, ...
}

void serviceObstacleAudioAlert(float distanceCm) {
  const bool detected = distanceCm < OBSTACLE_STOP_CM;
  const unsigned long now = millis();
  if (detected && (!obstacleWasDetected ||
      now - lastObstacleAlertAt >= OBSTACLE_AUDIO_COOLDOWN_MS)) {
    playAudioTrack(5); // Obstacle detected: /mp3/0005.mp3
    lastObstacleAlertAt = now;
  }
  obstacleWasDetected = detected;
}

void serviceBatteryWarnings() {
  if (!batteryVoltageValid) return;
  const unsigned long now = millis();

  if (batteryVoltage <= BATTERY_CRITICAL_V) {
    if (!criticalBatteryWarningActive ||
        now - lastBatteryWarningAt >= BATTERY_WARNING_REPEAT_MS) {
      playAudioTrack(6); // Low/critical battery warning: /mp3/0006.mp3
      lastBatteryWarningAt = now;
    }
    criticalBatteryWarningActive = true;
    lowBatteryWarningActive = true;

    // Keep this disabled until the ADC reading is calibrated against a meter.
    if (BATTERY_CRITICAL_STOP_ENABLED &&
        currentMode == MODE_AUTONOMOUS) {
      irrigationSequenceActive = false;
      irrigationSequenceSpraying = false;
      setPump(false);
      driveMotor(CMD_BRAKE, 0.0);
      currentMode = MODE_MANUAL;
      activeAutonomousMode = AUTO_NONE;
      manualMotorDir = CMD_STOP;
      autonomousControlLeaseActive = false;
      Serial.println(">> [SAFETY] Critical battery: autonomous operation stopped.");
    }
    return;
  }

  criticalBatteryWarningActive = false;
  if (batteryVoltage <= BATTERY_LOW_WARNING_V) {
    if (!lowBatteryWarningActive ||
        now - lastBatteryWarningAt >= BATTERY_WARNING_REPEAT_MS) {
      playAudioTrack(6);
      lastBatteryWarningAt = now;
    }
    lowBatteryWarningActive = true;
  } else if (batteryVoltage >= BATTERY_LOW_WARNING_V + 0.20f) {
    lowBatteryWarningActive = false;
  }
}

void setLedOutput(bool enabled) {
  ledOutputEnabled = enabled;
  digitalWrite(LED_MOSFET_PIN, enabled ? HIGH : LOW);
}

void readBatteryVoltage() {
  const unsigned long now = millis();
  if (now - lastBatteryRead < BATTERY_SAMPLE_INTERVAL_MS) return;
  lastBatteryRead = now;

  uint32_t totalMilliVolts = 0;
  for (uint8_t i = 0; i < 10; ++i) {
    totalMilliVolts += analogReadMilliVolts(BATTERY_ADC_PIN);
  }

  const float adcVoltage = (totalMilliVolts / 10.0f) / 1000.0f;
  batteryVoltage = adcVoltage * BATTERY_DIVIDER_RATIO;
  batteryVoltageValid = true;
  serviceBatteryWarnings();
}

void setPump(bool enable) {
  if (enable && emergencyStopLatched) enable = false;
  pumpState = enable;
  digitalWrite(PUMP_RELAY_PIN, enable ? RELAY_ON : RELAY_OFF);
}

void triggerEmergencyStop(const char* source) {
  emergencyStopLatched = true;
  autonomousControlLeaseActive = false;
  manualControlLeaseActive = false;
  manualTerrainModeActive = false;
  nozzleSweepActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationSequenceManual = false;
  irrigationZoneLatched = false;
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  manualMotorDir = CMD_STOP;
  setPump(false);
  driveMotor(CMD_BRAKE, 0.0);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  setLedOutput(false);
  playAudioTrack(7);
  Serial.printf(">> [EMERGENCY STOP] Latched by %s. Explicit reset required.\\n", source);
}

bool resetEmergencyStop() {
  if (!emergencyStopLatched) return false;
  setPump(false);
  driveMotor(CMD_BRAKE, 0.0);
  manualMotorDir = CMD_STOP;
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  autonomousControlLeaseActive = false;
  manualControlLeaseActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  emergencyStopLatched = false;
  setLedOutput(true);
  Serial.println(">> [SAFETY] Emergency stop reset. Robot remains stopped; start commands required.");
  return true;
}

void refreshManualControlLease() {
  lastManualControlRefresh = millis();
  manualControlLeaseActive = true;
}

void sendControlResponse(const char* command) {
  String json = "{\"success\":true,\"command\":\"";
  json += command;
  json += "\"}";
  server.send(200, "application/json", json);
}

void setManualMotorDirection(uint8_t direction, const char* command) {
  if (emergencyStopLatched) {
    server.send(423, "application/json", "{\"success\":false,\"message\":\"Emergency stop is latched\"}");
    return;
  }
  if (currentMode == MODE_AUTONOMOUS) {
    autonomousControlLeaseActive = false;
    irrigationSequenceActive = false;
    irrigationSequenceSpraying = false;
    irrigationZoneLatched = false;
    setPump(false);
  }
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  manualMotorDir = direction;
  driveMotor(direction, FULL_POWER);
  refreshManualControlLease();
  sendControlResponse(command);
}

void handleForward() {
  setManualMotorDirection(CMD_CW, "forward");
}

void handleReverse() {
  setManualMotorDirection(CMD_CCW, "reverse");
}

void handleSteerLeft() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before manual steering\"}");
    return;
  }
  manualTerrainModeActive = false;
  writeServo(STEER_SERVO_PIN, STEER_LEFT);
  sendControlResponse("steer_left");
}

void handleSteerRight() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before manual steering\"}");
    return;
  }
  manualTerrainModeActive = false;
  writeServo(STEER_SERVO_PIN, STEER_RIGHT);
  sendControlResponse("steer_right");
}

void handleSteerCenter() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before manual steering\"}");
    return;
  }
  manualTerrainModeActive = false;
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  sendControlResponse("steer_center");
}

void handleNozzleSweep() {
  handleFullNozzleSweep();
}

void configureManualNozzleSweep(int minimumAngle, int maximumAngle) {
  if (irrigationSequenceActive) {
    irrigationSequenceActive = false;
    irrigationSequenceSpraying = false;
    irrigationSequenceManual = false;
  }
  // Nozzle sweep is independent: preserve the operator's current drive and pump commands.
  nozzleSweepMinimumAngle = minimumAngle;
  nozzleSweepMaximumAngle = maximumAngle;
  nozzleSweepAngle = minimumAngle;
  nozzleSweepDirection = 1;
  lastNozzleSweepStep = 0;
  nozzleSweepActive = true;
  refreshManualControlLease();
}

void startManualNozzleSweep(int minimumAngle, int maximumAngle, const char* action) {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before manual nozzle sweep\"}");
    return;
  }
  configureManualNozzleSweep(minimumAngle, maximumAngle);
  sendControlResponse(action);
}

void handleLeftNozzleSweep() {
  startManualNozzleSweep(NOZZLE_MIN_SWEEP, 75, "nozzle_left_sweep_started");
}
void handleBackNozzleSweep() {
  startManualNozzleSweep(75, 105, "nozzle_back_sweep_started");
}
void handleRightNozzleSweep() {
  startManualNozzleSweep(105, NOZZLE_MAX_SWEEP, "nozzle_right_sweep_started");
}
void handleFullNozzleSweep() {
  startManualNozzleSweep(NOZZLE_MIN_SWEEP, NOZZLE_MAX_SWEEP, "nozzle_full_sweep_started");
}
void handleNozzleSweepStop() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before stopping manual nozzle sweep\"}");
    return;
  }
  nozzleSweepActive = false;
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  sendControlResponse("nozzle_sweep_stopped");
}

void handleSetIrrigationProfile() {
  if (currentMode == MODE_AUTONOMOUS || irrigationSequenceActive) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous irrigation before changing the profile\"}");
    return;
  }

  if (!server.hasArg("left") || !server.hasArg("back") ||
      !server.hasArg("right") || !server.hasArg("repeat")) {
    server.send(400, "application/json", "{\"success\":false,\"message\":\"Missing irrigation profile values\"}");
    return;
  }

  const int left = server.arg("left").toInt();
  const int back = server.arg("back").toInt();
  const int right = server.arg("right").toInt();
  const int repeat = server.arg("repeat").toInt();

  if (left < 0 || left > 60 || back < 0 || back > 60 ||
      right < 0 || right > 60 || repeat < 1 || repeat > 20 ||
      (left == 0 && back == 0 && right == 0)) {
    server.send(400, "application/json", "{\"success\":false,\"message\":\"Choose at least one direction; selected durations must be 1-60 seconds and unused directions must be 0\"}");
    return;
  }

  irrigationLeftSeconds = left;
  irrigationBackSeconds = back;
  irrigationRightSeconds = right;
  irrigationRepeatCount = repeat;
  sendControlResponse("irrigation_profile_applied");
}

void handleIrrigationStart() {
  if (currentMode == MODE_AUTONOMOUS || irrigationSequenceActive) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Manual irrigation requires idle Manual mode\"}");
    return;
  }
  if (irrigationLeftSeconds == 0 && irrigationBackSeconds == 0 && irrigationRightSeconds == 0) {
    server.send(400, "application/json", "{\"success\":false,\"message\":\"Apply a valid irrigation profile first\"}");
    return;
  }
  nozzleSweepActive = false;
  manualMotorDir = CMD_STOP;
  driveMotor(CMD_BRAKE, 0.0);
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  refreshManualControlLease();
  startIrrigationSequence(true);
  sendControlResponse("manual_irrigation_started");
}

void handleIrrigationStop() {
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationSequenceManual = false;
  setPump(false);
  manualControlLeaseActive = false;
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  sendControlResponse("manual_irrigation_stopped");
}

void handleStop() {
  manualTerrainModeActive = false;
  autonomousControlLeaseActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  nozzleSweepActive = false;
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  sendControlResponse("stop");
}

void handleAutonomousStart() {
  if (emergencyStopLatched) {
    server.send(423, "application/json", "{\"success\":false,\"message\":\"Emergency stop is latched\"}");
    return;
  }
  manualTerrainModeActive = false;
  nozzleSweepActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationZoneLatched = false;
  autonomousControlLeaseActive = true;
  lastAutonomousHeartbeat = millis();
  // Cancel any manual-control lease and start the existing autonomous loop.
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  currentMode = MODE_AUTONOMOUS;
  activeAutonomousMode = AUTO_GENERIC;
  sendControlResponse("autonomous_start");
  Serial.println(">> [MODE] AUTONOMOUS ENGAGED over Wi-Fi API");
}

void handleAutonomousStop() {
  autonomousControlLeaseActive = false;
  nozzleSweepActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationZoneLatched = false;
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  sendControlResponse("autonomous_stop");
  Serial.println(">> [MODE] AUTONOMOUS STOPPED over Wi-Fi API");
}

void handlePathwayStart() {
  if (emergencyStopLatched) {
    server.send(423, "application/json", "{\"success\":false,\"message\":\"Emergency stop is latched\"}");
    return;
  }
  handleAutonomousStart();
  activeAutonomousMode = AUTO_PATHWAY;
  Serial.println(">> [PATHWAY] Line-following mode selected.");
}

void handlePathwayStop() {
  handleAutonomousStop();
  Serial.println(">> [PATHWAY] Line-following mode stopped.");
}

void handleTerrainStart() {
  if (emergencyStopLatched) {
    server.send(423, "application/json", "{\"success\":false,\"message\":\"Emergency stop is latched\"}");
    return;
  }
  handleAutonomousStart();
  activeAutonomousMode = AUTO_TERRAIN;
  terrainBias = 0;
  terrainPatternIndex = 0;
  lastTerrainSteeringStep = 0;
  driveMotor(CMD_CW, FULL_POWER);
  Serial.println(">> [TERRAIN] Twist movement started.");
}

void setTerrainBias(int bias, const char* response) {
  if (currentMode != MODE_AUTONOMOUS || activeAutonomousMode != AUTO_TERRAIN) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Terrain mode is not active\"}");
    return;
  }
  terrainBias = constrain(bias, -1, 1);
  sendControlResponse(response);
}

void handleTerrainBiasLeft() { setTerrainBias(-1, "terrain_bias_left"); }
void handleTerrainBiasRight() { setTerrainBias(1, "terrain_bias_right"); }
void handleTerrainBiasCenter() { setTerrainBias(0, "terrain_bias_center"); }

void handleTerrainStop() {
  handleAutonomousStop();
  Serial.println(">> [TERRAIN] Twist movement stopped.");
}

// Manual Terrain Mode is independent of autonomous terrain navigation.
// It only oscillates the steering servo; propulsion remains user-controlled.
void handleManualTerrainStart() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before enabling manual terrain steering\"}");
    return;
  }
  manualTerrainModeActive = true;
  refreshManualControlLease();
  manualTerrainSteeringAngle = STEER_LEFT;
  lastManualTerrainSteeringStep = 0;
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  writeServo(STEER_SERVO_PIN, manualTerrainSteeringAngle);
  sendControlResponse("manual_terrain_started");
  Serial.println(">> [MANUAL TERRAIN] Steering oscillation started; propulsion remains manual.");
}

void handleManualTerrainStop() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Manual Terrain Mode cannot be stopped through manual controls while autonomous mode is active\"}");
    return;
  }
  manualTerrainModeActive = false;
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  sendControlResponse("manual_terrain_stopped");
  Serial.println(">> [MANUAL TERRAIN] Steering oscillation stopped and centered.");
}

void serviceManualTerrainMode() {
  if (!manualTerrainModeActive || currentMode != MODE_MANUAL) return;
  if (millis() - lastManualTerrainSteeringStep < TERRAIN_STEERING_INTERVAL_MS) return;
  lastManualTerrainSteeringStep = millis();
  manualTerrainSteeringAngle = manualTerrainSteeringAngle == STEER_LEFT ? STEER_RIGHT : STEER_LEFT;
  writeServo(STEER_SERVO_PIN, manualTerrainSteeringAngle);
}

void runTerrainStep() {
  if (millis() - lastTerrainSteeringStep < TERRAIN_STEERING_INTERVAL_MS) return;
  lastTerrainSteeringStep = millis();

  const int baseAngle = TERRAIN_STEERING_PATTERN[terrainPatternIndex];
  const int biasedAngle = constrain(
      baseAngle + (terrainBias * TERRAIN_BIAS_OFFSET_DEGREES),
      STEER_LEFT,
      STEER_RIGHT);
  writeServo(STEER_SERVO_PIN, biasedAngle);
  terrainPatternIndex = (terrainPatternIndex + 1) % 4;
}

void handlePumpOn() {
  if (emergencyStopLatched) {
    server.send(423, "application/json", "{\"success\":false,\"message\":\"Emergency stop is latched\"}");
    return;
  }
	if (currentMode == MODE_AUTONOMOUS) {
    autonomousControlLeaseActive = false;
    irrigationSequenceActive = false;
    irrigationSequenceSpraying = false;
    irrigationZoneLatched = false;
	manualMotorDir = CMD_STOP;
	driveMotor(CMD_BRAKE, 0.0);
  }
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  setPump(true);
  refreshManualControlLease();
  sendControlResponse("pump_on");
}

void handlePumpOff() {
  setPump(false);
  if (manualMotorDir == CMD_CW || manualMotorDir == CMD_CCW) {
	refreshManualControlLease();
  } else {
	manualControlLeaseActive = false;
  }
  sendControlResponse("pump_off");
}

void handleHeartbeat() {
  if (currentMode == MODE_MANUAL) {
    refreshManualControlLease();
  } else if (currentMode == MODE_AUTONOMOUS && autonomousControlLeaseActive) {
    lastAutonomousHeartbeat = millis();
  }
  sendControlResponse("heartbeat");
}


// ============================================================================
// MQTT COMMAND DISPATCHER
// The MQTT callback only enqueues payloads. All parsing and hardware actions
// happen here from loop(), preserving the ESP32 as the final safety authority.
// Initial remote command set is intentionally conservative.
static const uint8_t MQTT_RECENT_COMMAND_CAPACITY = 12;
static const unsigned long MQTT_MAX_MOVEMENT_TTL_MS = 1500;
static const unsigned long MQTT_MAX_STOP_TTL_MS = 5000;
String mqttRecentCommandIds[MQTT_RECENT_COMMAND_CAPACITY];
uint8_t mqttRecentCommandCursor = 0;
bool mqttClockSyncRequested = false;

bool mqttCommandWasProcessed(const String& commandId) {
  for (uint8_t i = 0; i < MQTT_RECENT_COMMAND_CAPACITY; ++i) {
    if (mqttRecentCommandIds[i] == commandId) return true;
  }
  return false;
}

void rememberMqttCommand(const String& commandId) {
  mqttRecentCommandIds[mqttRecentCommandCursor] = commandId;
  mqttRecentCommandCursor = (mqttRecentCommandCursor + 1) % MQTT_RECENT_COMMAND_CAPACITY;
}

void publishMqttCommandAck(const String& commandId, const char* status,
                           const char* code, const char* message) {
  if (!mqttManager.connected()) return;
  StaticJsonDocument<384> ack;
  ack["version"] = 1;
  ack["commandId"] = commandId;
  ack["robotId"] = mqttManager.robotId();
  ack["status"] = status;
  ack["code"] = code;
  ack["message"] = message;
  ack["timestamp"] = (uint32_t)time(nullptr);
  String output;
  serializeJson(ack, output);
  mqttManager.publishAck(output);
}

void serviceMqttCommands() {
  String raw;
  if (!mqttManager.takeCommand(raw)) return;

  StaticJsonDocument<768> document;
  const DeserializationError parseError = deserializeJson(document, raw);
  if (parseError || !document.is<JsonObject>()) {
    publishMqttCommandAck("", "rejected", "INVALID_JSON", "Command JSON is invalid");
    return;
  }

  const int version = document["version"] | 0;
  const String commandId = document["commandId"] | "";
  const String robotId = document["robotId"] | "";
  const String command = document["command"] | "";
  const uint32_t issuedAt = document["issuedAt"] | 0;
  const uint32_t ttlMs = document["ttlMs"] | 0;

  if (version != 1 || commandId.length() < 8 || commandId.length() > 64 ||
      robotId != mqttManager.robotId() || command.length() == 0) {
    publishMqttCommandAck(commandId, "rejected", "INVALID_ENVELOPE", "Command envelope is invalid");
    return;
  }

  if (mqttCommandWasProcessed(commandId)) {
    publishMqttCommandAck(commandId, "duplicate", "DUPLICATE_ID", "Command ID was already processed");
    return;
  }

  const time_t nowEpoch = time(nullptr);
  if (nowEpoch < 1700000000) {
    publishMqttCommandAck(commandId, "rejected", "CLOCK_UNSYNCED", "Robot clock is not synchronized");
    return;
  }

  const bool isStop = command == "stop" || command == "pump_off" ||
                      command == "autonomous_stop" || command == "pathway_stop" ||
                      command == "terrain_stop" || command == "irrigation_stop";
  const uint32_t maxTtl = isStop ? MQTT_MAX_STOP_TTL_MS : MQTT_MAX_MOVEMENT_TTL_MS;
  if (issuedAt == 0 || ttlMs == 0 || ttlMs > maxTtl ||
      nowEpoch < issuedAt || (uint64_t)(nowEpoch - issuedAt) * 1000ULL > ttlMs) {
    rememberMqttCommand(commandId);
    publishMqttCommandAck(commandId, "expired", "COMMAND_EXPIRED", "Command has expired or has an invalid lifetime");
    return;
  }

  // Mark before execution so QoS 1 redelivery cannot repeat a physical action.
  rememberMqttCommand(commandId);

  if (command == "stop") {
    handleStop();
  } else if (command == "forward" || command == "reverse") {
    if (emergencyStopLatched) {
      publishMqttCommandAck(commandId, "rejected", "ESTOP_LATCHED", "Emergency stop is latched");
      return;
    }
    setManualMotorDirection(command == "forward" ? CMD_CW : CMD_CCW,
                            command == "forward" ? "forward" : "reverse");
  } else if (command == "pump_off") {
    handlePumpOff();
  } else if (command == "pump_on") {
    if (emergencyStopLatched) {
      publishMqttCommandAck(commandId, "rejected", "ESTOP_LATCHED", "Emergency stop is latched");
      return;
    }
    handlePumpOn();
  } else if (command == "heartbeat") {
    handleHeartbeat();
  } else {
    publishMqttCommandAck(commandId, "rejected", "UNSUPPORTED_COMMAND", "Command is not enabled for remote execution yet");
    return;
  }

  publishMqttCommandAck(commandId, "executed", "OK", "Command accepted by robot");
}

void enforceAutonomousControlTimeout() {
  if (autonomousControlLeaseActive && currentMode == MODE_AUTONOMOUS &&
      millis() - lastAutonomousHeartbeat > AUTONOMOUS_CONTROL_TIMEOUT_MS) {
    autonomousControlLeaseActive = false;
    irrigationSequenceActive = false;
    irrigationSequenceSpraying = false;
    irrigationZoneLatched = false;
    currentMode = MODE_MANUAL;
    activeAutonomousMode = AUTO_NONE;
    manualMotorDir = CMD_STOP;
    driveMotor(CMD_BRAKE, 0.0);
    setPump(false);
    writeServo(STEER_SERVO_PIN, STEER_CENTER);
    writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
    Serial.println(">> Autonomous control lease expired; robot stopped safely.");
  }
}

void enforceManualControlTimeout() {
  if (manualControlLeaseActive && currentMode == MODE_MANUAL &&
	  millis() - lastManualControlRefresh > MANUAL_CONTROL_TIMEOUT_MS) {
	manualMotorDir = CMD_STOP;
	driveMotor(CMD_BRAKE, 0.0);
	setPump(false);
    if (irrigationSequenceManual) {
      irrigationSequenceActive = false;
      irrigationSequenceSpraying = false;
      irrigationSequenceManual = false;
      writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
    }
    nozzleSweepActive = false;
    writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
	manualTerrainModeActive = false;
    writeServo(STEER_SERVO_PIN, STEER_CENTER);
	manualControlLeaseActive = false;
	Serial.println(">> Manual control lease expired; motor, pump, and manual terrain steering stopped.");
  }
}

float getObstacleDistanceCM() {
  digitalWrite(OBSTACLE_TRIG_PIN, LOW);
  delayMicroseconds(2);
  digitalWrite(OBSTACLE_TRIG_PIN, HIGH);
  delayMicroseconds(15);
  digitalWrite(OBSTACLE_TRIG_PIN, LOW);

  long duration = pulseIn(OBSTACLE_ECHO_PIN, HIGH, 30000);
  if (duration == 0) return 999.0;
  return (duration * 0.0343) / 2.0;
}

void sweepNozzleOnce() {
  for (int a = NOZZLE_MIN_SWEEP; a <= NOZZLE_MAX_SWEEP; a += 15) {
	writeServo(NOZZLE_SERVO_PIN, a);
	delay(80);
  }
  for (int a = NOZZLE_MAX_SWEEP; a >= NOZZLE_MIN_SWEEP; a -= 15) {
	writeServo(NOZZLE_SERVO_PIN, a);
	delay(80);
  }
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
}

void startNozzleSweep() {
  // A manual nozzle sweep must not leave propulsion or irrigation running.
  autonomousControlLeaseActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationZoneLatched = false;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  currentMode = MODE_MANUAL;
  activeAutonomousMode = AUTO_NONE;
  nozzleSweepAngle = NOZZLE_MIN_SWEEP;
  nozzleSweepDirection = 1;
  lastNozzleSweepStep = 0;
  nozzleSweepActive = true;
}

void serviceNozzleSweep() {
  if (!nozzleSweepActive || millis() - lastNozzleSweepStep < 80) return;

  lastNozzleSweepStep = millis();
  writeServo(NOZZLE_SERVO_PIN, nozzleSweepAngle);

  if (nozzleSweepDirection > 0 && nozzleSweepAngle >= nozzleSweepMaximumAngle) {
    nozzleSweepAngle = nozzleSweepMaximumAngle;
    nozzleSweepDirection = -1;
  } else if (nozzleSweepDirection < 0 && nozzleSweepAngle <= nozzleSweepMinimumAngle) {
    nozzleSweepAngle = nozzleSweepMinimumAngle;
    nozzleSweepDirection = 1;
  } else {
    nozzleSweepAngle += nozzleSweepDirection * 5;
    if (nozzleSweepAngle > nozzleSweepMaximumAngle) nozzleSweepAngle = nozzleSweepMaximumAngle;
    if (nozzleSweepAngle < nozzleSweepMinimumAngle) nozzleSweepAngle = nozzleSweepMinimumAngle;
  }
}

// ============================================================================
// 6. AUTONOMOUS CONTROLLER
// ============================================================================
void beginIrrigationPhase() {
  setPump(false);
  irrigationSequenceSpraying = false;

  // Skip every direction whose duration is zero. The app sends zero for
  // directions that are not part of the selected sweep pattern.
  const int durations[] = { irrigationLeftSeconds, irrigationBackSeconds, irrigationRightSeconds };
  while (irrigationSequencePhase < 3 && durations[irrigationSequencePhase] == 0) {
    irrigationSequencePhase++;
  }

  if (irrigationSequencePhase >= 3) {
    irrigationSequencesCompleted++;
    if (irrigationSequencesCompleted >= irrigationRepeatCount) {
      irrigationSequenceActive = false;
      playAudioTrack(4); // Irrigation completed: /mp3/0004.mp3
      writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
      if (irrigationSequenceManual) {
        driveMotor(CMD_BRAKE, 0.0);
        manualMotorDir = CMD_STOP;
        manualControlLeaseActive = false;
        Serial.println(">> [MANUAL] Irrigation sweep completed; robot remains stopped.");
      } else {
        driveMotor(CMD_CW, FULL_POWER);
        Serial.println(">> [AUTO] Irrigation profile completed.");
      }
      irrigationSequenceManual = false;
      return;
    }
    irrigationSequencePhase = 0;
    while (irrigationSequencePhase < 3 && durations[irrigationSequencePhase] == 0) {
      irrigationSequencePhase++;
    }
    if (irrigationSequencePhase >= 3) {
      irrigationSequenceActive = false;
      setPump(false);
      driveMotor(CMD_BRAKE, 0.0);
      Serial.println(">> [SAFETY] Irrigation profile contains no enabled direction.");
      return;
    }
  }

  // The nozzle is mounted at the rear. Divide the expanded 30-150 degree
  // command range into left, rear, and right arcs of approximately equal width.
  // Each phase sweeps back and forth inside its own sector.
  const int sweepMinimums[] = { NOZZLE_MIN_SWEEP, NOZZLE_LEFT_SECTOR_MAX, NOZZLE_RIGHT_SECTOR_MIN };
  const int sweepMaximums[] = { NOZZLE_LEFT_SECTOR_MAX, NOZZLE_RIGHT_SECTOR_MIN, NOZZLE_MAX_SWEEP };
  nozzleSweepAngle = sweepMinimums[irrigationSequencePhase];
  nozzleSweepDirection = 1;
  lastNozzleSweepStep = millis();
  irrigationPhaseStartedAt = millis();
  writeServo(NOZZLE_SERVO_PIN, nozzleSweepAngle);
}

void startIrrigationSequence(bool manual) {
  if (emergencyStopLatched) {
    setPump(false);
    driveMotor(CMD_BRAKE, 0.0);
    Serial.println(">> [SAFETY] Irrigation start rejected: emergency stop is latched.");
    return;
  }
  irrigationSequenceManual = manual;
  playAudioTrack(3); // Irrigation started: /mp3/0003.mp3
  Serial.println(manual ? "\n>> [MANUAL] Running selected irrigation sweep..." : "\n>> [AUTO] Irrigation zone reached. Running selected profile...");
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  irrigationSequenceActive = true;
  irrigationSequencePhase = 0;
  irrigationSequencesCompleted = 0;
  beginIrrigationPhase();
}

void serviceIrrigationSequence() {
  if (!irrigationSequenceActive) return;

  const float obstacleDistance = getObstacleDistanceCM();
  serviceObstacleAudioAlert(obstacleDistance);
  if (obstacleDistance < OBSTACLE_STOP_CM) {
    irrigationSequenceActive = false;
    irrigationSequenceSpraying = false;
    irrigationSequenceManual = false;
    setPump(false);
    driveMotor(CMD_BRAKE, 0.0);
    Serial.println(">> [SAFETY] Obstacle detected during irrigation. Sequence aborted.");
    return;
  }

  const unsigned long now = millis();
  if (!irrigationSequenceSpraying) {
    if (now - irrigationPhaseStartedAt >= 300) {
      setPump(true);
      irrigationSequenceSpraying = true;
      irrigationPhaseStartedAt = now;
      lastNozzleSweepStep = now;
    }
    return;
  }

  // Sweep the nozzle continuously within the active directional arc.
  if (now - lastNozzleSweepStep >= 80) {
    lastNozzleSweepStep = now;
    const int sweepMinimums[] = { NOZZLE_MIN_SWEEP, NOZZLE_LEFT_SECTOR_MAX, NOZZLE_RIGHT_SECTOR_MIN };
    const int sweepMaximums[] = { NOZZLE_LEFT_SECTOR_MAX, NOZZLE_RIGHT_SECTOR_MIN, NOZZLE_MAX_SWEEP };
    const int low = sweepMinimums[irrigationSequencePhase];
    const int high = sweepMaximums[irrigationSequencePhase];
    nozzleSweepAngle += nozzleSweepDirection * 5;
    if (nozzleSweepAngle >= high) {
      nozzleSweepAngle = high;
      nozzleSweepDirection = -1;
    } else if (nozzleSweepAngle <= low) {
      nozzleSweepAngle = low;
      nozzleSweepDirection = 1;
    }
    writeServo(NOZZLE_SERVO_PIN, nozzleSweepAngle);
  }

  const int durations[] = { irrigationLeftSeconds, irrigationBackSeconds, irrigationRightSeconds };
  if (now - irrigationPhaseStartedAt < (unsigned long)durations[irrigationSequencePhase] * 1000UL)
    return;

  setPump(false);
  irrigationSequenceSpraying = false;
  irrigationSequencePhase++;
  beginIrrigationPhase();
}

void executeIrrigationZone() {
  if (!irrigationSequenceActive)
    startIrrigationSequence();
}

void runAutonomousStep() {
  if (irrigationSequenceActive) {
    serviceIrrigationSequence();
    return;
  }

  const float obstacleDistance = getObstacleDistanceCM();
  serviceObstacleAudioAlert(obstacleDistance);
  if (obstacleDistance < OBSTACLE_STOP_CM) {
	driveMotor(CMD_BRAKE, 0.0);
	setPump(false);
	return;
  }

  int leftLine = digitalRead(LINE_LEFT_PIN);
  int rightLine = digitalRead(LINE_RIGHT_PIN);

  if (!(leftLine == HIGH && rightLine == HIGH))
    irrigationZoneLatched = false;

  if (leftLine == LOW && rightLine == LOW) {
	writeServo(STEER_SERVO_PIN, STEER_CENTER);
	driveMotor(CMD_CW, FULL_POWER);
  } else if (leftLine == HIGH && rightLine == LOW) {
	writeServo(STEER_SERVO_PIN, STEER_LEFT);
	driveMotor(CMD_CW, FULL_POWER);
  } else if (leftLine == LOW && rightLine == HIGH) {
	writeServo(STEER_SERVO_PIN, STEER_RIGHT);
	driveMotor(CMD_CW, FULL_POWER);
  } else if (leftLine == HIGH && rightLine == HIGH) {
    if (!irrigationZoneLatched) {
      irrigationZoneLatched = true;
      executeIrrigationZone();
    }
  }
}

// ============================================================================
// 7. SERIAL COMMAND DISPATCHER
// ============================================================================
void printMenu() {
  Serial.println("\n==============================================");
  Serial.println("   A-RIGATOR COMMAND CONSOLE (100% POWER)     ");
  Serial.println("==============================================");
  Serial.println("  w : Forward (100% Power)  |  x : Reverse (100% Power)");
  Serial.println("  s : Stop (Coast)          |  b : Hard Brake");
  Serial.println("  a : Steer Left (45 deg)   |  d : Steer Right (135 deg)");
  Serial.println("  c : Steer Center (90 deg)");
  Serial.println("  p : Toggle Water Pump     |  r : Reset Motor Shield");
  Serial.println("  q : Forget Router Wi-Fi   |  m : Toggle Autonomous Mode");
  Serial.println("  t : Read Sensors          |  STATUS : Print system status");
  Serial.println("  L 5 : Left sweep, 5 sec   |  R 5 : Right sweep, 5 sec");
  Serial.println("  B 5 : Back sweep, 5 sec   |  D 5 : Both sides, 5 sec each");
  Serial.println("  F 5 2 : Full sweep, 5 sec each, repeat twice");
  Serial.println("  STOP : Stop irrigation and brake robot");
  Serial.println("==============================================\n");
}

void executeSerialLine(String line) {
  line.trim();
  line.toUpperCase();
  if (line == "ESTOP" || line == "E-STOP") {
    triggerEmergencyStop("Serial console");
    return;
  }
  if (line == "RESET-ESTOP") {
    if (!resetEmergencyStop()) Serial.println(">> [SAFETY] No emergency stop is currently latched.");
    return;
  }
  if (line == "STOP") {
    manualTerrainModeActive = false;
    irrigationSequenceActive = false;
    irrigationSequenceSpraying = false;
    irrigationSequenceManual = false;
    manualControlLeaseActive = false;
    setPump(false);
    driveMotor(CMD_BRAKE, 0.0);
    manualMotorDir = CMD_STOP;
    writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
    Serial.println(">> [MANUAL] Irrigation stopped; robot remains braked.");
    return;
  }
  if (line == "STATUS") {
    Serial.printf(">> [STATUS] Mode: %s | Pump: %s | Irrigation: %s | STA: %s | IP: %s\n",
      currentMode == MODE_AUTONOMOUS ? "AUTONOMOUS" : "MANUAL", pumpState ? "ON" : "OFF",
      irrigationSequenceActive ? "RUNNING" : "IDLE", WiFi.status() == WL_CONNECTED ? "CONNECTED" : "DISCONNECTED",
      WiFi.status() == WL_CONNECTED ? WiFi.localIP().toString().c_str() : "none");
    return;
  }
  const int split = line.indexOf(' ');
  if (split < 1) { Serial.println(">> [SERIAL] Use L/R/B/D/F <seconds> [repeat], STOP, or STATUS."); return; }
  const String pattern = line.substring(0, split);
  String args = line.substring(split + 1); args.trim();
  const int second = args.indexOf(' ');
  const int seconds = (second < 0 ? args : args.substring(0, second)).toInt();
  const int repeats = second < 0 ? 1 : args.substring(second + 1).toInt();
  if (seconds < 1 || seconds > 60 || repeats < 1 || repeats > 20) {
    Serial.println(">> [SERIAL] Duration must be 1-60 seconds; repeat must be 1-20."); return;
  }
  if (pattern != "L" && pattern != "R" && pattern != "B" && pattern != "D" && pattern != "F") {
    Serial.println(">> [SERIAL] Unknown pattern. Use L, R, B, D, or F."); return;
  }
  irrigationLeftSeconds = (pattern == "L" || pattern == "D" || pattern == "F") ? seconds : 0;
  irrigationBackSeconds = (pattern == "B" || pattern == "F") ? seconds : 0;
  irrigationRightSeconds = (pattern == "R" || pattern == "D" || pattern == "F") ? seconds : 0;
  irrigationRepeatCount = repeats;
  manualControlLeaseActive = false;
  Serial.printf(">> [MANUAL] Pattern %s, %d sec per selected direction, %d pass(es).\n", pattern.c_str(), seconds, repeats);
  startIrrigationSequence(true);
}
void executeCommand(char cmd) {
  if (emergencyStopLatched) {
    Serial.println(">> [SAFETY] Command rejected: emergency stop is latched. Use RESET-ESTOP first.");
    return;
  }
  switch (cmd) {
	case 'w': case 'W':
	  currentMode = MODE_MANUAL;
	  manualMotorDir = CMD_CW;
	  driveMotor(CMD_CW, FULL_POWER);
	  Serial.println(">> [CMD] Motor: FORWARD (100% Power)");
	  break;

	case 'x': case 'X':
	  currentMode = MODE_MANUAL;
	  manualMotorDir = CMD_CCW;
	  driveMotor(CMD_CCW, FULL_POWER);
	  Serial.println(">> [CMD] Motor: REVERSE (100% Power)");
	  break;

	case 's': case 'S':
	  currentMode = MODE_MANUAL;
	  manualMotorDir = CMD_STOP;
	  driveMotor(CMD_STOP, 0.0);
	  Serial.println(">> [CMD] Motor: STOP");
	  break;

	case 'b': case 'B':
	  currentMode = MODE_MANUAL;
	  manualMotorDir = CMD_BRAKE;
	  driveMotor(CMD_BRAKE, 0.0);
	  Serial.println(">> [CMD] Motor: BRAKE");
	  break;

	case 'a': case 'A':
      manualTerrainModeActive = false;
	  Serial.println(">> [CMD] Steer: LEFT (45 deg)");
	  writeServo(STEER_SERVO_PIN, STEER_LEFT);
	  break;

	case 'c': case 'C':
      manualTerrainModeActive = false;
	  Serial.println(">> [CMD] Steer: CENTER (90 deg)");
	  writeServo(STEER_SERVO_PIN, STEER_CENTER);
	  break;

	case 'd': case 'D':
      manualTerrainModeActive = false;
	  Serial.println(">> [CMD] Steer: RIGHT (135 deg)");
	  writeServo(STEER_SERVO_PIN, STEER_RIGHT);
	  break;

	case 'p': case 'P':
	  setPump(!pumpState);
	  Serial.printf(">> [CMD] Water Pump: %s\n", pumpState ? "ON" : "OFF");
	  break;

	case 'r': case 'R':
	  resetMotorShield();
	  break;

	case 'n': case 'N':
	  Serial.println(">> [CMD] Sweeping Nozzle...");
      if (currentMode != MODE_AUTONOMOUS)
        configureManualNozzleSweep(NOZZLE_MIN_SWEEP, NOZZLE_MAX_SWEEP);
	  break;

	case 'm': case 'M':
      manualTerrainModeActive = false;
	  if (currentMode == MODE_MANUAL) {
		currentMode = MODE_AUTONOMOUS;
		manualMotorDir = CMD_STOP;
		Serial.println(">> [MODE] AUTONOMOUS ENGAGED");
	  } else {
		currentMode = MODE_MANUAL;
        activeAutonomousMode = AUTO_NONE;
        autonomousControlLeaseActive = false;
		manualMotorDir = CMD_STOP;
		driveMotor(CMD_BRAKE, 0.0);
		setPump(false);
		writeServo(STEER_SERVO_PIN, STEER_CENTER);
        writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
		Serial.println(">> [MODE] MANUAL CONTROL ENGAGED");
	  }
	  break;

	case 'q': case 'Q':
	  if (forgetSavedWifiNetwork()) {
	    Serial.println(">> [CMD] Router Wi-Fi reset complete. AP password preserved.");
	  } else {
	    Serial.println(">> [CMD] Router Wi-Fi reset failed.");
	  }
	  break;

	case 't': case 'T':
	  Serial.printf(">> [SENSORS] Sonar: %.1f cm | Left Line: %d | Right Line: %d | Pump: %s\n",
					getObstacleDistanceCM(), digitalRead(LINE_LEFT_PIN), digitalRead(LINE_RIGHT_PIN), pumpState ? "ON" : "OFF");
	  break;

	default:
	  printMenu();
	  break;
  }
}

// ============================================================================
// 8. SETUP & LOOP
// ============================================================================
void setup() {
  Serial.begin(115200);
  delay(BOOT_POWER_SETTLE_MS);

  pinMode(LINE_LEFT_PIN, INPUT);
  pinMode(LINE_RIGHT_PIN, INPUT);
  pinMode(OBSTACLE_TRIG_PIN, OUTPUT);
  pinMode(OBSTACLE_ECHO_PIN, INPUT);

  pinMode(PUMP_RELAY_PIN, OUTPUT);
  digitalWrite(PUMP_RELAY_PIN, RELAY_OFF);

  pinMode(LED_MOSFET_PIN, OUTPUT);
  digitalWrite(LED_MOSFET_PIN, LOW);
  analogReadResolution(12);
  analogSetPinAttenuation(BATTERY_ADC_PIN, ADC_11db);
  initializeAudio();
  playAudioTrack(1); // Startup: /mp3/0001.mp3

  pinMode(STEER_SERVO_PIN, OUTPUT);
  pinMode(NOZZLE_SERVO_PIN, OUTPUT);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);

  pinMode(SHIELD_RST_PIN, OUTPUT);
  digitalWrite(SHIELD_RST_PIN, LOW);

  if (apConfigStore.begin()) {
    apConfigStoreAvailable = true;
  } else {
    Serial.println(">> Could not open AP settings storage. AP startup is blocked for safety.");
  }

  if (wifiCredentialStore.begin()) {
    savedWifiCredentialsAvailable = wifiCredentialStore.load(savedWifiCredentials);
    Serial.println(savedWifiCredentialsAvailable
      ? ">> Saved Wi-Fi credentials found in NVS."
      : ">> No saved Wi-Fi credentials. Local setup AP only.");
  } else {
    Serial.println(">> Could not open Wi-Fi credential storage. Continuing with local setup AP.");
  }


  startBleDiscovery();
  if (apConfigStoreAvailable) {
    apPassword = apConfigStore.load();
    Serial.println(">> AP password loaded from NVS or generated for this device.");
  }
  startWifiApi();

  // MQTT is optional for local operation. Without a provisioned per-device
  // profile, the local AP/HTTP API remains fully available.
  if (mqttConfigStore.begin()) {
    mqttConfigStoreAvailable = true;
    mqttConfigured = mqttConfigStore.load(mqttConfig);
    if (mqttConfigured && mqttManager.begin(mqttConfig)) {
      Serial.println(">> Per-device MQTT configuration loaded. Waiting for station Wi-Fi.");
    } else {
      mqttConfigured = false;
      Serial.println(">> MQTT is not provisioned. Local control remains available.");
    }
  } else {
    Serial.println(">> MQTT NVS unavailable. Local control remains available.");
  }

  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(10000);
  delay(100);
  initializeMotorShield();

  driveMotor(CMD_STOP, 0.0);
  setLedOutput(true);
  playAudioTrack(2); // Ready: /mp3/0002.mp3
  printMenu();
}

void loop() {
  readBatteryVoltage();
  server.handleClient();
  mqttManager.loop();
  serviceMqttCommands();
  serviceBleWifiScan();
  serviceBleWifiProvisioning();
  serviceBleApPasswordChange();
  serviceBleControlCommands();
  publishBleTelemetryIfDue();

  if (wifiForgetDisconnectPending &&
      (int32_t)(millis() - wifiForgetDisconnectAt) >= 0) {
    wifiForgetDisconnectPending = false;
    WiFi.disconnect(false, true);
  }

  // Report station connectivity transitions without exposing the password.
  const bool stationConnected = WiFi.status() == WL_CONNECTED;
  if (stationConnected && !mqttClockSyncRequested) {
    configTime(0, 0, "pool.ntp.org", "time.google.com");
    mqttClockSyncRequested = true;
  }
  if (stationConnected && !stationWasConnected) {
    if (!mdnsStarted) {
      mdnsStarted = MDNS.begin("arigator");
      if (mdnsStarted) MDNS.addService("http", "tcp", 80);
    }
    Serial.printf(">> Wi-Fi connected. Robot network IP: %s (arigator.local)\\n",
                  WiFi.localIP().toString().c_str());
  } else if (!stationConnected && stationWasConnected) {
    if (mdnsStarted) MDNS.end();
    mdnsStarted = false;
    Serial.println(">> Saved Wi-Fi disconnected. ESP32 will attempt to reconnect.");
  }
  stationWasConnected = stationConnected;

  enforceManualControlTimeout();
  enforceAutonomousControlTimeout();
  serviceNozzleSweep();
  serviceManualTerrainMode();
  if (irrigationSequenceActive && irrigationSequenceManual && currentMode == MODE_MANUAL)
    serviceIrrigationSequence();
  refreshMotorShieldKeepAlive();

  while (Serial.available() > 0) {
    const char ch = (char)Serial.read();
    if (ch == '\r') continue;
    if (ch == '\n') {
      serialCommandBuffer.trim();
      if (serialCommandBuffer.length() == 1) executeCommand(serialCommandBuffer[0]);
      else if (serialCommandBuffer.length() > 1) executeSerialLine(serialCommandBuffer);
      serialCommandBuffer = "";
    } else if (serialCommandBuffer.length() < 48) {
      serialCommandBuffer += ch;
    } else {
      serialCommandBuffer = "";
      Serial.println(">> [SERIAL] Command too long; input cleared.");
    }
  }

  if (currentMode == MODE_MANUAL) {
	if (millis() - lastMotorRefresh >= 100) {
	  lastMotorRefresh = millis();
	  if (manualMotorDir == CMD_CW || manualMotorDir == CMD_CCW) {
		driveMotor(manualMotorDir, FULL_POWER);
	  }
	}
  } else if (currentMode == MODE_AUTONOMOUS) {
    if (activeAutonomousMode == AUTO_TERRAIN) runTerrainStep();
    else runAutonomousStep();
  }
}
