#include "BluetoothSerial.h"
#include "esp_bt_device.h"
#include "esp_bt_main.h"
#include "esp_gap_bt_api.h"

BluetoothSerial SerialBT;

#define MAX_DEVICES 20
esp_bd_addr_t discovered_devices[MAX_DEVICES];
char device_names[MAX_DEVICES][64];
int device_count = 0;
bool scan_complete = false;

// GAP 콜백 - 장치 발견 시 호출
void gap_callback(esp_bt_gap_cb_event_t event, esp_bt_gap_cb_param_t *param) {
  if (event == ESP_BT_GAP_DISC_RES_EVT) {
    // 새 장치 발견
    if (device_count < MAX_DEVICES) {
      // MAC 주소 저장
      memcpy(discovered_devices[device_count], param->disc_res.bda, sizeof(esp_bd_addr_t));

      // 이름 찾기
      char *name = NULL;
      for (int i = 0; i < param->disc_res.num_prop; i++) {
        if (param->disc_res.prop[i].type == ESP_BT_GAP_DEV_PROP_EIR) {
          uint8_t *eir = (uint8_t *)param->disc_res.prop[i].val;
          name = (char *)esp_bt_gap_resolve_eir_data(eir, ESP_BT_EIR_TYPE_CMPL_LOCAL_NAME, NULL);
          if (!name) {
            name = (char *)esp_bt_gap_resolve_eir_data(eir, ESP_BT_EIR_TYPE_SHORT_LOCAL_NAME, NULL);
          }
        }
        if (param->disc_res.prop[i].type == ESP_BT_GAP_DEV_PROP_BDNAME && !name) {
          name = (char *)param->disc_res.prop[i].val;
        }
      }

      if (name && strlen(name) > 0) {
        strncpy(device_names[device_count], name, 63);
      } else {
        strcpy(device_names[device_count], "(unknown)");
      }
      device_names[device_count][63] = '\0';

      device_count++;
    }
  } else if (event == ESP_BT_GAP_DISC_STATE_CHANGED_EVT) {
    if (param->disc_st_chg.state == ESP_BT_GAP_DISCOVERY_STOPPED) {
      scan_complete = true;
    }
  }
}

void scanDevices() {
  Serial.println("Scanning... (about 10 sec)");

  // 초기화
  device_count = 0;
  scan_complete = false;

  // 스캔 시작
  esp_bt_gap_start_discovery(ESP_BT_INQ_MODE_GENERAL_INQUIRY, 10, 0);

  // 스캔 완료 대기
  while (!scan_complete) {
    delay(100);
  }

  // print results
  Serial.println("------------------------------");
  if (device_count == 0) {
    Serial.println("No devices found");
  } else {
    for (int i = 0; i < device_count; i++) {
      Serial.printf("[%d] %s\n", i + 1, device_names[i]);
      Serial.printf("    MAC: %02X:%02X:%02X:%02X:%02X:%02X\n",
                    discovered_devices[i][0], discovered_devices[i][1],
                    discovered_devices[i][2], discovered_devices[i][3],
                    discovered_devices[i][4], discovered_devices[i][5]);
    }
    Serial.println("------------------------------");
    Serial.printf("Found %d device(s)\n", device_count);
  }
}

void setup() {
  Serial.begin(115200);
  delay(1000);

  Serial.println("ESP32 Bluetooth Scanner");
  Serial.println("------------------------------");

  if (!btStart()) {
    Serial.println("Bluetooth start failed");
    return;
  }

  if (esp_bluedroid_init() != ESP_OK) {
    Serial.println("Bluedroid init failed");
    return;
  }

  if (esp_bluedroid_enable() != ESP_OK) {
    Serial.println("Bluedroid enable failed");
    return;
  }

  esp_bt_gap_register_callback(gap_callback);

  Serial.println("Ready!");
  Serial.println("Enter 1 -> Scan devices");
  Serial.println("------------------------------");
}

void loop() {
  if (Serial.available()) {
    char input = Serial.read();

    // 버퍼 비우기
    while (Serial.available()) {
      Serial.read();
    }

    if (input == '1') {
      scanDevices();
    } else {
      Serial.println("Command: 1 = Scan");
    }
  }
  delay(10);
}
