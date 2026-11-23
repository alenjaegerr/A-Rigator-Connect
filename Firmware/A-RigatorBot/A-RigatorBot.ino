#include <Wire.h>
#include <ArduinoJson.h>
#include <time.h>

// Firmware version reported through BLE telemetry and MQTT.
#define ARIGATOR_FIRMWARE_VERSION "0.1.0"

#include <HardwareSerial.h>
#include <DFRobotDFPlayerMini.h>
#include <WiFi.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLESecurity.h>
#include "RobotWifiConfig.h"
#include "RobotMqttConfig.h"
#include "RobotMqttManager.h"

// ============================================================================
// 1. WI-FI STATION + MQTT
// ============================================================================
RobotMqttConfigStore mqttConfigStore;
RobotMqttConfig mqttConfig;
RobotMqttManager mqttManager;
bool mqttConfigStoreAvailable = false;
bool mqttConfigured = false;
// BLE manual actions persist until stop or BLE disconnect. Remote manual control
// uses a short session lease because the ESP32's broker connection can outlive the phone.
static const unsigned long REMOTE_MANUAL_CONTROL_TIMEOUT_MS = 1500;
volatile bool bleClientDisconnectedPending = false;
bool manualControlViaMqtt = false;
static const unsigned long BOOT_POWER_SETTLE_MS = 1500;
RobotWifiCredentialStore wifiCredentialStore;
RobotWifiCredentials savedWifiCredentials;
bool savedWifiCredentialsAvailable = false;
bool stationWasConnected = false;

// BLE credential writes require an encrypted, bonded link. Just Works does
// not provide MITM protection; production pairing needs physical presence or
// a device-identity mechanism.
static const char* BLE_DEVICE_NAME = "A-Rigator";
static const char* BLE_PROVISIONING_SERVICE_UUID = "7a1c0001-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_DISCOVERY_CHARACTERISTIC_UUID = "7a1c0002-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_SCAN_CHARACTERISTIC_UUID = "7a1c0003-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_CREDENTIAL_CHARACTERISTIC_UUID = "7a1c0004-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_STATUS_CHARACTERISTIC_UUID = "7a1c0005-8f2d-4b6a-9c31-4a5249474154";
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
String pendingBleControlCommand;
volatile bool bleControlCommandPending = false;
bool bleCommandExecution = false;
bool bleCommandRejected = false;
unsigned long lastBleTelemetryAt = 0;
BLEService* bleProvisioningService = nullptr;
BLECharacteristic* bleDiscoveryCharacteristic = nullptr;
BLECharacteristic* bleWifiScanCharacteristic = nullptr;
BLECharacteristic* bleWifiCredentialCharacteristic = nullptr;
BLECharacteristic* bleWifiStatusCharacteristic = nullptr;
BLECharacteristic* bleHardwareIdCharacteristic = nullptr;
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
;

String bleControlAssembly;
int bleControlExpectedLength = -1;

class RobotBleServerCallbacks : public BLEServerCallbacks {
  void onDisconnect(BLEServer* server) override {
    // ESP32 BLE stops advertising while a central is connected. Restart so
    // the app can reconnect after leaving range or closing the session.
    bleClientDisconnectedPending = true;
    BLEDevice::startAdvertising();
    Serial.println(">> BLE client disconnected; advertising restarted.");
  }
};

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

    // Keep heap-backed String operations outside FreeRTOS critical sections.
    if (!bleControlCommandPending) {
      pendingBleControlCommand = bleControlAssembly;
      bleControlCommandPending = true;
    }
    bleControlExpectedLength = -1;
    bleControlAssembly = "";
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
    WiFi.mode(WIFI_STA);
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
      const String result = String("OK:") + WiFi.localIP().toString();
      bleWifiStatusCharacteristic->setValue(result.c_str());
      Serial.printf(">> Wi-Fi provisioned. STA IP: %s \n",
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
    Serial.println(">> Wi-Fi provisioning timed out. BLE remains available for retry.");
    if (savedWifiCredentialsAvailable) {
      WiFi.begin(savedWifiCredentials.ssid.c_str(), savedWifiCredentials.password.c_str());
    }
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

class WifiScanCommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    const String command = characteristic->getValue();

    if (command == "SCAN" || command == "scan") {
      // Start a fresh scan and clear any previous payload.
      bleWifiScanJson = "{\"networks\":[]}";
      bleWifiScanRequested = true;
      characteristic->setValue("SCANNING");
      return;
    }

    // The mobile client retrieves the completed JSON in small reads because
    // the default BLE ATT MTU cannot carry the complete scan response.
    if (command.startsWith("GET:")) {
      const String offsetText = command.substring(4);
      if (offsetText.length() == 0) {
        characteristic->setValue("ERROR:OFFSET");
        return;
      }

      for (unsigned int i = 0; i < offsetText.length(); ++i) {
        if (!isDigit(offsetText[i])) {
          characteristic->setValue("ERROR:OFFSET");
          return;
        }
      }

      const int offset = offsetText.toInt();
      if (offset < 0 || offset >= (int)bleWifiScanJson.length()) {
        characteristic->setValue("ERROR:OFFSET");
        return;
      }

      static const int BLE_WIFI_SCAN_CHUNK_SIZE = 18;
      characteristic->setValue(
        bleWifiScanJson.substring(offset, offset + BLE_WIFI_SCAN_CHUNK_SIZE).c_str()
      );
      return;
    }

    characteristic->setValue("ERROR:COMMAND");
  }
};

