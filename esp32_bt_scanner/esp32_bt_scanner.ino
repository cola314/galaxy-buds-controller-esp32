#include "esp_bt.h"
#include "esp_bt_main.h"
#include "esp_bt_device.h"
#include "esp_gap_bt_api.h"
#include "esp_spp_api.h"

// ========== Galaxy Buds 3 Pro 설정 ==========
// Galaxy Buds 3 Pro MAC 주소
uint8_t BUDS_MAC[6] = {0x5C, 0xDC, 0x49, 0x0A, 0x20, 0x3B};

// Galaxy Buds SPP UUID: 2e73a4ad-332d-41fc-90e2-16bef06523f2
static const uint8_t BUDS_SPP_UUID[16] = {
    0x2e, 0x73, 0xa4, 0xad,
    0x33, 0x2d,
    0x41, 0xfc,
    0x90, 0xe2,
    0x16, 0xbe, 0xf0, 0x65, 0x23, 0xf2
};

// ========== 프로토콜 상수 ==========
#define SOM 0xFD
#define EOM 0xDD
#define MSG_NOISE_CONTROL 0x78
#define MSG_EXTENDED_STATUS 0x61
#define MSG_ACK 0x42

// 노이즈 컨트롤 모드
#define NC_OFF 0x00
#define NC_ANC 0x01
#define NC_AMBIENT 0x02
#define NC_ADAPTIVE 0x03

// ========== 상태 변수 ==========
static uint32_t spp_handle = 0;
static bool connected = false;

static int8_t battery_left = -1;
static int8_t battery_right = -1;
static int8_t noise_control = -1;
static bool wearing = false;

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
    int length = 1 + payloadLen + 2;  // MSG_ID + PAYLOAD + CRC

    // CRC 계산용 데이터
    uint8_t crcData[64];
    crcData[0] = msgId;
    memcpy(crcData + 1, payload, payloadLen);
    uint16_t crc = crc16(crcData, 1 + payloadLen);

    // 패킷 생성
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
        if (msgId == MSG_EXTENDED_STATUS && payloadLen > 19) {
            battery_left = payload[2];
            battery_right = payload[3];
            wearing = (payload[4] & 0x01) != 0;
            Serial.printf("[STATUS] Battery: L=%d%% R=%d%%, Wearing: %s\n",
                          battery_left, battery_right, wearing ? "Yes" : "No");
        }
        else if (msgId == MSG_ACK && payloadLen >= 2 && payload[0] == MSG_NOISE_CONTROL) {
            noise_control = payload[1];
            const char *modes[] = {"Off", "ANC", "Ambient", "Adaptive"};
            Serial.printf("[ACK] Noise Control: %s (%d)\n",
                          noise_control < 4 ? modes[noise_control] : "Unknown", noise_control);
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
                    // 첫 번째 채널로 연결
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
                connected = true;
                Serial.println("[SPP] Connected!");

                // 초기 상태 요청 (0x78 + 0xFF)
                uint8_t pkt[16];
                uint8_t payload[] = {0xFF};
                int len = createPacket(pkt, MSG_NOISE_CONTROL, payload, 1);
                esp_spp_write(spp_handle, len, pkt);
                Serial.println("[TX] Query current state");
            } else {
                Serial.printf("[SPP] Connection failed: %d\n", param->open.status);
            }
            break;

        case ESP_SPP_CLOSE_EVT:
            connected = false;
            spp_handle = 0;
            Serial.println("[SPP] Disconnected");
            break;

        case ESP_SPP_DATA_IND_EVT:
            Serial.printf("[RX] %d bytes\n", param->data_ind.len);
            parsePacket(param->data_ind.data, param->data_ind.len);
            break;

        default:
            break;
    }
}

// ========== GAP 콜백 (인증용) ==========
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

// ========== 명령 함수들 ==========
void connectBuds() {
    if (BUDS_MAC[0] == 0 && BUDS_MAC[1] == 0) {
        Serial.println("ERROR: Set BUDS_MAC first!");
        return;
    }

    Serial.printf("Connecting to %02X:%02X:%02X:%02X:%02X:%02X...\n",
                  BUDS_MAC[0], BUDS_MAC[1], BUDS_MAC[2],
                  BUDS_MAC[3], BUDS_MAC[4], BUDS_MAC[5]);

    // UUID로 서비스 검색 후 연결
    esp_spp_start_discovery(BUDS_MAC);
}

void setNoiseControl(uint8_t mode) {
    if (!connected) {
        Serial.println("Not connected!");
        return;
    }

    uint8_t pkt[16];
    uint8_t payload[] = {mode};
    int len = createPacket(pkt, MSG_NOISE_CONTROL, payload, 1);
    esp_spp_write(spp_handle, len, pkt);

    const char *modes[] = {"Off", "ANC", "Ambient", "Adaptive"};
    Serial.printf("[TX] Set Noise Control: %s\n", mode < 4 ? modes[mode] : "?");
}

void printStatus() {
    Serial.println("=== Galaxy Buds 3 Pro Status ===");
    Serial.printf("Connected: %s\n", connected ? "Yes" : "No");
    if (connected) {
        Serial.printf("Battery: L=%d%% R=%d%%\n", battery_left, battery_right);
        Serial.printf("Wearing: %s\n", wearing ? "Yes" : "No");
        const char *modes[] = {"Off", "ANC", "Ambient", "Adaptive"};
        Serial.printf("Noise Control: %s\n", noise_control >= 0 && noise_control < 4 ? modes[noise_control] : "--");
    }
    Serial.println("================================");
}

void printHelp() {
    Serial.println("Commands:");
    Serial.println("  c - Connect to Buds");
    Serial.println("  0 - Noise Control: Off");
    Serial.println("  1 - Noise Control: ANC");
    Serial.println("  2 - Noise Control: Ambient");
    Serial.println("  3 - Noise Control: Adaptive");
    Serial.println("  s - Show status");
    Serial.println("  h - Help");
}

// ========== Setup & Loop ==========
void setup() {
    Serial.begin(115200);
    delay(1000);

    Serial.println("=== Galaxy Buds 3 Pro Controller ===");

    // Bluetooth 초기화
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

    Serial.println("Ready! Type 'h' for help");
    printHelp();
}

void loop() {
    if (Serial.available()) {
        char cmd = Serial.read();

        // 버퍼 비우기
        while (Serial.available()) Serial.read();

        switch (cmd) {
            case 'c': connectBuds(); break;
            case '0': setNoiseControl(NC_OFF); break;
            case '1': setNoiseControl(NC_ANC); break;
            case '2': setNoiseControl(NC_AMBIENT); break;
            case '3': setNoiseControl(NC_ADAPTIVE); break;
            case 's': printStatus(); break;
            case 'h': printHelp(); break;
            default: break;
        }
    }
    delay(10);
}
