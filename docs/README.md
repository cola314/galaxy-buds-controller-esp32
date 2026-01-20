# Galaxy Buds 3 Pro - Bluetooth 프로토콜 분석

## 1. 개요

Galaxy Buds 3 Pro와 통신하기 위한 Bluetooth SPP 프로토콜 분석.
이 문서를 바탕으로 노이즈 컨트롤 앱을 밑바닥부터 구현할 수 있습니다.

---

## 2. Bluetooth 연결 정보

### 2.1 SPP UUID

```java
UUID SPP_UUID = UUID.fromString("2e73a4ad-332d-41fc-90e2-16bef06523f2");
```

**참조**: `sources/J7/O.java:41`

### 2.2 연결 방식

- **프로토콜**: Bluetooth SPP (Serial Port Profile)
- **통신**: `BluetoothSocket` → `InputStream` / `OutputStream`

---

## 3. 패킷 구조

### 3.1 전체 패킷 포맷

```
┌──────┬──────────┬──────────┬────────┬─────────────┬──────────┬──────────┬──────┐
│ SOM  │ LEN_LOW  │ LEN_HIGH │ MSG_ID │   PAYLOAD   │ CRC_LOW  │ CRC_HIGH │ EOM  │
├──────┼──────────┼──────────┼────────┼─────────────┼──────────┼──────────┼──────┤
│ 0xFD │   0xXX   │   0xXX   │  0xXX  │  ... bytes  │   0xXX   │   0xXX   │ 0xDD │
└──────┴──────────┴──────────┴────────┴─────────────┴──────────┴──────────┴──────┘

총 길이 = 7 + payload_length
```

### 3.2 필드 설명

| 필드 | 크기 | 설명 |
|------|------|------|
| SOM (Start of Message) | 1 byte | 항상 `0xFD` |
| LEN_LOW | 1 byte | 길이 하위 바이트 (payload + 3) |
| LEN_HIGH | 1 byte | 길이 상위 바이트 + 플래그 |
| MSG_ID | 1 byte | 메시지 ID |
| PAYLOAD | N bytes | 실제 데이터 |
| CRC_LOW | 1 byte | CRC16 하위 바이트 |
| CRC_HIGH | 1 byte | CRC16 상위 바이트 |
| EOM (End of Message) | 1 byte | 항상 `0xDD` |

### 3.3 길이 필드 상세

```java
int length = payload_length + 3;  // MSG_ID(1) + PAYLOAD(N) + CRC(2)
byte len_low = (byte) (length & 0xFF);
byte len_high = (byte) ((length >> 8) & 0xFF);

// 플래그 (len_high에 OR)
if (isResponse) len_high |= 0x20;  // bit 5
if (isFragment) len_high |= 0x10;  // bit 4
```

**참조**: `sources/L7/C0155a.java:680-695`

---

## 4. CRC16 계산

### 4.1 CRC 알고리즘

**참조**: `sources/W6/d.java` (메서드 `b()`)

```java
public static int crc16(int length, byte[] data) {
    // CRC16-CCITT (0x1021) 알고리즘 사용 추정
    // 실제 구현은 W6.d.b() 참조
}
```

### 4.2 CRC 적용 범위

```
CRC 계산 대상 = [MSG_ID] + [PAYLOAD]
```

---

## 5. 노이즈 컨트롤 메시지

### 5.1 메시지 ID

```java
MSG_ID_NOISE_CONTROLS = 0x78;  // 120 (decimal)
```

**참조**: `sources/L7/L.java:46`

### 5.2 페이로드

```java
// 페이로드: 1 byte
byte payload = mode;  // 0, 1, 2, 또는 3
```

| 값 | 모드 |
|----|------|
| `0x00` | Off (끄기) |
| `0x01` | ANC (액티브 노이즈 캔슬링) |
| `0x02` | Ambient Sound (주변 소리 듣기) |
| `0x03` | Adaptive (소음 제어 최적화) |

### 5.3 전체 패킷 예시

**ANC 모드로 변경 (mode = 1)**:

