using System.Net.Sockets;
using System.Text;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace GalaxyBudsPoc;

class Program
{
    // Galaxy Buds 3 Pro SPP UUID
    private static readonly Guid SppUuid = new("2e73a4ad-332d-41fc-90e2-16bef06523f2");

    // Noise Control Modes
    private const byte NoiseControlOff = 0x00;
    private const byte NoiseControlAnc = 0x01;
    private const byte NoiseControlAmbient = 0x02;
    private const byte NoiseControlAdaptive = 0x03;

    // Message IDs
    private const byte MsgIdNoiseControls = 0x78;
    private const byte MsgIdExtendedStatusRequest = 0x61;

    // Packet markers
    private const byte SOM = 0xFD;
    private const byte EOM = 0xDD;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== Galaxy Buds 3 Pro PoC ===\n");

        // Find paired Galaxy Buds
        var device = await FindGalaxyBudsAsync();
        if (device == null)
        {
            Console.WriteLine("Galaxy Buds 3 Pro를 찾을 수 없습니다.");
            Console.WriteLine("페어링되어 있는지 확인하세요.");
            return;
        }

        Console.WriteLine($"디바이스 발견: {device.DeviceName}");
        Console.WriteLine($"주소: {device.DeviceAddress}\n");

        // Connect via SPP
        using var client = new BluetoothClient();
        Console.WriteLine("연결 중...");

        try
        {
            client.Connect(device.DeviceAddress, SppUuid);
            Console.WriteLine("연결 성공!\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"연결 실패: {ex.Message}");
            return;
        }

        using var stream = client.GetStream();

        // Request extended status first
        Console.WriteLine("상태 요청 중 (0x61)...");
        var statusPacket = CreatePacket(MsgIdExtendedStatusRequest, []);
        await stream.WriteAsync(statusPacket);
        await stream.FlushAsync();

        // Wait for response
        await Task.Delay(500);
        await ReadResponseAsync(stream);

        // Interactive menu
        while (true)
        {
            Console.WriteLine("\n노이즈 컨트롤 모드 선택:");
            Console.WriteLine("  0. Off (끄기)");
            Console.WriteLine("  1. ANC (노이즈 캔슬링)");
            Console.WriteLine("  2. Ambient (주변 소리)");
            Console.WriteLine("  3. Adaptive (소음 제어 최적화)");
            Console.WriteLine("  q. 종료");
            Console.Write("\n선택: ");

            var input = Console.ReadLine()?.Trim().ToLower();

            if (input == "q") break;

            if (byte.TryParse(input, out var mode) && mode <= 3)
            {
                var modeName = mode switch
                {
                    0 => "Off",
                    1 => "ANC",
                    2 => "Ambient",
                    3 => "Adaptive",
                    _ => "Unknown"
                };

                Console.WriteLine($"\n{modeName} 모드로 변경 중...");

                var packet = CreatePacket(MsgIdNoiseControls, [mode]);
                PrintPacket("TX", packet);

                await stream.WriteAsync(packet);
                await stream.FlushAsync();

                await Task.Delay(300);
                await ReadResponseAsync(stream);

                Console.WriteLine("명령 전송 완료!");
            }
            else
            {
                Console.WriteLine("잘못된 입력입니다.");
            }
        }

        Console.WriteLine("\n연결 종료.");
    }

    static async Task<BluetoothDeviceInfo?> FindGalaxyBudsAsync()
    {
        Console.WriteLine("페어링된 디바이스 검색 중...\n");

        using var client = new BluetoothClient();
        var devices = client.PairedDevices;

        foreach (var device in devices)
        {
            Console.WriteLine($"  - {device.DeviceName} ({device.DeviceAddress})");

            if (device.DeviceName.Contains("Galaxy Buds3 Pro", StringComparison.OrdinalIgnoreCase) ||
                device.DeviceName.Contains("Buds3 Pro", StringComparison.OrdinalIgnoreCase) ||
                device.DeviceName.Contains("Galaxy Buds 3 Pro", StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        Console.WriteLine();
        return null;
    }

    static byte[] CreatePacket(byte msgId, byte[] payload)
    {
        // Length = MSG_ID(1) + PAYLOAD(N) + CRC(2)
        int length = 1 + payload.Length + 2;

        // Build CRC data: MSG_ID + PAYLOAD
        var crcData = new byte[1 + payload.Length];
        crcData[0] = msgId;
        Array.Copy(payload, 0, crcData, 1, payload.Length);

        ushort crc = Crc16(crcData);

        // Build packet
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
        {
            crc = (ushort)(CrcTable[((crc >> 8) ^ b) & 0xFF] ^ (crc << 8));
        }
        return crc;
    }

    static async Task ReadResponseAsync(Stream stream)
    {
        var buffer = new byte[1024];
        try
        {
            if (stream is NetworkStream ns && ns.DataAvailable)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (bytesRead > 0)
                {
                    PrintPacket("RX", buffer[..bytesRead]);
                    ParseResponse(buffer[..bytesRead]);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"응답 읽기 오류: {ex.Message}");
        }
    }

    static void ParseResponse(byte[] data)
    {
        if (data.Length < 7 || data[0] != SOM || data[^1] != EOM)
        {
            return;
        }

        var msgId = data[3];

        switch (msgId)
        {
            case 0x61: // Extended Status Response
                Console.WriteLine("  [Extended Status 응답 수신]");
                if (data.Length > 22)
                {
                    var noiseControl = data[22]; // offset 19 in payload
                    var modeName = noiseControl switch
                    {
                        0 => "Off",
                        1 => "ANC",
                        2 => "Ambient",
                        3 => "Adaptive",
                        _ => $"Unknown ({noiseControl})"
                    };
                    Console.WriteLine($"  현재 노이즈 컨트롤: {modeName}");
                }
                break;

            case 0x77: // Noise Controls Update
                Console.WriteLine("  [Noise Control 업데이트 수신]");
                if (data.Length > 4)
                {
                    var noiseControl = data[4];
                    var modeName = noiseControl switch
                    {
                        0 => "Off",
                        1 => "ANC",
                        2 => "Ambient",
                        3 => "Adaptive",
                        _ => $"Unknown ({noiseControl})"
                    };
                    Console.WriteLine($"  노이즈 컨트롤 모드: {modeName}");
                }
                break;
        }
    }

    static void PrintPacket(string prefix, byte[] packet)
    {
        var hex = BitConverter.ToString(packet).Replace("-", " ");
        Console.WriteLine($"  [{prefix}] {hex}");
    }
}
