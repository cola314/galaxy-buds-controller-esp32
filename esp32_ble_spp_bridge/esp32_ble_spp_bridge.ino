#include "esp_bt.h"
#include "esp_bt_main.h"
#include "esp_bt_device.h"
#include "esp_gap_bt_api.h"
#include "esp_spp_api.h"
#include "buds_protocol.h"
#include "ble_server.h"

// ========== Galaxy Buds 3 Pro 설정 ==========
uint8_t BUDS_MAC[6] = {0x5C, 0xDC, 0x49, 0x0A, 0x20, 0x3B};

static const uint8_t BUDS_SPP_UUID[16] = {
    0x2e, 0x73, 0xa4, 0xad, 0x33, 0x2d, 0x41, 0xfc,
    0x90, 0xe2, 0x16, 0xbe, 0xf0, 0x65, 0x23, 0xf2
};

// ========== 상태 변수 ==========
uint32_t spp_handle = 0;
bool spp_connected = false;

int8_t battery_left = -1;
int8_t battery_right = -1;
int8_t noise_control = -1;
int8_t anc_level = -1;
int8_t ambient_level = -1;
bool wearing = false;

// ========== BLE 변수 ==========
BLEServer* pServer = nullptr;
BLECharacteristic* pNoiseCharacteristic = nullptr;
BLECharacteristic* pBatteryCharacteristic = nullptr;
BLECharacteristic* pStatusCharacteristic = nullptr;
BLECharacteristic* pCommandCharacteristic = nullptr;
bool ble_device_connected = false;

// 연결 요청 플래그
volatile bool connect_requested = false;
volatile bool disconnect_after_status = false;
volatile bool connection_test_requested = false;

// ========== 패킷 파싱 ==========
void parsePacket(uint8_t *data, int len) {
    int i = 0;
    while (i < len) {
        if (data[i] != SOM) { i++; continue; }
        if (i + 4 > len) break;

        int pktLen = data[i + 1] | ((data[i + 2] & 0x0F) << 8);
        int end = i + 4 + pktLen - 3 + 2 + 1;

        if (end > len) break;
        if (data[end - 1] != EOM) { i++; continue; }

        uint8_t msgId = data[i + 3];
        uint8_t *payload = &data[i + 4];
        int payloadLen = end - 3 - (i + 4);

        // 메시지 처리
        if (msgId == MSG_EXTENDED_STATUS && payloadLen > 31) {
            battery_left = payload[2];
            battery_right = payload[3];
            wearing = (payload[4] & 0x01) != 0;
            noise_control = payload[31];

            Serial.printf("[STATUS] Battery: L=%d%% R=%d%%, Wearing: %s, Mode: %d\n",
                          battery_left, battery_right, wearing ? "Yes" : "No", noise_control);

            // BLE로 배터리 상태 전송
            if (ble_device_connected && pBatteryCharacteristic) {
                uint8_t batteryData[2] = {(uint8_t)battery_left, (uint8_t)battery_right};
                pBatteryCharacteristic->setValue(batteryData, 2);
                pBatteryCharacteristic->notify();
            }

            // BLE로 전체 상태 전송
            if (ble_device_connected && pStatusCharacteristic) {
                uint8_t statusData[3] = {
                    (uint8_t)wearing,
                    (uint8_t)noise_control,
                    (uint8_t)(noise_control == NC_ANC ? anc_level : ambient_level)
                };
                pStatusCharacteristic->setValue(statusData, 3);
                pStatusCharacteristic->notify();
            }
        }
        else if (msgId == MSG_ACK && payloadLen >= 2) {
            if (payload[0] == MSG_NOISE_CONTROL) {
                noise_control = payload[1];
                Serial.printf("[ACK] Noise Control: %d\n", noise_control);

                // BLE로 상태 업데이트
                if (ble_device_connected && pStatusCharacteristic) {
                    uint8_t statusData[3] = {
                        (uint8_t)wearing,
                        (uint8_t)noise_control,
                        (uint8_t)(noise_control == NC_ANC ? anc_level : ambient_level)
                    };
                    pStatusCharacteristic->setValue(statusData, 3);
                    pStatusCharacteristic->notify();
                }
            }
            else if (payload[0] == MSG_ANC_LEVEL) {
                anc_level = payload[1];
                Serial.printf("[ACK] ANC Level: %d\n", anc_level);

                // BLE로 상태 업데이트
                if (ble_device_connected && pStatusCharacteristic) {
                    uint8_t statusData[3] = {
                        (uint8_t)wearing,
                        (uint8_t)noise_control,
                        (uint8_t)anc_level
                    };
                    pStatusCharacteristic->setValue(statusData, 3);
                    pStatusCharacteristic->notify();
                }
            }
            else if (payload[0] == MSG_AMBIENT_LEVEL) {
                ambient_level = payload[1];
                Serial.printf("[ACK] Ambient Level: %d\n", ambient_level);

                // BLE로 상태 업데이트
                if (ble_device_connected && pStatusCharacteristic) {
                    uint8_t statusData[3] = {
                        (uint8_t)wearing,
                        (uint8_t)noise_control,
                        (uint8_t)ambient_level
                    };
                    pStatusCharacteristic->setValue(statusData, 3);
                    pStatusCharacteristic->notify();
                }
            }
        }

        i = end;
    }
}

