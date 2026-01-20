using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System.Text;

namespace GalaxyBudsController.Services;

public class BleService
{
    private readonly IBluetoothLE _bluetoothLE;
    private readonly IAdapter _adapter;
    private IDevice? _connectedDevice;
    private IService? _esp32Service;
    private ICharacteristic? _noiseControlCharacteristic;
    private ICharacteristic? _batteryCharacteristic;
    private ICharacteristic? _statusCharacteristic;
    private ICharacteristic? _commandCharacteristic;

    // ESP32 BLE Service UUID (사용자가 ESP32에서 정의할 UUID)
    private static readonly Guid ServiceUuid = Guid.Parse("12345678-1234-5678-1234-56789abcdef0");
    private static readonly Guid NoiseControlCharacteristicUuid = Guid.Parse("12345678-1234-5678-1234-56789abcdef1");
    private static readonly Guid BatteryCharacteristicUuid = Guid.Parse("12345678-1234-5678-1234-56789abcdef2");
    private static readonly Guid StatusCharacteristicUuid = Guid.Parse("12345678-1234-5678-1234-56789abcdef3");
    private static readonly Guid CommandCharacteristicUuid = Guid.Parse("12345678-1234-5678-1234-56789abcdef4");

    public event EventHandler<DeviceEventArgs>? DeviceDiscovered;
    public event EventHandler<CharacteristicUpdatedEventArgs>? BatteryUpdated;
    public event EventHandler<CharacteristicUpdatedEventArgs>? StatusUpdated;
    public event EventHandler<CharacteristicUpdatedEventArgs>? CommandResult;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public bool IsScanning => _adapter.IsScanning;
    public bool IsConnected => _connectedDevice?.State == Plugin.BLE.Abstractions.DeviceState.Connected;

    public BleService()
    {
        _bluetoothLE = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.DeviceConnected += OnDeviceConnected;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
    }

    public async Task<bool> CheckBluetoothAsync()
    {
        if (!_bluetoothLE.IsAvailable)
            return false;

        if (!_bluetoothLE.IsOn)
            return false;

        return true;
    }

    public async Task StartScanAsync()
    {
        if (!await CheckBluetoothAsync())
            throw new Exception("Bluetooth is not available or turned off");

        if (_adapter.IsScanning)
            await _adapter.StopScanningForDevicesAsync();

        await _adapter.StartScanningForDevicesAsync();
    }

    public async Task StopScanAsync()
    {
        if (_adapter.IsScanning)
            await _adapter.StopScanningForDevicesAsync();
    }

    public async Task<bool> ConnectAsync(IDevice device)
    {
        try
        {
            await _adapter.ConnectToDeviceAsync(device);
            _connectedDevice = device;

            // Discover services
            var services = await device.GetServicesAsync();
            _esp32Service = services.FirstOrDefault(s => s.Id == ServiceUuid);

            if (_esp32Service == null)
                return false;

            // Get characteristics
            var characteristics = await _esp32Service.GetCharacteristicsAsync();
            _noiseControlCharacteristic = characteristics.FirstOrDefault(c => c.Id == NoiseControlCharacteristicUuid);
            _batteryCharacteristic = characteristics.FirstOrDefault(c => c.Id == BatteryCharacteristicUuid);
            _statusCharacteristic = characteristics.FirstOrDefault(c => c.Id == StatusCharacteristicUuid);
            _commandCharacteristic = characteristics.FirstOrDefault(c => c.Id == CommandCharacteristicUuid);

            // Subscribe to notifications
            if (_batteryCharacteristic != null)
            {
                _batteryCharacteristic.ValueUpdated += OnBatteryCharacteristicUpdated;
                await _batteryCharacteristic.StartUpdatesAsync();
            }

            if (_statusCharacteristic != null)
            {
                _statusCharacteristic.ValueUpdated += OnStatusCharacteristicUpdated;
                await _statusCharacteristic.StartUpdatesAsync();
            }

            if (_commandCharacteristic != null)
            {
                _commandCharacteristic.ValueUpdated += OnCommandCharacteristicUpdated;
                await _commandCharacteristic.StartUpdatesAsync();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connectedDevice != null)
        {
            await _adapter.DisconnectDeviceAsync(_connectedDevice);
            _connectedDevice = null;
        }
    }

    public async Task<bool> SetNoiseControlAsync(byte mode)
    {
        if (_noiseControlCharacteristic == null || !IsConnected)
            return false;

        try
        {
            await _noiseControlCharacteristic.WriteAsync(new[] { mode });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SetLevelAsync(byte msgId, byte level)
    {
        if (_noiseControlCharacteristic == null || !IsConnected)
            return false;

        try
        {
            await _noiseControlCharacteristic.WriteAsync(new[] { msgId, level });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TestBudsConnectionAsync()
    {
        if (_commandCharacteristic == null || !IsConnected)
            return false;

        try
        {
            // CMD_TEST_CONNECTION = 0x01
            await _commandCharacteristic.WriteAsync(new byte[] { 0x01 });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        DeviceDiscovered?.Invoke(this, e);
    }

    private void OnDeviceConnected(object? sender, DeviceEventArgs e)
    {
        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnBatteryCharacteristicUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        BatteryUpdated?.Invoke(this, e);
    }

    private void OnStatusCharacteristicUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        StatusUpdated?.Invoke(this, e);
    }

    private void OnCommandCharacteristicUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        CommandResult?.Invoke(this, e);
    }
}
