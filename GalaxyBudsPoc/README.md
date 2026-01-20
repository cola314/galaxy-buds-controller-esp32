# Galaxy Buds 3 Pro - Windows PoC

Galaxy Buds 3 Pro를 제어하는 Windows 콘솔 애플리케이션입니다.

## 기능

### 노이즈 컨트롤
- **Off**: 노이즈 컨트롤 끄기
- **ANC**: Active Noise Cancelling (노이즈 캔슬링)
- **Ambient**: 주변 소리 듣기
- **Adaptive**: 소음 제어 최적화 (자동)

### 레벨 조정
- **ANC 레벨**: 0~4 단계 조정 (ANC 모드에서)
- **주변음 레벨**: 0~4 단계 조정 (Ambient 모드에서)

### 터치 컨트롤
- 터치 컨트롤 전체 On/Off
- 모든 터치 기능 일괄 제어

### 상태 조회
- 배터리 잔량 (좌/우)
- 착용 상태
- 현재 노이즈 컨트롤 모드
- 터치 컨트롤 상태

## 빌드

```bash
cd GalaxyBudsPoc
dotnet build
```

## 실행

### 대화형 모드

```bash
dotnet run
```

**키보드 단축키:**
- `0` - 노이즈 컨트롤 Off
- `1` - ANC 모드
- `2` - Ambient 모드
- `3` - Adaptive 모드
- `T` - 터치 컨트롤 토글
- `+` - 레벨 증가 (ANC/Ambient)
- `-` - 레벨 감소 (ANC/Ambient)
- `S` - 상태 새로고침
- `Q` - 종료

### 통합 테스트 모드

모든 기능을 자동으로 테스트합니다:

```bash
dotnet run -- --test
```

**테스트 항목:**
1. ✓ Extended Status Request - 전체 상태 조회
2. ✓ Noise Control: Off - 노이즈 컨트롤 끄기
3. ✓ Noise Control: ANC - ANC 모드 전환
4. ✓ ANC Level Control (0-4) - ANC 레벨 0~4 조정
5. ✓ Noise Control: Ambient Sound - Ambient 모드 전환
6. ✓ Ambient Sound Level Control (0-4) - 주변음 레벨 0~4 조정
7. ✓ Touch Control Toggle - 터치 컨트롤 On/Off

## 프로토콜 문서

프로토콜 상세 정보는 다음 문서를 참조하세요:
- [docs/5. Noise Control and Touch Control Protocol.md](../docs/5.%20Noise%20Control%20and%20Touch%20Control%20Protocol.md)

## 요구 사항

- .NET 10.0
- Windows 10/11
- Bluetooth 페어링된 Galaxy Buds 3 Pro

## 예제 출력

### 대화형 모드
```
╔═══════════════════════════════════════╗
║       Galaxy Buds 3 Pro Status        ║
╠═══════════════════════════════════════╣
║  배터리:  L 85%  │ R 83%             ║
║  착용:    착용 중                      ║
║  모드:    ● ANC (노이즈 캔슬링)        ║
║  ANC 레벨: ■■■□□ (3)                  ║
║  터치:    켜짐 ✓                       ║
╚═══════════════════════════════════════╝

  [0] Off    [1] ANC    [2] Ambient    [3] Adaptive
  [T] 터치 컨트롤    [+/-] 레벨 조정
  [S] 새로고침       [Q] 종료

  선택:
```

### 테스트 모드
```
╔═══════════════════════════════════════════════════╗
║  Galaxy Buds 3 Pro - 통합 테스트                ║
╚═══════════════════════════════════════════════════╝

✓ 디바이스 발견: Galaxy Buds3 Pro (203B)
  연결 중... ✓

──────────────────────────────────────────────────
테스트 시작...

✓ [1/7] Extended Status Request
✓ [2/7] Noise Control: Off
✓ [3/7] Noise Control: ANC
✓ [4/7] ANC Level Control (0-4)
✓ [5/7] Noise Control: Ambient Sound
✓ [6/7] Ambient Sound Level Control (0-4)
✓ [7/7] Touch Control Toggle

──────────────────────────────────────────────────

테스트 완료: 7개 성공, 0개 실패

🎉 모든 테스트 통과!

══════════════════════════════════════════════════
최종 상태:
  배터리: L 85% / R 83%
  착용: 착용 중
  노이즈 모드: 2 (Ambient)
  터치 컨트롤: 켜짐
  주변음 레벨: 4
══════════════════════════════════════════════════
```

## 구조

```
GalaxyBudsPoc/
├── Program.cs           - 메인 프로그램 (대화형 UI)
├── IntegrationTest.cs   - 통합 테스트
└── GalaxyBudsPoc.csproj
```

## 주요 메시지 ID

| 기능 | MSG_ID | 값 |
|------|--------|-----|
| Noise Controls | 0x78 | 120 |
| Extended Status | 0x61 | 97 |
| ACK | 0x42 | 66 |
| Lock Touchpad | 0x90 | 144 |
| Ambient Sound Level | 0x84 | 132 |
| Noise Reduction Level | 0x83 | 131 |

## 라이센스

MIT License
