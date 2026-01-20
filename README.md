# Galaxy Buds 3 Pro - ESP32 Controller

An ESP32-based controller project for controlling Galaxy Buds 3 Pro from cross-platform devices.

## Architecture

```
[MAUI App (iOS/Windows)]
         ↕ BLE or WiFi
    [ESP32 Controller]
         ↕ Bluetooth Classic SPP
  [Galaxy Buds 3 Pro]
```

## Features

- **Noise Control**: Off / ANC / Ambient / Adaptive
- **Level Adjustment**: ANC Level (0-4), Ambient Level (0-4)
- **Status**: Battery (L/R), Wearing detection, Current mode

## TODO

### ✅ Completed

- [x] ESP32 Bluetooth 기본 기능 테스트
- [x] Galaxy Buds 3 Pro 프로토콜 분석
- [x] Windows Bluetooth 제어 PoC ([GalaxyBudsPoc/](GalaxyBudsPoc/))
- [x] ESP32 SPP Client 기본 구조 작성

### 📋 Planned

- [ ] ESP32 SPP Client 완성 (레벨 조정, Extended Status)
- [ ] ESP32 BLE Server 구현
- [ ] MAUI 크로스 플랫폼 앱 개발 (iOS/Windows)

## Protocol

- **SPP UUID**: `2e73a4ad-332d-41fc-90e2-16bef06523f2`
- **Message IDs**:
  - `0x78`: Noise Control (0=Off, 1=ANC, 2=Ambient, 3=Adaptive)
  - `0x61`: Extended Status Request
  - `0x83`: ANC Level (0-4)
  - `0x84`: Ambient Level (0-4)

See [docs/](docs/) for detailed protocol information.

## References

- [Galaxy Buds Protocol Documentation](docs/)
