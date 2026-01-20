#include "esp_bt.h"
#include "esp_bt_main.h"
#include "esp_bt_device.h"
#include "esp_gap_bt_api.h"
#include "esp_spp_api.h"
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>

// ========== Galaxy Buds 3 Pro 설정 ==========
uint8_t BUDS_MAC[6] = {0x5C, 0xDC, 0x49, 0x0A, 0x20, 0x3B};

static const uint8_t BUDS_SPP_UUID[16] = {
    0x2e, 0x73, 0xa4, 0xad, 0x33, 0x2d, 0x41, 0xfc,
    0x90, 0xe2, 0x16, 0xbe, 0xf0, 0x65, 0x23, 0xf2
};

// ========== BLE UUIDs (MAUI 앱과 통신용) ==========
#define SERVICE_UUID        "12345678-1234-5678-1234-56789abcdef0"
#define NOISE_CHAR_UUID     "12345678-1234-5678-1234-56789abcdef1"
#define BATTERY_CHAR_UUID   "12345678-1234-5678-1234-56789abcdef2"
#define STATUS_CHAR_UUID    "12345678-1234-5678-1234-56789abcdef3"

// ========== 프로토콜 상수 ==========
#define SOM 0xFD
#define EOM 0xDD
#define MSG_NOISE_CONTROL 0x78
#define MSG_EXTENDED_STATUS 0x61
#define MSG_ACK 0x42
#define MSG_AMBIENT_LEVEL 0x84
#define MSG_ANC_LEVEL 0x83

#define NC_OFF 0x00
#define NC_ANC 0x01
#define NC_AMBIENT 0x02
#define NC_ADAPTIVE 0x03

// ========== 상태 변수 ==========
static uint32_t spp_handle = 0;
static bool spp_connected = false;

static int8_t battery_left = -1;
static int8_t battery_right = -1;
static int8_t noise_control = -1;
static int8_t anc_level = -1;
static int8_t ambient_level = -1;
static bool wearing = false;

// ========== BLE 변수 ==========
BLEServer* pServer = nullptr;
BLECharacteristic* pNoiseCharacteristic = nullptr;
BLECharacteristic* pBatteryCharacteristic = nullptr;
BLECharacteristic* pStatusCharacteristic = nullptr;
bool ble_device_connected = false;

// ========== CRC16 테이블 ==========
static const uint16_t CRC_TABLE[256] = {
    0, 4129, 8258, 12387, 16516, 20645, 24774, 28903,
    33032, 37161, 41290, 45419, 49548, 53677, 57806, 61935,
    4657, 528, 12915, 8786, 21173, 17044, 29431, 25302,
    37689, 33560, 45947, 41818, 54205, 50076, 62463, 58334,
    9314, 13379, 1056, 5121, 25830, 29895, 17572, 21637,
    42346, 46411, 34088, 38153, 58862, 62927, 50604, 54669,
    13907, 9842, 5649, 1584, 30423, 26358, 22165, 18100,
    46939, 42874, 38681, 34616, 63455, 59390, 55197, 51132,
    18628, 22757, 26758, 30887, 2112, 6241, 10242, 14371,
    51660, 55789, 59790, 63919, 35144, 39273, 43274, 47403,
    23285, 19156, 31415, 27286, 6769, 2640, 14899, 10770,
    56317, 52188, 64447, 60318, 39801, 35672, 47931, 43802,
    27814, 31879, 19684, 23749, 11298, 15363, 3168, 7233,
    60846, 64911, 52716, 56781, 44330, 48395, 36200, 40265,
    32407, 28342, 24277, 20212, 15891, 11826, 7761, 3696,
    65439, 61374, 57309, 53244, 48923, 44858, 40793, 36728,
    37256, 33193, 45514, 41451, 53516, 49453, 61774, 57711,
    4224, 161, 12482, 8419, 20484, 16421, 28742, 24679,
    33721, 37784, 41979, 46042, 49981, 54044, 58239, 62302,
    689, 4752, 8947, 13010, 16949, 21012, 25207, 29270,
    46570, 42443, 38312, 34185, 62830, 58703, 54572, 50445,
    13538, 9411, 5280, 1153, 29798, 25671, 21540, 17413,
    42971, 47098, 34713, 38840, 59231, 63358, 50973, 55100,
    9939, 14066, 1681, 5808, 26199, 30326, 17941, 22068,
    55628, 51565, 63758, 59695, 39368, 35305, 47498, 43435,
    22596, 18533, 30726, 26663, 6336, 2273, 14466, 10403,
    52093, 56156, 60223, 64286, 35833, 39896, 43963, 48026,
    19061, 23124, 27191, 31254, 2801, 6864, 10931, 14994,
    64814, 60687, 56684, 52557, 48554, 44427, 40424, 36297,
    31782, 27655, 23652, 19525, 15522, 11395, 7392, 3265,
    61215, 65342, 53085, 57212, 44955, 49082, 36825, 40952,
    28183, 32310, 20053, 24180, 11923, 16050, 3793, 7920
};

uint16_t crc16(uint8_t *data, int len) {
    uint16_t crc = 0;
    for (int i = 0; i < len; i++) {
        crc = CRC_TABLE[((crc >> 8) ^ data[i]) & 0xFF] ^ (crc << 8);
    }
    return crc;
}