```
계산:
- payload = [0x01]
- payload_length = 1
- length = 1 + 3 = 4
- crc_data = [0x78, 0x01]
- crc = CRC16(crc_data)

패킷:
[0xFD] [0x04] [0x00] [0x78] [0x01] [CRC_L] [CRC_H] [0xDD]
  │      │      │      │      │       │       │      │
  │      │      │      │      │       └───────┴──────┴─ CRC + EOM
  │      │      │      │      └─ payload (mode=1=ANC)
  │      │      │      └─ MSG_ID (0x78=노이즈컨트롤)
  │      │      └─ len_high
  │      └─ len_low (4)
  └─ SOM
```

---

## 6. 패킷 생성 코드 (Java)

### 6.1 패킷 직렬화

```java
public byte[] createNoiseControlPacket(byte mode) {
    byte msgId = 0x78;
    byte[] payload = new byte[] { mode };

    int length = payload.length + 3;
    byte[] packet = new byte[payload.length + 7];

    // SOM
    packet[0] = (byte) 0xFD;

    // Length
    packet[1] = (byte) (length & 0xFF);
    packet[2] = (byte) ((length >> 8) & 0xFF);

    // MSG_ID + Payload
    byte[] crcData = new byte[1 + payload.length + 2];
    crcData[0] = msgId;
    System.arraycopy(payload, 0, crcData, 1, payload.length);

    // CRC 계산
    int crc = calculateCRC16(crcData, 0, 1 + payload.length);
    crcData[1 + payload.length] = (byte) (crc & 0xFF);
    crcData[1 + payload.length + 1] = (byte) ((crc >> 8) & 0xFF);

    // 패킷에 복사
    System.arraycopy(crcData, 0, packet, 3, crcData.length);

    // EOM
    packet[packet.length - 1] = (byte) 0xDD;

    return packet;
}
```

### 6.2 전송

```java
public void sendNoiseControlCommand(BluetoothSocket socket, byte mode) {
    byte[] packet = createNoiseControlPacket(mode);
    OutputStream out = socket.getOutputStream();
    out.write(packet);
    out.flush();
}

// 사용 예시
sendNoiseControlCommand(socket, (byte) 0x00);  // Off
sendNoiseControlCommand(socket, (byte) 0x01);  // ANC
sendNoiseControlCommand(socket, (byte) 0x02);  // Ambient
sendNoiseControlCommand(socket, (byte) 0x03);  // Adaptive
```

---

## 7. 응답 메시지

### 7.1 노이즈 컨트롤 업데이트 (Buds → Phone)

**MSG_ID**: `0x77` (119)

**참조**: `sources/L7/C0155a.java:313-325`

```java
// MsgNoiseControlsUpdate
// 페이로드:
// byte[0] = noiseControlsUpdate (현재 모드)
// byte[1] = wearingState (착용 상태)
```

### 7.2 상태 업데이트 (Buds → Phone)

**MSG_ID**: `0x60` (96)

**참조**: `sources/L7/C0155a.java:97-169`

```java
// MsgStatusUpdated - 배터리, 착용 상태 등
// byte[0] = revision
// byte[1] = batteryLeft
// byte[2] = batteryRight
// byte[3] = isWearing (1=착용, 0=미착용)
// ... (추가 필드)
```

---

## 8. 디바이스 상태 필드 매핑

### 8.1 주요 필드 (Z6.b 클래스)

**참조**: `sources/Z6/b.java`

| 필드명 | 타입 | 설명 |
|--------|------|------|
| `l0` | int | 노이즈 컨트롤 모드 (0-3) |
| `f9481l` | boolean | 왼쪽 이어버드 착용 |
| `f9482m` | boolean | 오른쪽 이어버드 착용 |
| `f9485n0` | int | Ambient Sound 레벨 (0-4) |
| `f9440R` | int | ANC 레벨 (0-4) |
| `f9506y0` | boolean | 한쪽만 착용시 노이즈 컨트롤 허용 |
| `f9473g` | int | 왼쪽 배터리 (%) |
| `h` | int | 오른쪽 배터리 (%) |
| `f9475i` | int | 통합 배터리 (%) |
| `f9442S` | boolean | 대화 감지시 자동 Ambient |
| `f9409B` | boolean | 통화 중 자동 Ambient |
| `f9454Y0` | boolean | 사이렌 감지시 자동 Ambient |

---

## 9. 전체 통신 흐름