void startBleDiscovery() {
  BLEDevice::init(BLE_DEVICE_NAME);
  bleServer = BLEDevice::createServer();
  bleServer->setCallbacks(new RobotBleServerCallbacks());
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
  bleControlAckCharacteristic->setValue("{\"status\":\"ready\"}");

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
#define SHIELD_RST_PIN      23    // WeMos Shield RST (moved off boot-strapping GPIO5)

#define PUMP_RELAY_PIN      4     // Relay IN (D4 on ESP32)
#define STEER_SERVO_PIN     13    // MG996R Steering Servo (D13 on ESP32)
#define NOZZLE_SERVO_PIN    33    // Nozzle Sweep Servo (moved off boot-strapping GPIO12)

#define LINE_LEFT_PIN       14    // TCRT5000 Left Digital (D14)
#define LINE_RIGHT_PIN      27    // TCRT5000 Right Digital (D27)

// Front obstacle-detection sonar
#define OBSTACLE_TRIG_PIN   18    // JSN-B02 Trigger (D18)
#define OBSTACLE_ECHO_PIN   19    // JSN-B02 Echo (D19)

// Reservoir water-level sonar (GPIOs reserved; placement/calibration pending)
#define WATER_TRIG_PIN      25
#define WATER_ECHO_PIN      26

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
// Emergency-stop latch is controlled by firmware commands; the chassis reset
// buttons are wired directly to ESP32 EN and do not consume a GPIO.
bool emergencyStopLatched = false;
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
uint8_t audioVolume = 15;
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
}

