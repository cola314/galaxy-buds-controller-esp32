using System.Net.Sockets;
using System.Text;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace GalaxyBudsPoc;

class Program
{
    private static readonly Guid SppUuid = new("2e73a4ad-332d-41fc-90e2-16bef06523f2");

    private const byte MsgIdNoiseControls = 0x78;
    private const byte MsgIdExtendedStatusRequest = 0x61;
    private const byte MsgIdAck = 0x42;

    private const byte SOM = 0xFD;
    private const byte EOM = 0xDD;

    private static BudsStatus _status = new();
    private static readonly object _lock = new();
    private static CancellationTokenSource? _cts;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        try
        {
            await RunAsync();
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    static async Task RunAsync()
    {
        Console.WriteLine("=== Galaxy Buds 3 Pro ===\n");

        using var client = new BluetoothClient();
        var device = client.PairedDevices
            .FirstOrDefault(d => d.DeviceName.Contains("Buds", StringComparison.OrdinalIgnoreCase));

        if (device == null)
        {
            Console.WriteLine("Galaxy Buds를 찾을 수 없습니다.");
            return;
        }

        Console.WriteLine($"디바이스: {device.DeviceName}");
        Console.WriteLine("연결 중...");

        try
        {
            client.Connect(device.DeviceAddress, SppUuid);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"연결 실패: {ex.Message}");
            return;
        }

        using var stream = client.GetStream();

        // Start listener
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(stream, _cts.Token));

        // Query current state: 0x78 with 0xFF triggers 0x61 + 0x42 ACK with current mode
        await SendPacketAsync(stream, MsgIdNoiseControls, [0xFF]);
        await Task.Delay(600);

        // Main loop
        while (true)
        {
            DrawUI();

            var key = Console.ReadKey(true);
            switch (key.KeyChar)
            {
                case '0':
                    await SendPacketAsync(stream, MsgIdNoiseControls, [0x00]);
                    break;
                case '1':
                    await SendPacketAsync(stream, MsgIdNoiseControls, [0x01]);
                    break;
                case '2':
                    await SendPacketAsync(stream, MsgIdNoiseControls, [0x02]);
                    break;
                case '3':
                    await SendPacketAsync(stream, MsgIdNoiseControls, [0x03]);
                    break;
                case 's':
                case 'S':
                    await SendPacketAsync(stream, MsgIdExtendedStatusRequest, []);
                    break;
                case 'q':
                case 'Q':
                    _cts.Cancel();
                    return;
            }
            await Task.Delay(300);
        }
    }

    static void DrawUI()
    {
        Console.Clear();
        Console.WriteLine("╔═══════════════════════════════════════╗");
        Console.WriteLine("║       Galaxy Buds 3 Pro Status        ║");
        Console.WriteLine("╠═══════════════════════════════════════╣");

        lock (_lock)
        {
            var left = _status.BatteryLeft >= 0 ? $"{_status.BatteryLeft}%" : "--";
            var right = _status.BatteryRight >= 0 ? $"{_status.BatteryRight}%" : "--";
            Console.WriteLine($"║  배터리:  L {left,-4} │ R {right,-4}          ║");

            var wearing = _status.IsWearing switch
            {
                true => "착용 중",
                false => "미착용",
                _ => "--"
            };
            Console.WriteLine($"║  착용:    {wearing,-27} ║");

            var (icon, name) = _status.NoiseControl switch
            {
                0 => ("○", "Off (끄기)"),
                1 => ("●", "ANC (노이즈 캔슬링)"),
                2 => ("◐", "Ambient (주변 소리)"),
                3 => ("◑", "Adaptive (최적화)"),
                _ => ("?", "--")
            };
            Console.WriteLine($"║  모드:    {icon} {name,-23} ║");
        }

        Console.WriteLine("╚═══════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("  [0] Off    [1] ANC    [2] Ambient    [3] Adaptive");
        Console.WriteLine("  [S] 새로고침              [Q] 종료");
        Console.WriteLine();
        Console.Write("  선택: ");
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
                    if (payload.Length > 19)
                    {
                        _status.BatteryLeft = payload[2];
                        _status.BatteryRight = payload[3];
                        _status.IsWearing = (payload[4] & 0x01) != 0;
                        // offset 19는 부정확할 수 있음, ACK에서 가져옴
                    }
                    break;

                case MsgIdAck: // 0x42 - ACK with current mode
                    if (payload.Length >= 2 && payload[0] == MsgIdNoiseControls)
                    {
                        _status.NoiseControl = payload[1];
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

class BudsStatus
{
    public int BatteryLeft { get; set; } = -1;
    public int BatteryRight { get; set; } = -1;
    public bool? IsWearing { get; set; }
    public int NoiseControl { get; set; } = -1;
}
