#include <Wire.h>
#include <WiFi.h>
#include <WebServer.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLESecurity.h>
#include <ESPmDNS.h>
#include "RobotWifiConfig.h"

// ============================================================================
// 1. WI-FI STATUS API
// ============================================================================
// AP credentials are device-specific. The password is generated once and
// persisted in NVS; never print it to the serial console or commit it to Git.
static const char* WIFI_SSID = "A-Rigator";
String apPassword;
RobotApConfigStore apConfigStore;
bool apConfigStoreAvailable = false;
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
BLEServer* bleServer = nullptr;
BLEService* bleProvisioningService = nullptr;
BLECharacteristic* bleDiscoveryCharacteristic = nullptr;
BLECharacteristic* bleWifiScanCharacteristic = nullptr;
BLECharacteristic* bleWifiCredentialCharacteristic = nullptr;
BLECharacteristic* bleWifiStatusCharacteristic = nullptr;
BLECharacteristic* bleApConfigCharacteristic = nullptr;
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
void serviceNozzleSweep();
void startNozzleSweep();
void handleStop();
void handleAutonomousStart();
void handleAutonomousStop();
void handlePumpOn();
void handlePumpOff();
void handleHeartbeat();
void handleResetShield();
void serviceBleWifiProvisioning();
void serviceBleApPasswordChange();
float getObstacleDistanceCM();

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

  BLEAdvertising* advertising = BLEDevice::getAdvertising();
  advertising->addServiceUUID(BLE_PROVISIONING_SERVICE_UUID);
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

#define CHASSIS_BTN_PIN     15    // Twist Car Top Button (D15)

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

#define NOZZLE_CENTER       90
#define NOZZLE_MIN_SWEEP    45
#define NOZZLE_MAX_SWEEP    135

#define OBSTACLE_STOP_CM    25

// ============================================================================
// 4. STATE VARIABLES
// ============================================================================
enum RobotMode {
  MODE_MANUAL,
  MODE_AUTONOMOUS
};

RobotMode currentMode = MODE_MANUAL;
uint8_t manualMotorDir = CMD_STOP;
bool pumpState = false;
bool nozzleSweepActive = false;
int nozzleSweepAngle = NOZZLE_MIN_SWEEP;
int nozzleSweepDirection = 1;
unsigned long lastNozzleSweepStep = 0;
unsigned long lastMotorRefresh = 0;
unsigned long lastManualControlRefresh = 0;
bool manualControlLeaseActive = false;
bool controlsEnabled = false;

// Forget only the saved router profile. Preserve the A-Rigator AP password and
// keep the local setup hotspot available so the app can provision another network.
bool forgetSavedWifiNetwork(bool disconnectNow = true) {
  setPump(false);
  currentMode = MODE_MANUAL;
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

	String json = "{\"success\":true,\"state\":\"";
  json += state;
  json += "\",\"mode\":\"";
  json += mode;
  json += "\",\"pump\":";
  json += pumpState ? "true" : "false";
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
  if (controlsEnabled) {
	server.on("/api/control/forward", HTTP_POST, handleForward);
	server.on("/api/control/reverse", HTTP_POST, handleReverse);
	server.on("/api/control/steer/left", HTTP_POST, handleSteerLeft);
	server.on("/api/control/steer/right", HTTP_POST, handleSteerRight);
	server.on("/api/control/steer/center", HTTP_POST, handleSteerCenter);
	server.on("/api/control/nozzle/sweep", HTTP_POST, handleNozzleSweep);
	server.on("/api/control/stop", HTTP_POST, handleStop);
	server.on("/api/control/autonomous/start", HTTP_POST, handleAutonomousStart);
	server.on("/api/control/autonomous/stop", HTTP_POST, handleAutonomousStop);
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

void setPump(bool enable) {
  pumpState = enable;
  digitalWrite(PUMP_RELAY_PIN, enable ? RELAY_ON : RELAY_OFF);
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
  currentMode = MODE_MANUAL;
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
  writeServo(STEER_SERVO_PIN, STEER_LEFT);
  sendControlResponse("steer_left");
}

void handleSteerRight() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before manual steering\"}");
    return;
  }
  writeServo(STEER_SERVO_PIN, STEER_RIGHT);
  sendControlResponse("steer_right");
}

void handleSteerCenter() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\"success\":false,\"message\":\"Stop autonomous mode before manual steering\"}");
    return;
  }
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  sendControlResponse("steer_center");
}

void handleNozzleSweep() {
  if (currentMode == MODE_AUTONOMOUS) {
    server.send(409, "application/json", "{\\"success\\":false,\\"message\\":\\"Stop autonomous mode before manual nozzle sweep\\"}");
    return;
  }
  startNozzleSweep();
  sendControlResponse("nozzle_sweep_started");
}

void handleStop() {
  nozzleSweepActive = false;
  currentMode = MODE_MANUAL;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  sendControlResponse("stop");
}

void handleAutonomousStart() {
  nozzleSweepActive = false;
  // Cancel any manual-control lease and start the existing autonomous loop.
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  currentMode = MODE_AUTONOMOUS;
  sendControlResponse("autonomous_start");
  Serial.println(">> [MODE] AUTONOMOUS ENGAGED over Wi-Fi API");
}

void handleAutonomousStop() {
  nozzleSweepActive = false;
  currentMode = MODE_MANUAL;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  sendControlResponse("autonomous_stop");
  Serial.println(">> [MODE] AUTONOMOUS STOPPED over Wi-Fi API");
}

