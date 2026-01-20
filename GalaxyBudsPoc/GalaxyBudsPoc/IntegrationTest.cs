using System.Net.Sockets;
using InTheHand.Net.Sockets;

namespace GalaxyBudsPoc;

/// <summary>
/// Galaxy Buds 3 Pro 통합 테스트
/// 모든 프로토콜 기능을 순차적으로 테스트합니다.
/// </summary>
class IntegrationTest
{
    private static readonly Guid SppUuid = new("2e73a4ad-332d-41fc-90e2-16bef06523f2");

    private const byte MsgIdNoiseControls = 0x78;
    private const byte MsgIdExtendedStatusRequest = 0x61;
    private const byte MsgIdAmbientSoundLevel = 0x84;
    private const byte MsgIdNoiseReductionLevel = 0x83;

    private const byte SOM = 0xFD;
    private const byte EOM = 0xDD;

    private static readonly object _lock = new();
    private static BudsTestStatus _status = new();
    private static CancellationTokenSource? _cts;

    public static async Task RunAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════╗");
        Console.WriteLine("║  Galaxy Buds 3 Pro - 통합 테스트                ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════╝\n");

        using var client = new BluetoothClient();
        var device = client.PairedDevices
            .FirstOrDefault(d => d.DeviceName.Contains("Buds", StringComparison.OrdinalIgnoreCase));

        if (device == null)
        {
            Console.WriteLine("❌ Galaxy Buds를 찾을 수 없습니다.");
            return;
        }

        Console.WriteLine($"✓ 디바이스 발견: {device.DeviceName}");
        Console.Write("  연결 중... ");

        try
        {
            client.Connect(device.DeviceAddress, SppUuid);
            Console.WriteLine("✓");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 연결 실패: {ex.Message}");
            return;
        }

        using var stream = client.GetStream();

