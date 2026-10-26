#include <WiFi.h>
#include <WebServer.h>

// Temporary open setup network for connectivity testing only.
// Replace with a WPA2 password before using this AP outside a controlled test.
static const char* WIFI_SSID = "A-Rigator";
static const char* ROBOT_ID = "ARIGATOR-001";
static const IPAddress AP_IP(192, 168, 4, 1);
static const IPAddress AP_GATEWAY(192, 168, 4, 1);
static const IPAddress AP_SUBNET(255, 255, 255, 0);

WebServer server(80);

void handleStatus()
{
  String json = "{\"success\":true,\"robotId\":\"";
  json += ROBOT_ID;
  json += "\",\"state\":\"Idle\",\"mode\":\"Manual\",\"pump\":false,\"message\":\"A-Rigator status endpoint ready\"}";
  server.send(200, "application/json", json);
}

void handleNotFound()
{
  server.send(404, "application/json", "{\"success\":false,\"message\":\"Not found\"}");
}

void setup()
{
  Serial.begin(115200);
  WiFi.mode(WIFI_AP);
  WiFi.softAPConfig(AP_IP, AP_GATEWAY, AP_SUBNET);

  if (!WiFi.softAP(WIFI_SSID))
  {
	Serial.println("Failed to start A-Rigator setup access point.");
	return;
  }

  server.on("/api/status", HTTP_GET, handleStatus);
  server.onNotFound(handleNotFound);
  server.begin();

  Serial.println("A-Rigator pairing status test is ready.");
  Serial.print("SSID: ");
  Serial.println(WIFI_SSID);
  Serial.print("Status URL: http://");
  Serial.print(WiFi.softAPIP());
  Serial.println("/api/status");
  Serial.println("WARNING: this temporary setup AP is open and has no Wi-Fi password.");
}

void loop()
{
  server.handleClient();
}
