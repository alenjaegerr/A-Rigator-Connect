#include <Wire.h>
#include <WiFi.h>
#include <WebServer.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLESecurity.h>
#include <ESPmDNS.h>
#include "RobotWifiConfig.h"
#include "RobotMqttConfig.h"

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
static const char* BLE_HARDWARE_ID_CHARACTERISTIC_UUID = "7a1c0008-8f2d-4b6a-9c31-4a5249474154";
BLEServer* bleServer = nullptr;
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

	String json = "{\"success\":true,\"state\":\"";
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
  handleAutonomousStart();
  activeAutonomousMode = AUTO_PATHWAY;
  Serial.println(">> [PATHWAY] Line-following mode selected.");
}

void handlePathwayStop() {
  handleAutonomousStop();
  Serial.println(">> [PATHWAY] Line-following mode stopped.");
}

void handleTerrainStart() {
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

  // The nozzle is mounted at the rear. Preserve the currently calibrated
  // 45-135 degree operating envelope and divide it into left, rear, and right
  // arcs. Each phase sweeps back and forth inside its own sector.
  const int sweepMinimums[] = { NOZZLE_MIN_SWEEP, 75, 105 };
  const int sweepMaximums[] = { 75, 105, NOZZLE_MAX_SWEEP };
  nozzleSweepAngle = sweepMinimums[irrigationSequencePhase];
  nozzleSweepDirection = 1;
  lastNozzleSweepStep = millis();
  irrigationPhaseStartedAt = millis();
  writeServo(NOZZLE_SERVO_PIN, nozzleSweepAngle);
}

void startIrrigationSequence(bool manual) {
  irrigationSequenceManual = manual;
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

  if (getObstacleDistanceCM() < OBSTACLE_STOP_CM) {
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
    const int sweepMinimums[] = { NOZZLE_MIN_SWEEP, 75, 105 };
    const int sweepMaximums[] = { 75, 105, NOZZLE_MAX_SWEEP };
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

  if (getObstacleDistanceCM() < OBSTACLE_STOP_CM) {
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
