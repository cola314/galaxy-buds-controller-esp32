# Galaxy Buds 3 Pro - 연결 및 상태 조회 FAQ

## 1. 연결 초기화 시퀀스

### Q: 소켓 연결 후 바로 노이즈 컨트롤 패킷 전송해도 되나요?

**A: 아니요, 초기화 시퀀스가 필요합니다.**

공식 앱은 연결 후 다음 순서로 동작합니다:

```
1. SPP 소켓 연결
2. Extended Status Request (0x61) 전송
3. Extended Status Response 수신 대기 (현재 상태 포함)
4. 이후 노이즈 컨트롤 명령 전송 가능
```

**참조 코드** (`sources/J7/C0153o.java:1238-1239`):

```java
// 연결 성공 후 실행되는 코드
c0153o.I(new C0163i());            // Debug Data Request (0x26) 전송
c0153o.s().c0(38, 3000L);          // 타임아웃 3초 설정
```

### 최소 초기화 시퀀스 (권장)

```
1. SPP 연결
2. 0x61 (Extended Status Request) 전송
3. 응답 대기 (현재 노이즈 컨트롤 모드 포함)
4. 노이즈 컨트롤 명령 전송
```

### 바로 전송하면?

- **가능할 수 있음**: Buds가 명령을 받아들일 수 있음
- **불안정**: 초기 상태 동기화 없이 전송하면 UI와 실제 상태가 불일치할 수 있음
- **권장하지 않음**: 안정성을 위해 상태 조회 후 전송 권장

---

## 2. 현재 상태 조회

### Q: 앱 시작 시 현재 노이즈 컨트롤 모드를 알아내는 방법?

**A: Extended Status Request (0x61)를 전송합니다.**

### 요청 메시지

| 항목 | 값 |
|------|-----|
| MSG_ID | `0x61` (97) |
| Payload | 없음 (0 bytes) |

**패킷 예시**:
```
[FD] [03] [00] [61] [CRC_L] [CRC_H] [DD]
```

### 응답 메시지

| 항목 | 값 |
|------|-----|
| MSG_ID | `0x61` (97) |
| 클래스 | `C0168n` (MsgExtendedStatusUpdated) |

**응답에 포함된 정보** (`sources/L7/C0168n.java`):

| 오프셋 | 필드 | 설명 |
|--------|------|------|
| 0 | revision | 프로토콜 버전 |
| 2 | batteryLeft | 왼쪽 배터리 (%) |
| 3 | batteryRight | 오른쪽 배터리 (%) |
| 4 | isWearing | 착용 여부 |
| ... | ... | ... |
| 19 | **noiseControls** | **현재 노이즈 컨트롤 모드 (0-3)** |
| ... | ... | ... |

**노이즈 컨트롤 모드 값**:
- `0` = Off
- `1` = ANC
- `2` = Ambient Sound
- `3` = Adaptive

### 0x77 응답은?

`0x77` (MsgNoiseControlsUpdate)는 **노이즈 컨트롤 상태가 변경될 때** Buds가 자동으로 보내는 알림입니다.

- **0x61 요청**: 전체 상태 조회 (배터리, 착용, 노이즈 컨트롤 등)
- **0x77 알림**: 노이즈 컨트롤 변경 시 자동 수신

**권장**: 앱 시작 시 `0x61` 요청, 이후 `0x77` 수신 대기

---

## 3. 디바이스 식별

### Q: 페어링된 장치 중 Galaxy Buds 3 Pro를 어떻게 구분하나요?

**A: 디바이스 이름 또는 모델 번호로 식별합니다.**

### 식별 정보

| 항목 | 값 |
|------|-----|
| 디바이스 이름 | `Galaxy Buds3 Pro` |
| 모델 번호 | `SM-R630` |
| SPP UUID | `2e73a4ad-332d-41fc-90e2-16bef06523f2` |

**참조** (`sources/J7/C0153o.java:807`):
```java
intent.putExtra("com.spotify.tap.spoton.extras.MODEL", "Galaxy Buds3 Pro");
intent.putExtra("com.spotify.tap.spoton.extras.DEVICE_NAME", "SM-R630");
```

### 식별 방법

```csharp
// .NET MAUI 예시
foreach (var device in pairedDevices)
{
    string name = device.Name;

    // 방법 1: 이름으로 식별
    if (name.Contains("Galaxy Buds3 Pro") ||
        name.Contains("Buds3 Pro") ||
        name.StartsWith("Galaxy Buds3"))
    {
        // Galaxy Buds 3 Pro 발견
    }

    // 방법 2: UUID로 식별 (SPP 서비스 지원 여부)
    if (device.Uuids.Contains("2e73a4ad-332d-41fc-90e2-16bef06523f2"))
    {
        // Samsung Buds SPP 지원 디바이스
    }
}
```

### 다른 Galaxy Buds 모델들