void handlePumpOn() {
	if (currentMode == MODE_AUTONOMOUS) {
	manualMotorDir = CMD_STOP;
	driveMotor(CMD_BRAKE, 0.0);
  }
  currentMode = MODE_MANUAL;
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
  }
  sendControlResponse("heartbeat");
}

void enforceManualControlTimeout() {
  if (manualControlLeaseActive && currentMode == MODE_MANUAL &&
	  millis() - lastManualControlRefresh > MANUAL_CONTROL_TIMEOUT_MS) {
	manualMotorDir = CMD_STOP;
	driveMotor(CMD_BRAKE, 0.0);
	setPump(false);
	manualControlLeaseActive = false;
	Serial.println(">> Manual control lease expired; motor stopped and pump disabled.");
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
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  currentMode = MODE_MANUAL;
  nozzleSweepAngle = NOZZLE_MIN_SWEEP;
  nozzleSweepDirection = 1;
  lastNozzleSweepStep = 0;
  nozzleSweepActive = true;
}

void serviceNozzleSweep() {
  if (!nozzleSweepActive || millis() - lastNozzleSweepStep < 80) return;

  lastNozzleSweepStep = millis();
  writeServo(NOZZLE_SERVO_PIN, nozzleSweepAngle);

  if (nozzleSweepDirection > 0 && nozzleSweepAngle >= NOZZLE_MAX_SWEEP) {
    nozzleSweepDirection = -1;
  } else if (nozzleSweepDirection < 0 && nozzleSweepAngle <= NOZZLE_MIN_SWEEP) {
    nozzleSweepActive = false;
    writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
    Serial.println(">> [MANUAL] Nozzle sweep complete.");
    return;
  }

  nozzleSweepAngle += nozzleSweepDirection * 15;
}

// ============================================================================
// 6. AUTONOMOUS CONTROLLER
// ============================================================================
void executeIrrigationZone() {
  Serial.println("\n>> [AUTO] Irrigation Zone Reached! Spraying...");
  driveMotor(CMD_BRAKE, 0.0);
  delay(500);

  setPump(true);
  sweepNozzleOnce();
  setPump(false);
  delay(500);
}

void runAutonomousStep() {
  if (getObstacleDistanceCM() < OBSTACLE_STOP_CM) {
	driveMotor(CMD_BRAKE, 0.0);
	setPump(false);
	return;
  }

  int leftLine = digitalRead(LINE_LEFT_PIN);
  int rightLine = digitalRead(LINE_RIGHT_PIN);

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
	executeIrrigationZone();
	writeServo(STEER_SERVO_PIN, STEER_CENTER);
	driveMotor(CMD_CW, FULL_POWER);
	delay(600);
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
  Serial.println("  p : Toggle Water Pump     |  n : Sweep Nozzle");
	Serial.println("  r : Reset Motor Shield");
	Serial.println("  q : Forget Saved Router Wi-Fi");
  Serial.println("  m : Toggle Autonomous Mode|  t : Read Sensors");
  Serial.println("==============================================\n");
}

void executeCommand(char cmd) {
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
	  Serial.println(">> [CMD] Steer: LEFT (45 deg)");
	  writeServo(STEER_SERVO_PIN, STEER_LEFT);
	  break;

	case 'c': case 'C':
	  Serial.println(">> [CMD] Steer: CENTER (90 deg)");
	  writeServo(STEER_SERVO_PIN, STEER_CENTER);
	  break;

	case 'd': case 'D':
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
	  startNozzleSweep();
	  break;

	case 'm': case 'M':
	  if (currentMode == MODE_MANUAL) {
		currentMode = MODE_AUTONOMOUS;
		manualMotorDir = CMD_STOP;
		Serial.println(">> [MODE] AUTONOMOUS ENGAGED");
	  } else {
		currentMode = MODE_MANUAL;
		manualMotorDir = CMD_STOP;
		driveMotor(CMD_BRAKE, 0.0);
		setPump(false);
		writeServo(STEER_SERVO_PIN, STEER_CENTER);
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

  // Initialize BLE before first-boot password generation so the ESP32 hardware
  // RNG has an active radio entropy source.
  startBleDiscovery();
  if (apConfigStoreAvailable) {
    apPassword = apConfigStore.load();
    Serial.println(">> AP password loaded from NVS or generated for this device.");
  }
  startWifiApi();

  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(10000);
  delay(100);
  initializeMotorShield();

  driveMotor(CMD_STOP, 0.0);
  printMenu();
}

void loop() {
  server.handleClient();
  serviceBleWifiScan();
  serviceBleWifiProvisioning();
  serviceBleApPasswordChange();

  if (wifiForgetDisconnectPending &&
      (int32_t)(millis() - wifiForgetDisconnectAt) >= 0) {
    wifiForgetDisconnectPending = false;
    WiFi.disconnect(false, true);
  }

  // Report station connectivity transitions without exposing the password.
  const bool stationConnected = WiFi.status() == WL_CONNECTED;
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
  serviceNozzleSweep();
  refreshMotorShieldKeepAlive();

  if (Serial.available() > 0) {
	char c = Serial.read();
	if (c != '\n' && c != '\r' && c != ' ') {
	  executeCommand(c);
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
	runAutonomousStep();
  }
}
