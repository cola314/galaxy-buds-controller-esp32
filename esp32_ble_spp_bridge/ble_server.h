#ifndef BLE_SERVER_H
#define BLE_SERVER_H

#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <BLE2902.h>
#include "buds_protocol.h"

// ========== BLE UUIDs (MAUI 앱과 통신용) ==========
#define SERVICE_UUID        "12345678-1234-5678-1234-56789abcdef0"
#define NOISE_CHAR_UUID     "12345678-1234-5678-1234-56789abcdef1"
#define BATTERY_CHAR_UUID   "12345678-1234-5678-1234-56789abcdef2"
#define STATUS_CHAR_UUID    "12345678-1234-5678-1234-56789abcdef3"
#define COMMAND_CHAR_UUID   "12345678-1234-5678-1234-56789abcdef4"

// 명령 코드
#define CMD_TEST_CONNECTION 0x01  // Buds 연결 테스트

// 연결 결과 코드
#define RESULT_SUCCESS 0x01
#define RESULT_FAILED 0x00

// ========== 외부 변수 선언 (main .ino에서 정의) ==========
extern BLEServer* pServer;
extern BLECharacteristic* pNoiseCharacteristic;
extern BLECharacteristic* pBatteryCharacteristic;
extern BLECharacteristic* pStatusCharacteristic;
extern BLECharacteristic* pCommandCharacteristic;
extern bool ble_device_connected;
extern volatile bool connect_requested;
extern volatile bool disconnect_after_status;
extern volatile bool connection_test_requested;
extern bool spp_connected;
extern uint32_t spp_handle;
extern int8_t battery_left;
extern int8_t battery_right;
extern int8_t noise_control;
extern int8_t anc_level;
extern int8_t ambient_level;
extern bool wearing;

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

class CommandCallbacks: public BLECharacteristicCallbacks {
    void onWrite(BLECharacteristic *pCharacteristic) {
        uint8_t* pData = pCharacteristic->getData();
        size_t len = pCharacteristic->getValue().length();
        if (len > 0) {
            uint8_t cmd = pData[0];
            if (cmd == CMD_TEST_CONNECTION) {
                Serial.println("[BLE] Test connection requested");
                connect_requested = true;
                connection_test_requested = true;
            }
        }
    }
};

// 보류 중인 노이즈 컨트롤 명령 (main .ino에서 정의)
struct PendingCommand;
extern PendingCommand pending_noise_cmd;

class NoiseControlCallbacks: public BLECharacteristicCallbacks {
    void onWrite(BLECharacteristic *pCharacteristic) {
        uint8_t* pData = pCharacteristic->getData();
        size_t len = pCharacteristic->getValue().length();
        if (len > 0) {
            // 명령 저장
            pending_noise_cmd.has_command = true;
            pending_noise_cmd.len = len;
            memcpy(pending_noise_cmd.data, pData, len);

            // Buds 연결되어 있지 않으면 연결 요청
            if (!spp_connected) {
                Serial.println("[BLE] Noise control requested, connecting to Buds...");
                connect_requested = true;
            } else {
                // 이미 연결되어 있으면 바로 전송
                uint8_t mode = pData[0];

                // 레벨 조정인지 확인 (2바이트)
                if (len == 2) {
                    uint8_t msgId = mode;  // 0x83 or 0x84
                    uint8_t level = pData[1];

                    uint8_t pkt[16];
                    uint8_t payload[] = {level};
                    int pktLen = createPacket(pkt, msgId, payload, 1);
                    esp_spp_write(spp_handle, pktLen, pkt);
                    Serial.printf("[BLE->SPP] Set Level: msgId=0x%02X, level=%d\n", msgId, level);
                }
                // 노이즈 모드 변경
                else {
                    uint8_t pkt[16];
                    uint8_t payload[] = {mode};
                    int pktLen = createPacket(pkt, MSG_NOISE_CONTROL, payload, 1);
                    esp_spp_write(spp_handle, pktLen, pkt);
                    Serial.printf("[BLE->SPP] Noise Control: %d\n", mode);
                }

                pending_noise_cmd.has_command = false;
            }
        }
    }
};

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

    // Command Characteristic (Write + Read + Notify)
    pCommandCharacteristic = pService->createCharacteristic(
                              COMMAND_CHAR_UUID,
                              BLECharacteristic::PROPERTY_WRITE |
                              BLECharacteristic::PROPERTY_READ |
                              BLECharacteristic::PROPERTY_NOTIFY
                            );
    pCommandCharacteristic->setCallbacks(new CommandCallbacks());
    pCommandCharacteristic->addDescriptor(new BLE2902());

    pService->start();

    BLEAdvertising *pAdvertising = BLEDevice::getAdvertising();
    pAdvertising->addServiceUUID(SERVICE_UUID);
    pAdvertising->setScanResponse(true);
    pAdvertising->setMinPreferred(0x06);
    pAdvertising->setMinPreferred(0x12);
    BLEDevice::startAdvertising();

    Serial.println("[BLE] Server started, advertising...");
}

#endif