// ========== 패킷 생성/파싱 ==========
int createPacket(uint8_t *buf, uint8_t msgId, uint8_t *payload, int payloadLen) {
    int length = 1 + payloadLen + 2;

    uint8_t crcData[64];
    crcData[0] = msgId;
    memcpy(crcData + 1, payload, payloadLen);
    uint16_t crc = crc16(crcData, 1 + payloadLen);

    buf[0] = SOM;
    buf[1] = length & 0xFF;
    buf[2] = (length >> 8) & 0xFF;
    buf[3] = msgId;
    memcpy(buf + 4, payload, payloadLen);
    buf[4 + payloadLen] = crc & 0xFF;
    buf[5 + payloadLen] = (crc >> 8) & 0xFF;
    buf[6 + payloadLen] = EOM;

    return 7 + payloadLen;
}

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
            }
            else if (payload[0] == MSG_AMBIENT_LEVEL) {
                ambient_level = payload[1];
                Serial.printf("[ACK] Ambient Level: %d\n", ambient_level);
            }
        }

        i = end;
    }
}

// ========== BLE Server Callbacks ==========
class ServerCallbacks: public BLEServerCallbacks {
    void onConnect(BLEServer* pServer) {
        ble_device_connected = true;
        Serial.println("[BLE] Client connected");
    }

    void onDisconnect(BLEServer* pServer) {
        ble_device_connected = false;
        Serial.println("[BLE] Client disconnected");
        // 재광고 시작
        BLEDevice::startAdvertising();
    }
};

class NoiseControlCallbacks: public BLECharacteristicCallbacks {
    void onWrite(BLECharacteristic *pCharacteristic) {
        uint8_t* pData = pCharacteristic->getData();
        size_t len = pCharacteristic->getValue().length();
        if (len > 0 && spp_connected) {
            uint8_t mode = pData[0];

            // 레벨 조정인지 확인 (2바이트)
            if (len == 2) {
                uint8_t msgId = mode;  // 0x83 or 0x84
                uint8_t level = pData[1];

                uint8_t pkt[16];
                uint8_t payload[] = {level};
                int len = createPacket(pkt, msgId, payload, 1);
                esp_spp_write(spp_handle, len, pkt);
                Serial.printf("[BLE->SPP] Set Level: msgId=0x%02X, level=%d\n", msgId, level);
            }
            // 노이즈 모드 변경
            else {
                uint8_t pkt[16];
                uint8_t payload[] = {mode};
                int len = createPacket(pkt, MSG_NOISE_CONTROL, payload, 1);
                esp_spp_write(spp_handle, len, pkt);
                Serial.printf("[BLE->SPP] Noise Control: %d\n", mode);
            }
        }
    }
};

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
            }
            break;

        case ESP_SPP_OPEN_EVT:
            if (param->open.status == ESP_SPP_SUCCESS) {
                spp_handle = param->open.handle;
                spp_connected = true;
                Serial.println("[SPP] Connected to Buds!");

                // 초기 상태 요청
                uint8_t pkt[16];
                uint8_t payload[] = {0xFF};
                int len = createPacket(pkt, MSG_NOISE_CONTROL, payload, 1);
                esp_spp_write(spp_handle, len, pkt);
                Serial.println("[SPP] Query current state");
            } else {
                Serial.printf("[SPP] Connection failed: %d\n", param->open.status);
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

// ========== BLE 초기화 ==========
void initBLE() {
    BLEDevice::init("ESP32-GalaxyBuds");
    pServer = BLEDevice::createServer();
    pServer->setCallbacks(new ServerCallbacks());

    BLEService *pService = pServer->createService(SERVICE_UUID);

    // Noise Control Characteristic (Write)
    pNoiseCharacteristic = pService->createCharacteristic(
                            NOISE_CHAR_UUID,
                            BLECharacteristic::PROPERTY_WRITE
                          );
    pNoiseCharacteristic->setCallbacks(new NoiseControlCallbacks());

    // Battery Characteristic (Read + Notify)
    pBatteryCharacteristic = pService->createCharacteristic(
                              BATTERY_CHAR_UUID,
                              BLECharacteristic::PROPERTY_READ |
                              BLECharacteristic::PROPERTY_NOTIFY
                            );
    pBatteryCharacteristic->addDescriptor(new BLE2902());

    // Status Characteristic (Read + Notify)
    pStatusCharacteristic = pService->createCharacteristic(
                             STATUS_CHAR_UUID,
                             BLECharacteristic::PROPERTY_READ |
                             BLECharacteristic::PROPERTY_NOTIFY
                           );
    pStatusCharacteristic->addDescriptor(new BLE2902());

    pService->start();

    BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
    pAdvertising->addServiceUUID(SERVICE_UUID);
    pAdvertising->setScanResponse(true);
    pAdvertising->setMinPreferred(0x06);
    pAdvertising->setMinPreferred(0x12);
    BLEDevice::startAdvertising();

    Serial.println("[BLE] Server started, advertising...");
}

// ========== Setup & Loop ==========
void setup() {
    Serial.begin(115200);
    delay(1000);

    Serial.println("=== Galaxy Buds 3 Pro Bridge ===");
    Serial.println("BLE Server + SPP Client");

    // BLE 초기화
    initBLE();

    // Bluetooth Classic 초기화
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

    // 콜백 등록
    esp_bt_gap_register_callback(gap_callback);
    esp_spp_register_callback(spp_callback);

    // SPP 초기화
    esp_spp_cfg_t spp_cfg = {
        .mode = ESP_SPP_MODE_CB,
        .enable_l2cap_ertm = false,
    };
    esp_spp_enhanced_init(&spp_cfg);

    Serial.println("Ready! Connecting to Galaxy Buds...");

    // Galaxy Buds 연결 시작
    delay(1000);
    esp_spp_start_discovery(BUDS_MAC);
}

void loop() {
    delay(10);
}