// ========== SPP 콜백 ==========
void spp_callback(esp_spp_cb_event_t event, esp_spp_cb_param_t *param) {
    switch (event) {
        case ESP_SPP_INIT_EVT:
            Serial.println("[SPP] Initialized");
            break;

        case ESP_SPP_DISCOVERY_COMP_EVT:
            if (param->disc_comp.status == ESP_SPP_SUCCESS) {
                Serial.printf("[SPP] Found %d services\n", param->disc_comp.scn_num);
                if (param->disc_comp.scn_num > 0) {
                    esp_spp_connect(ESP_SPP_SEC_NONE, ESP_SPP_ROLE_MASTER,
                                    param->disc_comp.scn[0], BUDS_MAC);
                }
            } else {
                Serial.println("[SPP] Discovery failed");

                // 연결 테스트였으면 실패 알림
                if (connection_test_requested && ble_device_connected && pCommandCharacteristic) {
                    uint8_t result = RESULT_FAILED;
                    pCommandCharacteristic->setValue(&result, 1);
                    pCommandCharacteristic->notify();
                    connection_test_requested = false;
                }
            }
            break;

        case ESP_SPP_OPEN_EVT:
            if (param->open.status == ESP_SPP_SUCCESS) {
                spp_handle = param->open.handle;
                spp_connected = true;
                Serial.println("[SPP] Connected to Buds!");

                // Extended Status 요청 (배터리 등 상세 정보)
                uint8_t pkt[16];
                int len = createPacket(pkt, MSG_EXTENDED_STATUS, nullptr, 0);
                esp_spp_write(spp_handle, len, pkt);
                Serial.println("[SPP] Request Extended Status");

                // 연결 테스트였으면 성공 알림
                if (connection_test_requested && ble_device_connected && pCommandCharacteristic) {
                    uint8_t result = RESULT_SUCCESS;
                    pCommandCharacteristic->setValue(&result, 1);
                    pCommandCharacteristic->notify();
                    connection_test_requested = false;
                }
            } else {
                Serial.printf("[SPP] Connection failed: %d\n", param->open.status);

                // 연결 테스트였으면 실패 알림
                if (connection_test_requested && ble_device_connected && pCommandCharacteristic) {
                    uint8_t result = RESULT_FAILED;
                    pCommandCharacteristic->setValue(&result, 1);
                    pCommandCharacteristic->notify();
                    connection_test_requested = false;
                }
            }
            break;

        case ESP_SPP_CLOSE_EVT:
            spp_connected = false;
            spp_handle = 0;
            Serial.println("[SPP] Disconnected from Buds");
            break;

        case ESP_SPP_DATA_IND_EVT:
            Serial.printf("[SPP] RX %d bytes\n", param->data_ind.len);
            parsePacket(param->data_ind.data, param->data_ind.len);
            break;

        default:
            break;
    }
}

// ========== GAP 콜백 ==========
void gap_callback(esp_bt_gap_cb_event_t event, esp_bt_gap_cb_param_t *param) {
    switch (event) {
        case ESP_BT_GAP_AUTH_CMPL_EVT:
            if (param->auth_cmpl.stat == ESP_BT_STATUS_SUCCESS) {
                Serial.println("[GAP] Authentication success");
            } else {
                Serial.printf("[GAP] Authentication failed: %d\n", param->auth_cmpl.stat);
            }
            break;

        case ESP_BT_GAP_PIN_REQ_EVT: {
            Serial.println("[GAP] PIN requested");
            esp_bt_pin_code_t pin = {'0', '0', '0', '0'};
            esp_bt_gap_pin_reply(param->pin_req.bda, true, 4, pin);
            break;
        }

        default:
            break;
    }
}

// ========== Setup & Loop ==========
void setup() {
    Serial.begin(115200);
    delay(1000);

    Serial.println("=== Galaxy Buds 3 Pro Bridge ===");
    Serial.println("BLE Server + SPP Client");

    // Bluetooth Classic 초기화 (먼저!)
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

    Serial.println("[BT] Bluetooth Classic initialized");

    // 콜백 등록
    esp_bt_gap_register_callback(gap_callback);
    esp_spp_register_callback(spp_callback);

    // SPP 초기화
    esp_spp_cfg_t spp_cfg = {
        .mode = ESP_SPP_MODE_CB,
        .enable_l2cap_ertm = false,
    };
    esp_spp_enhanced_init(&spp_cfg);

    Serial.println("[SPP] SPP initialized");

    // BLE 초기화 (나중에!)
    initBLE();

    Serial.println("Ready! Waiting for commands...");
}

void loop() {
    // 연결 요청 처리
    if (connect_requested && !spp_connected) {
        connect_requested = false;
        Serial.println("[LOOP] Connecting to Galaxy Buds...");
        esp_spp_start_discovery(BUDS_MAC);
    }

    // 주기적으로 Extended Status 요청 (10초마다)
    static unsigned long lastStatusRequest = 0;
    unsigned long now = millis();

    if (spp_connected && (now - lastStatusRequest > 10000)) {
        uint8_t pkt[16];
        int len = createPacket(pkt, MSG_EXTENDED_STATUS, nullptr, 0);
        esp_spp_write(spp_handle, len, pkt);
        lastStatusRequest = now;
    }

    delay(100);
}
