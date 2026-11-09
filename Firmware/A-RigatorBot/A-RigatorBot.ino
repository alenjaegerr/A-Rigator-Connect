#include <Wire.h>
#include <WiFi.h>
#include <WebServer.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include "RobotWifiConfig.h"

// ============================================================================
// 1. WI-FI STATUS API
// ============================================================================
// Replace the placeholder locally before flashing. Never publish the password.
static const char* WIFI_SSID = "A-Rigator";
static const char* WIFI_PASSWORD = "12345678";
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

// BLE provisioning transport. Network scanning is read-only; credential writes
// will remain disabled until the authenticated session protocol is implemented.
static const char* BLE_DEVICE_NAME = "A-Rigator";
static const char* BLE_PROVISIONING_SERVICE_UUID = "7a1c0001-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_DISCOVERY_CHARACTERISTIC_UUID = "7a1c0002-8f2d-4b6a-9c31-4a5249474154";
static const char* BLE_WIFI_SCAN_CHARACTERISTIC_UUID = "7a1c0003-8f2d-4b6a-9c31-4a5249474154";
BLEServer* bleServer = nullptr;
BLEService* bleProvisioningService = nullptr;
BLECharacteristic* bleDiscoveryCharacteristic = nullptr;
BLECharacteristic* bleWifiScanCharacteristic = nullptr;
volatile bool bleWifiScanRequested = false;
bool bleWifiScanRunning = false;
void handleForward();
void handleReverse();
void handleStop();
void handleAutonomousStart();
void handleAutonomousStop();
void handlePumpOn();
void handlePumpOff();
void handleHeartbeat();
void handleResetShield();

// ============================================================================
// BLE DISCOVERY + READ-ONLY WI-FI SCAN
// ============================================================================
class WifiScanCommandCallbacks : public BLECharacteristicCallbacks {
  void onWrite(BLECharacteristic* characteristic) override {
    const std::string command = characteristic->getValue();
    if (command == "SCAN") {
      bleWifiScanRequested = true;
      characteristic->setValue("{\"state\":\"scanning\",\"networks\":[]}");
    } else {
      characteristic->setValue("{\"state\":\"error\",\"message\":\"Unsupported command\",\"networks\":[]}");
    }
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

void serviceBleWifiScan() {
  if (bleWifiScanRequested && !bleWifiScanRunning) {
    bleWifiScanRequested = false;
    const int result = WiFi.scanNetworks(true, true);
    if (result == WIFI_SCAN_FAILED) {
      bleWifiScanCharacteristic->setValue("{\"state\":\"error\",\"networks\":[]}");
    } else {
      bleWifiScanRunning = true;
      bleWifiScanCharacteristic->setValue("{\"state\":\"scanning\",\"networks\":[]}");
    }
  }

  if (!bleWifiScanRunning) return;

  const int count = WiFi.scanComplete();
  if (count == WIFI_SCAN_RUNNING) return;

  if (count == WIFI_SCAN_FAILED) {
    bleWifiScanCharacteristic->setValue("{\"state\":\"error\",\"networks\":[]}");
  } else {
    String json = "{\"state\":\"complete\",\"networks\":[";
    for (int i = 0; i < count; ++i) {
      if (i > 0) json += ",";
      json += "{\"ssid\":\"";
      json += escapeJsonString(WiFi.SSID(i));
      json += "\",\"rssi\":";
      json += String(WiFi.RSSI(i));
      json += ",\"secured\":";
      json += (WiFi.encryptionType(i) == WIFI_AUTH_OPEN ? "false" : "true");
      json += "}";
    }
    json += "]}";
    bleWifiScanCharacteristic->setValue(json.c_str());
  }

  WiFi.scanDelete();
  bleWifiScanRunning = false;
}

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
unsigned long lastMotorRefresh = 0;
unsigned long lastManualControlRefresh = 0;
bool manualControlLeaseActive = false;
bool controlsEnabled = false;

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
  json += "}";
  server.send(200, "application/json", json);
}

void handleNotFound() {
  server.send(404, "application/json", "{\"success\":false,\"message\":\"Not found\"}");
}

void startWifiApi() {
  const bool openSetupMode = String(WIFI_PASSWORD) == "CHANGE_THIS_BEFORE_FLASHING";
  if (!openSetupMode && String(WIFI_PASSWORD).length() < 8) {
	Serial.println(">> Wi-Fi AP not started. Use a WPA2 password of at least 8 characters or restore the setup placeholder.");
	return;
  }

  // Keep the existing A-Rigator setup/control AP available. When credentials
  // have been provisioned, join the saved network in AP+STA mode as well.
  // This preserves the current 192.168.4.1 local-control path during migration.
  WiFi.mode(savedWifiCredentialsAvailable ? WIFI_AP_STA : WIFI_AP);
  WiFi.softAPConfig(AP_IP, AP_GATEWAY, AP_SUBNET);

  bool apStarted = false;
  for (uint8_t attempt = 1; attempt <= WIFI_AP_START_RETRIES; ++attempt) {
	apStarted = openSetupMode
		? WiFi.softAP(WIFI_SSID)
		: WiFi.softAP(WIFI_SSID, WIFI_PASSWORD);
	if (apStarted) {
	  break;
	}
	Serial.printf(">> Wi-Fi AP start attempt %u/%u failed. Retrying...\n", attempt, WIFI_AP_START_RETRIES);
	delay(WIFI_AP_RETRY_DELAY_MS);
  }

  if (!apStarted) {
	Serial.println(">> Wi-Fi AP failed to start; robot serial controls remain available.");
	return;
  }

	controlsEnabled = !openSetupMode;
  server.on("/api/status", HTTP_GET, handleStatus);
  if (controlsEnabled) {
	server.on("/api/control/forward", HTTP_POST, handleForward);
	server.on("/api/control/reverse", HTTP_POST, handleReverse);
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

  Serial.println(controlsEnabled
	  ? ">> WPA2 AP and manual-control API ready."
	  : ">> OPEN setup AP ready; status only. Motor and pump API routes are disabled.");
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

void handleStop() {
  currentMode = MODE_MANUAL;
  manualMotorDir = CMD_STOP;
  manualControlLeaseActive = false;
  driveMotor(CMD_BRAKE, 0.0);
  setPump(false);
  sendControlResponse("stop");
}

void handleAutonomousStart() {
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
	  sweepNozzleOnce();
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

  if (wifiCredentialStore.begin()) {
    savedWifiCredentialsAvailable = wifiCredentialStore.load(savedWifiCredentials);
    Serial.println(savedWifiCredentialsAvailable
      ? ">> Saved Wi-Fi credentials found in NVS."
      : ">> No saved Wi-Fi credentials. Local setup AP only.");
  } else {
    Serial.println(">> Could not open Wi-Fi credential storage. Continuing with local setup AP.");
  }

  startWifiApi();
  startBleDiscovery();

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

  // Report station connectivity transitions without exposing the password.
  const bool stationConnected = WiFi.status() == WL_CONNECTED;
  if (stationConnected && !stationWasConnected) {
    Serial.printf(">> Saved Wi-Fi connected. Robot network IP: %s\\n",
                  WiFi.localIP().toString().c_str());
  } else if (!stationConnected && stationWasConnected) {
    Serial.println(">> Saved Wi-Fi disconnected. ESP32 will attempt to reconnect.");
  }
  stationWasConnected = stationConnected;

  enforceManualControlTimeout();
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