```
┌─────────────────┐                    ┌─────────────────┐
│   Android App   │                    │  Galaxy Buds 3  │
│                 │                    │      Pro        │
└────────┬────────┘                    └────────┬────────┘
         │                                      │
         │ 1. BluetoothAdapter.getRemoteDevice() │
         │──────────────────────────────────────>│
         │                                      │
         │ 2. device.createRfcommSocketToServiceRecord(UUID)
         │──────────────────────────────────────>│
         │                                      │
         │ 3. socket.connect()                  │
         │──────────────────────────────────────>│
         │                                      │
         │ 4. 노이즈 컨트롤 패킷 전송            │
         │  [FD][04][00][78][01][CRC][CRC][DD]  │
         │──────────────────────────────────────>│
         │                                      │
         │ 5. 상태 응답 수신                    │
         │  [FD][..][..][77][..][CRC][CRC][DD]  │
         │<──────────────────────────────────────│
         │                                      │
```

---

## 10. 앱 구현 체크리스트

### 10.1 필수 권한 (AndroidManifest.xml)

```xml
<uses-permission android:name="android.permission.BLUETOOTH" />
<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
<uses-permission android:name="android.permission.BLUETOOTH_SCAN" />
```

### 10.2 구현 순서

1. **Bluetooth 어댑터 초기화**
2. **페어링된 디바이스 목록에서 Galaxy Buds 3 Pro 찾기**
3. **SPP UUID로 소켓 연결**
4. **패킷 생성 함수 구현** (CRC 포함)
5. **명령 전송**
6. **응답 파싱** (선택)

### 10.3 핵심 구현 코드

```java
// 1. UUID
UUID SPP_UUID = UUID.fromString("2e73a4ad-332d-41fc-90e2-16bef06523f2");

// 2. 연결
BluetoothDevice device = bluetoothAdapter.getRemoteDevice(macAddress);
BluetoothSocket socket = device.createRfcommSocketToServiceRecord(SPP_UUID);
socket.connect();

// 3. 노이즈 컨트롤 전송
OutputStream out = socket.getOutputStream();

// Off
out.write(createPacket((byte)0x78, new byte[]{0x00}));

// ANC
out.write(createPacket((byte)0x78, new byte[]{0x01}));

// Ambient Sound
out.write(createPacket((byte)0x78, new byte[]{0x02}));

// Adaptive
out.write(createPacket((byte)0x78, new byte[]{0x03}));
```

---

## 11. 참고 파일 경로

```
galaxy_buds3_pro_manager_decompiled/sources/

L7/
├── C0155a.java          ★ 메시지 베이스 클래스 (패킷 구조)
├── L.java               ★ 노이즈 컨트롤 메시지
└── M.java               # 노이즈 컨트롤 업데이트 응답

J7/
├── O.java               ★ SPP 연결 매니저 (UUID, 전송)
└── C0153o.java          # 메인 통신 매니저

Z6/
└── b.java               ★ 디바이스 상태 저장

W6/
└── d.java               # CRC 계산 (메서드 b)
```

---

## 12. CRC16 구현 (정확한 알고리즘)

**참조**: `sources/W6/d.java:94-100`

### 12.1 CRC 테이블

```java
public static final int[] CRC_TABLE = {
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
```

### 12.2 CRC 계산 함수

```java
/**
 * CRC16 계산 (Galaxy Buds 프로토콜용)
 * @param length 데이터 길이
 * @param data 데이터 배열
 * @return CRC16 값 (0-65535)
 */
public static int crc16(int length, byte[] data) {
    int crc = 0;
    for (int i = 0; i < length; i++) {
        crc = CRC_TABLE[((crc >> 8) ^ data[i]) & 0xFF] ^ (crc << 8);
    }
    return crc & 0xFFFF;
}
```

### 12.3 CRC 적용 예시

```java
// 노이즈 컨트롤 ANC 명령 (mode=1)
byte[] crcData = new byte[] { 0x78, 0x01 };  // MSG_ID + payload
int crc = crc16(crcData.length, crcData);

byte crcLow = (byte) (crc & 0xFF);
byte crcHigh = (byte) ((crc >> 8) & 0xFF);
```

---

*분석일: 2026-01-17*
*이 문서로 Galaxy Buds 3 Pro 노이즈 컨트롤 앱 구현 가능*