void serviceBleControlCommands() {
  if (!bleControlCommandPending) return;

  String raw = pendingBleControlCommand;
  pendingBleControlCommand = "";
  bleControlCommandPending = false;

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

  if (command != "heartbeat" && command != "irrigation_profile")
    manualControlViaMqtt = false;
  bool supported = true;
  bleCommandRejected = false;
  bleCommandExecution = true;
  if (command == "irrigation_profile") {
    JsonObjectConst payload = doc["payload"].as<JsonObjectConst>();
    const int left = payload["left"] | -1;
    const int back = payload["back"] | -1;
    const int right = payload["right"] | -1;
    const int repeat = payload["repeat"] | -1;
    if (currentMode == MODE_AUTONOMOUS || irrigationSequenceActive ||
        left < 0 || left > 60 || back < 0 || back > 60 ||
        right < 0 || right > 60 || repeat < 1 || repeat > 20 ||
        (left == 0 && back == 0 && right == 0)) {
      bleCommandExecution = false;
      publishBleControlAck(commandId, "rejected", "INVALID_PROFILE",
                           "Profile values are invalid or irrigation is currently active");
      return;
    }
    irrigationLeftSeconds = left;
    irrigationBackSeconds = back;
    irrigationRightSeconds = right;
    irrigationRepeatCount = repeat;
  } else if (command == "forward") {
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

  bleCommandExecution = false;
  if (!supported) {
    publishBleControlAck(commandId, "rejected", "UNSUPPORTED_COMMAND", "Command is not enabled over BLE yet");
  } else if (bleCommandRejected) {
    publishBleControlAck(commandId, "rejected", "COMMAND_REJECTED", "Robot safety or operating-state checks rejected the command");
  } else {
    publishBleControlAck(commandId, "executed", "OK", "Command accepted by robot");
  }
}

void publishBleTelemetryIfDue() {
  if (bleControlTelemetryCharacteristic == nullptr ||
      millis() - lastBleTelemetryAt < 1000) return;
  lastBleTelemetryAt = millis();

  StaticJsonDocument<896> doc;
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
  doc["batteryVoltage"] = batteryVoltageValid ? batteryVoltage : 0.0f;
  doc["batteryVoltageValid"] = batteryVoltageValid;
  doc["controlsEnabled"] = controlsEnabled;
  const bool wifiConnected = WiFi.status() == WL_CONNECTED;
  doc["wifiConnected"] = wifiConnected;
  if (wifiConnected) {
    doc["wifiRssi"] = WiFi.RSSI();
    doc["wifiIp"] = WiFi.localIP().toString();
  } else {
    doc["wifiRssi"] = nullptr;
    doc["wifiIp"] = "";
  }
  doc["hostname"] = "A-Rigator";
  doc["obstacleDistanceCm"] = getObstacleDistanceCM();
  doc["obstacleStopThresholdCm"] = OBSTACLE_STOP_CM;
  doc["manualControlTimeoutMs"] = REMOTE_MANUAL_CONTROL_TIMEOUT_MS;
  doc["lineGpio14"] = digitalRead(14) == HIGH;
  doc["lineGpio27"] = digitalRead(27) == HIGH;
  doc["connectionMode"] = "BLE";
  String payload;
  serializeJson(doc, payload);
  bleControlTelemetryCharacteristic->setValue(payload.c_str());
  // Keep BLE and Remote IoT dashboards on the same telemetry contract.
  if (mqttManager.connected()) mqttManager.publishTelemetry(payload);
}


// Forget the saved router profile while preserving BLE local access.
bool forgetSavedWifiNetwork() {
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
  WiFi.setAutoReconnect(false);
  WiFi.disconnect(false, true);
  Serial.println(">> Saved router Wi-Fi forgotten. BLE remains available for reprovisioning.");
  return true;
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
  // A-Rigator has one physical drive motor connected to channel A only.
  // Channel B is intentionally left unused.
  lastShieldMotorDirection = dir;
  lastShieldMotorSpeed = speed_percent;
  sendRawPacket(MOTOR_A, dir, speed_percent);
  lastShieldKeepAlive = millis();
}

void refreshMotorShieldKeepAlive() {
  if (!motorShieldInitialized ||
      millis() - lastShieldKeepAlive < SHIELD_KEEPALIVE_INTERVAL_MS) {
    return;
  }

  // Refresh only the connected drive channel. Do not use driveMotor()
  // here because this is a refresh, not a new motor-state change.
  sendRawPacket(MOTOR_A, lastShieldMotorDirection, lastShieldMotorSpeed);
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
    audioPlayer.volume(15); // Moderate test volume for the 8-ohm, 0.5W speaker (DFPlayer range: 0-30)
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

  audioPlayer.play(trackNumber); // /mp3/0001.mp3, /mp3/0002.mp3, ...
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

void markBleCommandRejected(int code) {
  if (bleCommandExecution && code >= 400) bleCommandRejected = true;
}

void sendControlResponse(const char* command) {
  // Command handlers report success through their side effects. The BLE
  // dispatcher publishes the matching command acknowledgement after return.
  (void)command;
}

void setManualMotorDirection(uint8_t direction, const char* command) {
  if (emergencyStopLatched) {
    markBleCommandRejected(423);
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
    markBleCommandRejected(409);
    return;
  }
  manualTerrainModeActive = false;
  writeServo(STEER_SERVO_PIN, STEER_LEFT);
  sendControlResponse("steer_left");
}

void handleSteerRight() {
  if (currentMode == MODE_AUTONOMOUS) {
    markBleCommandRejected(409);
    return;
  }
  manualTerrainModeActive = false;
  writeServo(STEER_SERVO_PIN, STEER_RIGHT);
  sendControlResponse("steer_right");
}

void handleSteerCenter() {
  if (currentMode == MODE_AUTONOMOUS) {
    markBleCommandRejected(409);
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
    markBleCommandRejected(409);
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
    markBleCommandRejected(409);
    return;
  }
  nozzleSweepActive = false;
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  sendControlResponse("nozzle_sweep_stopped");
}

void handleIrrigationStart() {
  if (currentMode == MODE_AUTONOMOUS || irrigationSequenceActive) {
    markBleCommandRejected(409);
    return;
  }
  if (irrigationLeftSeconds == 0 && irrigationBackSeconds == 0 && irrigationRightSeconds == 0) {
    markBleCommandRejected(400);
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
    markBleCommandRejected(423);
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
    markBleCommandRejected(423);
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
    markBleCommandRejected(423);
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
    markBleCommandRejected(409);
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
    markBleCommandRejected(409);
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
    markBleCommandRejected(409);
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
    markBleCommandRejected(423);
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
  // BLE actions are event-driven and do not need heartbeats. Remote manual
  // control uses heartbeats as a phone-session lease; autonomous work never does.
  if (currentMode == MODE_MANUAL && manualControlViaMqtt)
    refreshManualControlLease();
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
  if (command == "autonomous_start" || command == "autonomous_stop") {
    // An autonomous task is owned by the robot after it starts, not by the
    // lifetime of the phone's MQTT session.
    manualControlViaMqtt = false;
  } else if (command != "heartbeat") {
    manualControlViaMqtt = true;
  }

  if (command == "stop") {
    handleStop();
  } else if (command == "autonomous_start") {
    if (emergencyStopLatched) {
      publishMqttCommandAck(commandId, "rejected", "ESTOP_LATCHED", "Emergency stop is latched");
      return;
    }
    handleAutonomousStart();
  } else if (command == "autonomous_stop") {
    handleAutonomousStop();
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

void stopManualOutputsForLinkLoss(const char* source) {
  if (currentMode != MODE_MANUAL) return;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  manualTerrainModeActive = false;
  nozzleSweepActive = false;
  irrigationSequenceActive = false;
  irrigationSequenceSpraying = false;
  irrigationSequenceManual = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  Serial.printf(">> [SAFETY] Manual outputs stopped after %s connection loss.\\n", source);
}

void serviceControlLinkSafety() {
  if (bleClientDisconnectedPending) {
    bleClientDisconnectedPending = false;
    if (!manualControlViaMqtt) stopManualOutputsForLinkLoss("Bluetooth");
  }
  if (manualControlViaMqtt && currentMode == MODE_MANUAL && mqttConfigured &&
      (WiFi.status() != WL_CONNECTED || !mqttManager.connected())) {
    stopManualOutputsForLinkLoss("Remote IoT");
    manualControlViaMqtt = false;
  }
}

void enforceRemoteManualControlTimeout() {
  if (manualControlViaMqtt && manualControlLeaseActive &&
      currentMode == MODE_MANUAL &&
      millis() - lastManualControlRefresh > REMOTE_MANUAL_CONTROL_TIMEOUT_MS) {
    stopManualOutputsForLinkLoss("Remote control session");
    manualControlViaMqtt = false;
    Serial.println(">> [SAFETY] Remote manual-control session expired; phone heartbeat was lost.");
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
    startIrrigationSequence(false);
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
  Serial.println("  LED ON / LED OFF          : Switch LED strips");
  Serial.println("  AUDIO PLAY 1              : Play root track 1 (0001.mp3)");
  Serial.println("  AUDIO VOL 0-30            : Set DFPlayer volume");
  Serial.println("  AUDIO STOP                : Stop audio playback");
  Serial.println("  AUDIO STATUS              : Show audio module status");
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
  if (line == "LED ON") {
    if (emergencyStopLatched) {
      Serial.println(">> [LED] Rejected: emergency stop is latched.");
    } else {
      setLedOutput(true);
      Serial.println(">> [LED] Output ON. Check strip supply voltage and wiring.");
    }
    return;
  }
  if (line == "LED OFF") {
    setLedOutput(false);
    Serial.println(">> [LED] Output OFF.");
    return;
  }
  if (line == "AUDIO STATUS") {
    Serial.printf(">> [AUDIO] DFPlayer: %s | Volume setting: %u/30 | Speaker: configured externally (8-ohm, 0.5W)\n",
      audioAvailable ? "INITIALIZED" : "NOT DETECTED", audioVolume);
    return;
  }
  if (line == "AUDIO STOP") {
    if (!audioAvailable) {
      Serial.println(">> [AUDIO] DFPlayer is not initialized.");
    } else {
      audioPlayer.stop();
      Serial.println(">> [AUDIO] Stop command sent.");
    }
    return;
  }
  if (line.startsWith("AUDIO ")) {
    String args = line.substring(6);
    args.trim();
    const int separator = args.indexOf(' ');
    const String action = separator < 0 ? args : args.substring(0, separator);
    String value = separator < 0 ? "" : args.substring(separator + 1);
    value.trim();
    if (action == "PLAY") {
      const int track = value.toInt();
      if (!audioAvailable) {
        Serial.println(">> [AUDIO] DFPlayer is not initialized; check 5V, GND, RX/TX, and SD card.");
      } else if (track < 1 || track > 99) {
        Serial.println(">> [AUDIO] Track must be 1-99 (root file: 0001.mp3, etc.).");
      } else {
        audioPlayer.play(track);
        Serial.printf(">> [AUDIO] Play request sent for /mp3/%04d.mp3.\n", track);
      }
    } else if (action == "VOL") {
      const int requestedVolume = value.toInt();
      if (value.length() == 0 || requestedVolume < 0 || requestedVolume > 30) {
        Serial.println(">> [AUDIO] Volume must be between 0 and 30.");
      } else if (!audioAvailable) {
        Serial.println(">> [AUDIO] DFPlayer is not initialized.");
      } else {
        audioVolume = (uint8_t)requestedVolume;
        audioPlayer.volume(audioVolume);
        Serial.printf(">> [AUDIO] Volume set to %u/30.\n", audioVolume);
      }
    } else {
      Serial.println(">> [AUDIO] Use AUDIO PLAY <track>, AUDIO VOL <0-30>, AUDIO STOP, or AUDIO STATUS.");
    }
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
	    Serial.println(">> [CMD] Router Wi-Fi reset complete. BLE provisioning remains available.");
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
  // Startup playback disabled during audio diagnostics; use AUDIO PLAY 1.

  pinMode(STEER_SERVO_PIN, OUTPUT);
  pinMode(NOZZLE_SERVO_PIN, OUTPUT);
  writeServo(STEER_SERVO_PIN, STEER_CENTER);
  writeServo(NOZZLE_SERVO_PIN, NOZZLE_CENTER);

  pinMode(SHIELD_RST_PIN, OUTPUT);
  digitalWrite(SHIELD_RST_PIN, LOW);

  if (wifiCredentialStore.begin()) {
    savedWifiCredentialsAvailable = wifiCredentialStore.load(savedWifiCredentials);
    Serial.println(savedWifiCredentialsAvailable
      ? ">> Saved Wi-Fi credentials found in NVS."
      : ">> No saved Wi-Fi credentials. Configure router Wi-Fi over BLE.");
  } else {
    Serial.println(">> Could not open Wi-Fi credential storage. BLE local control remains available.");
  }


  startBleDiscovery();
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  if (savedWifiCredentialsAvailable) {
    WiFi.begin(savedWifiCredentials.ssid.c_str(), savedWifiCredentials.password.c_str());
    Serial.println(">> Attempting to connect to the saved router Wi-Fi network.");
  } else {
    Serial.println(">> No router configured. Provision Wi-Fi through BLE; local BLE control remains available.");
  }
  controlsEnabled = true;

  // MQTT is optional. BLE remains the local control path when cloud access is unavailable.
  if (mqttConfigStore.begin()) {
    mqttConfigStoreAvailable = true;
    mqttConfigured = mqttConfigStore.load(mqttConfig);
    if (mqttConfigured && mqttManager.begin(mqttConfig)) {
      Serial.println(">> Per-device MQTT configuration loaded. Waiting for station Wi-Fi.");
    } else {
      mqttConfigured = false;
      Serial.println(">> MQTT is not provisioned. BLE local control remains available.");
    }
  } else {
    Serial.println(">> MQTT NVS unavailable. Local control remains available.");
  }

  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(10000);
  delay(100);
  initializeMotorShield();

  driveMotor(CMD_STOP, 0.0);
  setLedOutput(false); // Keep lighting off until explicitly tested from Serial Monitor.
  // Ready playback disabled during audio diagnostics; use AUDIO PLAY 2.
  printMenu();
}

void loop() {
  readBatteryVoltage();
  mqttManager.loop();
  serviceMqttCommands();
  serviceBleWifiScan();
  serviceBleWifiProvisioning();
  serviceBleControlCommands();
  publishBleTelemetryIfDue();

  // Report station connectivity transitions without exposing the password.
  const bool stationConnected = WiFi.status() == WL_CONNECTED;
  if (stationConnected && !mqttClockSyncRequested) {
    configTime(0, 0, "pool.ntp.org", "time.google.com");
    mqttClockSyncRequested = true;
  }
  if (stationConnected && !stationWasConnected) {
    Serial.printf(">> Router Wi-Fi connected. IP: %s\n",
                  WiFi.localIP().toString().c_str());
  } else if (!stationConnected && stationWasConnected) {
    Serial.println(">> Router Wi-Fi disconnected. ESP32 will attempt to reconnect.");
  }
  stationWasConnected = stationConnected;

  serviceControlLinkSafety();
  enforceRemoteManualControlTimeout();
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