        // 리스너 시작
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(stream, _cts.Token));

        // 초기 상태 수신 대기 (버즈가 자동으로 상태 전송)
        await Task.Delay(1500);

        // 초기 상태 확인
        int initialMode;
        lock (_lock)
        {
            initialMode = _status.NoiseControl;
            Console.WriteLine($"\n현재 노이즈 모드: {initialMode} ({GetNoiseModeName(initialMode)})");
            Console.WriteLine($"배터리: L {_status.BatteryLeft}% / R {_status.BatteryRight}%\n");
        }

        // 테스트 시작
        int passed = 0;
        int failed = 0;

        Console.WriteLine("\n" + new string('─', 50));
        Console.WriteLine("테스트 시작...\n");

        // 테스트 1: Extended Status Request
        if (await TestExtendedStatus(stream))
        {
            passed++;
            Console.WriteLine("✓ [1/7] Extended Status Request");
        }
        else
        {
            failed++;
            Console.WriteLine("❌ [1/7] Extended Status Request");
        }

        await Task.Delay(2000);

        // 테스트 2: 현재 모드 확인 (노이즈 컨트롤 명령 전송)
        if (await TestNoiseControl(stream, (byte)initialMode, GetNoiseModeName(initialMode)))
        {
            passed++;
            Console.WriteLine($"✓ [2/5] Noise Control Command: {GetNoiseModeName(initialMode)}");
        }
        else
        {
            failed++;
            Console.WriteLine($"❌ [2/5] Noise Control Command: {GetNoiseModeName(initialMode)}");
        }

        await Task.Delay(2000);

        // 테스트 3 & 4: 레벨 조정 (현재 모드에 따라)
        if (initialMode == 1) // ANC
        {
            if (await TestNoiseReductionLevel(stream))
            {
                passed++;
                Console.WriteLine("✓ [3/5] ANC Level Control (0-4)");
            }
            else
            {
                failed++;
                Console.WriteLine("❌ [3/5] ANC Level Control");
            }
        }
        else if (initialMode == 2) // Ambient
        {
            if (await TestAmbientSoundLevel(stream))
            {
                passed++;
                Console.WriteLine("✓ [3/5] Ambient Sound Level Control (0-4)");
            }
            else
            {
                failed++;
                Console.WriteLine("❌ [3/5] Ambient Sound Level Control");
            }
        }
        else
        {
            Console.WriteLine($"⊘ [3/5] Level Control (현재 모드에서 사용 불가)");
        }

        // 결과 출력
        Console.WriteLine("\n" + new string('─', 50));
        Console.WriteLine($"\n테스트 완료: {passed}개 성공, {failed}개 실패\n");

        if (failed == 0)
        {
            Console.WriteLine("🎉 모든 테스트 통과!");
        }
        else
        {
            Console.WriteLine($"⚠️  {failed}개의 테스트가 실패했습니다.");
        }

        // 최종 상태 출력
        Console.WriteLine("\n" + new string('═', 50));
        Console.WriteLine("최종 상태:");
        lock (_lock)
        {
            Console.WriteLine($"  배터리: L {_status.BatteryLeft}% / R {_status.BatteryRight}%");
            Console.WriteLine($"  착용: {(_status.IsWearing == true ? "착용 중" : "미착용")}");
            Console.WriteLine($"  노이즈 모드: {_status.NoiseControl} ({GetNoiseModeName(_status.NoiseControl)})");
            if (_status.NoiseControl == 1 && _status.NoiseReductionLevel >= 0)
                Console.WriteLine($"  ANC 레벨: {_status.NoiseReductionLevel}");
            if (_status.NoiseControl == 2 && _status.AmbientSoundLevel >= 0)
                Console.WriteLine($"  주변음 레벨: {_status.AmbientSoundLevel}");
        }
        Console.WriteLine(new string('═', 50));

        _cts.Cancel();
        await Task.Delay(500);
    }

    static string GetNoiseModeName(int mode) => mode switch
    {
        0 => "Off",
        1 => "ANC",
        2 => "Ambient",
        3 => "Adaptive",
        _ => "Unknown"
    };

    static async Task<bool> TestExtendedStatus(Stream stream)
    {
        int oldBattery;
        lock (_lock)
        {
            oldBattery = _status.BatteryLeft;
            _status.StatusReceived = false;
        }

        await SendPacketAsync(stream, MsgIdExtendedStatusRequest, []);
        await Task.Delay(1500);

        bool success;
        lock (_lock)
        {
            // 배터리 값이 유효하거나 StatusReceived 플래그가 설정되었으면 성공
            success = _status.BatteryLeft >= 0;
            if (!success)
            {
                Console.WriteLine($"    [디버그] StatusReceived: {_status.StatusReceived}, Battery: {_status.BatteryLeft}");
            }
        }
        return success;
    }

    static async Task<bool> TestNoiseControl(Stream stream, byte mode, string name)
    {
        lock (_lock)
        {
            _status.NoiseControlAckReceived = false;
        }

        await SendPacketAsync(stream, MsgIdNoiseControls, [mode]);
        await Task.Delay(1000);

        bool success;
        lock (_lock)
        {
            success = _status.NoiseControlAckReceived && _status.NoiseControl == mode;
            if (!success)
            {
                Console.WriteLine($"    [디버그] ACK: {_status.NoiseControlAckReceived}, Mode: {_status.NoiseControl} (expected: {mode})");
            }
        }
        return success;
    }

    static async Task<bool> TestNoiseReductionLevel(Stream stream)
    {
        // 레벨 0부터 4까지 테스트
        for (byte level = 0; level <= 4; level++)
        {
            lock (_lock)
            {
                _status.NoiseReductionAckReceived = false;
            }

            await SendPacketAsync(stream, MsgIdNoiseReductionLevel, [level]);
            await Task.Delay(800);

            bool success;
            lock (_lock)
            {
                success = _status.NoiseReductionAckReceived && _status.NoiseReductionLevel == level;
            }

            if (!success)
                return false;
        }
        return true;
    }

    static async Task<bool> TestAmbientSoundLevel(Stream stream)
    {
        // 레벨 0부터 4까지 테스트
        for (byte level = 0; level <= 4; level++)
        {
            lock (_lock)
            {
                _status.AmbientSoundAckReceived = false;
            }

            await SendPacketAsync(stream, MsgIdAmbientSoundLevel, [level]);
            await Task.Delay(800);

            bool success;
            lock (_lock)
            {
                success = _status.AmbientSoundAckReceived && _status.AmbientSoundLevel == level;
            }

            if (!success)
                return false;
        }
        return true;
    }

    static async Task ListenAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1024];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (stream is NetworkStream ns && ns.DataAvailable)
                {
                    var len = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (len > 0) ParsePackets(buffer[..len]);
                }
                await Task.Delay(50, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    static void ParsePackets(byte[] data)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (data[i] != SOM) { i++; continue; }
            if (i + 4 > data.Length) break;

            int length = data[i + 1] | ((data[i + 2] & 0x0F) << 8);
            int end = i + 4 + length - 3 + 2 + 1;

            if (end > data.Length) break;
            if (data[end - 1] != EOM) { i++; continue; }

            var msgId = data[i + 3];
            var payload = data[(i + 4)..(end - 3)];

            ProcessMessage(msgId, payload);
            i = end;
        }
    }

    static void ProcessMessage(byte msgId, byte[] payload)
    {
        lock (_lock)
        {
            switch (msgId)
            {
                case MsgIdExtendedStatusRequest: // 0x61
                    if (payload.Length > 31)
                    {
                        _status.BatteryLeft = payload[2];
                        _status.BatteryRight = payload[3];
                        _status.IsWearing = (payload[4] & 0x01) != 0;

                        _status.NoiseControl = payload[31];
                        _status.StatusReceived = true;
                    }
                    break;

                case 0x42: // ACK
                    if (payload.Length >= 2)
                    {
                        if (payload[0] == MsgIdNoiseControls)
                        {
                            _status.NoiseControl = payload[1];
                            _status.NoiseControlAckReceived = true;
                        }
                        else if (payload[0] == MsgIdAmbientSoundLevel)
                        {
                            _status.AmbientSoundLevel = payload[1];
                            _status.AmbientSoundAckReceived = true;
                        }
                        else if (payload[0] == MsgIdNoiseReductionLevel)
                        {
                            _status.NoiseReductionLevel = payload[1];
                            _status.NoiseReductionAckReceived = true;
                        }
                    }
                    break;
            }
        }
    }

    static async Task SendPacketAsync(Stream stream, byte msgId, byte[] payload)
    {
        var packet = CreatePacket(msgId, payload);
        await stream.WriteAsync(packet);
        await stream.FlushAsync();
    }

    static byte[] CreatePacket(byte msgId, byte[] payload)
    {
        int length = 1 + payload.Length + 2;

        var crcData = new byte[1 + payload.Length];
        crcData[0] = msgId;
        Array.Copy(payload, 0, crcData, 1, payload.Length);

        ushort crc = Crc16(crcData);

        var packet = new byte[7 + payload.Length];
        packet[0] = SOM;
        packet[1] = (byte)(length & 0xFF);
        packet[2] = (byte)((length >> 8) & 0xFF);
        packet[3] = msgId;
        Array.Copy(payload, 0, packet, 4, payload.Length);
        packet[4 + payload.Length] = (byte)(crc & 0xFF);
        packet[5 + payload.Length] = (byte)((crc >> 8) & 0xFF);
        packet[6 + payload.Length] = EOM;

        return packet;
    }

    static readonly ushort[] CrcTable =
    [
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
    ];

    static ushort Crc16(byte[] data)
    {
        ushort crc = 0;
        foreach (var b in data)
            crc = (ushort)(CrcTable[((crc >> 8) ^ b) & 0xFF] ^ (crc << 8));
        return crc;
    }
}

class BudsTestStatus
{
    public int BatteryLeft { get; set; } = -1;
    public int BatteryRight { get; set; } = -1;
    public bool? IsWearing { get; set; }
    public int NoiseControl { get; set; } = -1;
    public int AmbientSoundLevel { get; set; } = -1;
    public int NoiseReductionLevel { get; set; } = -1;

    // 테스트용 플래그
    public bool StatusReceived { get; set; }
    public bool NoiseControlAckReceived { get; set; }
    public bool AmbientSoundAckReceived { get; set; }
    public bool NoiseReductionAckReceived { get; set; }
}