| 모델 | 이름 패턴 | 모델 번호 |
|------|-----------|-----------|
| Galaxy Buds3 Pro | `Galaxy Buds3 Pro` | SM-R630 |
| Galaxy Buds3 | `Galaxy Buds3` | SM-R530 |
| Galaxy Buds2 Pro | `Galaxy Buds2 Pro` | SM-R510 |
| Galaxy Buds2 | `Galaxy Buds2` | SM-R177 |

---

## 4. macOS/iOS Bluetooth

### Q: .NET MAUI Mac/iOS 타겟에서 SPP 연결이 동일하게 작동하나요?

**A: 아니요, 중요한 차이점이 있습니다.**

### 플랫폼별 차이

| 플랫폼 | Bluetooth 타입 | 프레임워크 | SPP 지원 |
|--------|---------------|------------|----------|
| Android | Classic BT / BLE | Android Bluetooth API | ✅ 직접 지원 |
| macOS | Classic BT / BLE | IOBluetooth | ⚠️ 제한적 |
| iOS | BLE만 | CoreBluetooth | ❌ SPP 불가 |

### iOS 제한 사항

**iOS는 Classic Bluetooth SPP를 지원하지 않습니다.**

- CoreBluetooth는 **BLE (Bluetooth Low Energy)만** 지원
- SPP (Serial Port Profile)는 **Classic Bluetooth** 프로토콜
- MFi (Made for iPhone) 인증 없이는 Classic BT 접근 불가
- Galaxy Buds는 MFi 인증 제품이 아님

### iOS 대안

1. **BLE 프로토콜 사용** (Galaxy Buds가 BLE도 지원하는 경우)
    - LE Audio 지원 모델은 BLE 통신 가능할 수 있음
    - 별도의 BLE 프로토콜 분석 필요

2. **삼성 SDK 사용** (존재하는 경우)
    - 삼성이 iOS용 SDK를 제공하는지 확인 필요
    - Galaxy Wearable iOS 앱이 어떻게 동작하는지 분석 필요

3. **현실적 결론**
    - iOS에서 직접 SPP 연결은 **불가능**
    - macOS는 IOBluetooth로 가능할 수 있으나 제한적

### macOS 구현 (가능성 있음)

```csharp
// macOS에서는 IOBluetooth 사용 가능
// .NET MAUI에서 Platform-specific 코드 필요

#if MACCATALYST
// IOBluetooth를 통한 RFCOMM 연결
// Native binding 필요
#endif
```

### 권장 타겟

| 타겟 | 구현 가능성 | 비고 |
|------|-------------|------|
| Android | ✅ 가능 | BluetoothSocket 직접 사용 |
| Windows | ✅ 가능 | 32feet.NET 또는 Windows.Devices.Bluetooth |
| macOS | ⚠️ 어려움 | IOBluetooth native binding 필요 |
| iOS | ❌ 불가능 | SPP 지원 안 함 |

---

## 5. 요약

### 구현 체크리스트

| 항목 | 상태 | 설명 |
|------|------|------|
| 연결 후 바로 전송 | ⚠️ | 가능하나 권장하지 않음 |
| 현재 상태 조회 | ✅ | 0x61 요청 → 응답에서 noiseControls 필드 읽기 |
| 디바이스 식별 | ✅ | 이름 "Galaxy Buds3 Pro" 또는 UUID 확인 |
| Android 지원 | ✅ | 완전 지원 |
| macOS 지원 | ⚠️ | Native binding 필요, 어려움 |
| iOS 지원 | ❌ | SPP 불가능 |

### 권장 초기화 흐름

```
┌─────────────────────────────────────┐
│ 1. 페어링된 디바이스 검색            │
│    - 이름에 "Galaxy Buds3 Pro" 포함? │
└───────────────┬─────────────────────┘
                ↓
┌─────────────────────────────────────┐
│ 2. SPP 소켓 연결                     │
│    - UUID: 2e73a4ad-...             │
└───────────────┬─────────────────────┘
                ↓
┌─────────────────────────────────────┐
│ 3. Extended Status Request (0x61)   │
│    - 현재 상태 조회                  │
└───────────────┬─────────────────────┘
                ↓
┌─────────────────────────────────────┐
│ 4. 응답 수신                         │
│    - noiseControls 필드 = 현재 모드  │
└───────────────┬─────────────────────┘
                ↓
┌─────────────────────────────────────┐
│ 5. 노이즈 컨트롤 명령 전송           │
│    - 0x78 + mode (0-3)              │
└─────────────────────────────────────┘
```

---

## 6. 참고 파일

```
sources/
├── J7/C0153o.java:1238    # 연결 후 초기화 시퀀스
├── L7/C0168n.java         # Extended Status Response (0x61)
├── L7/M.java              # Noise Controls Update (0x77)
├── J7/O.java:41           # SPP UUID
└── J7/t.java:92           # 디바이스 이름 "Galaxy Buds3 Pro"
```

---

*분석일: 2026-01-17*
