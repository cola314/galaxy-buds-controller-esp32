using GalaxyBudsController.Models;
using GalaxyBudsController.Services;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace GalaxyBudsController;

public partial class MainPage : ContentPage
{
    private readonly BleService _bleService;
    private readonly List<IDevice> _discoveredDevices = new();
    private BudsStatus _status = new();
    private NoiseControlMode _currentMode = NoiseControlMode.Unknown;
    private CancellationTokenSource? _testConnectionCts;
    private CancellationTokenSource? _commandCts;

    public MainPage()
    {
        InitializeComponent();
        _bleService = new BleService();

        _bleService.DeviceDiscovered += OnDeviceDiscovered;
        _bleService.Connected += OnConnected;
        _bleService.Disconnected += OnDisconnected;
        _bleService.BatteryUpdated += OnBatteryUpdated;
        _bleService.StatusUpdated += OnStatusUpdated;
        _bleService.CommandResult += OnCommandResult;
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        if (!await _bleService.CheckBluetoothAsync())
        {
            await DisplayAlert("Error", "Bluetooth is not available or turned off", "OK");
            return;
        }

        ScanButton.IsEnabled = false;
        ScanButton.Text = "Scanning...";
        _discoveredDevices.Clear();
        DevicePicker.Items.Clear();

        try
        {
            await _bleService.StartScanAsync();
            await Task.Delay(5000); // Scan for 5 seconds
            await _bleService.StopScanAsync();

            if (_discoveredDevices.Count > 0)
            {
                DevicePicker.IsVisible = true;
                ConnectButton.IsVisible = true;
            }
            else
            {
                await DisplayAlert("No Devices", "No ESP32 devices found", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Scan failed: {ex.Message}", "OK");
        }
        finally
        {
            ScanButton.Text = "Scan for ESP32";
            ScanButton.IsEnabled = true;
        }
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (DevicePicker.SelectedIndex < 0)
        {
            await DisplayAlert("Error", "Please select a device", "OK");
            return;
        }

        var device = _discoveredDevices[DevicePicker.SelectedIndex];
        ConnectButton.IsEnabled = false;
        ConnectButton.Text = "Connecting...";

        try
        {
            var success = await _bleService.ConnectAsync(device);
            if (!success)
            {
                await DisplayAlert("Error", "Failed to connect to device", "OK");
                ConnectButton.Text = "Connect";
                ConnectButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Connection failed: {ex.Message}", "OK");
            ConnectButton.Text = "Connect";
            ConnectButton.IsEnabled = true;
        }
    }

    private async void OnOffClicked(object? sender, EventArgs e)
    {
        await SetNoiseControlMode(NoiseControlMode.Off);
    }

    private async void OnAncClicked(object? sender, EventArgs e)
    {
        await SetNoiseControlMode(NoiseControlMode.ANC);
    }

    private async void OnAmbientClicked(object? sender, EventArgs e)
    {
        await SetNoiseControlMode(NoiseControlMode.Ambient);
    }

    private async void OnAdaptiveClicked(object? sender, EventArgs e)
    {
        await SetNoiseControlMode(NoiseControlMode.Adaptive);
    }

    private async Task SetNoiseControlMode(NoiseControlMode mode)
    {
        _commandCts?.Cancel();
        _commandCts = new CancellationTokenSource();

        try
        {
            var success = await _bleService.SetNoiseControlAsync((byte)mode);
            if (!success)
            {
                await DisplayAlert("Error", "Failed to send command to ESP32", "OK");
                return;
            }

            // 5초 타임아웃 대기 (StatusUpdated로 결과 받음)
            var oldMode = _currentMode;
            await Task.Delay(5000, _commandCts.Token);

            // 타임아웃 (모드 변경 안됨)
            if (_currentMode == oldMode)
            {
                await DisplayAlert("Timeout", "No response from Galaxy Buds", "OK");
            }
        }
        catch (OperationCanceledException)
        {
            // StatusUpdated에서 모드 변경됨 (정상)
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Command failed: {ex.Message}", "OK");
        }
    }

    private async void OnLevelChanged(object? sender, ValueChangedEventArgs e)
    {
        var level = (byte)Math.Round(e.NewValue);
        LevelValueLabel.Text = level.ToString();

        if (_currentMode == NoiseControlMode.ANC)
        {
            await _bleService.SetLevelAsync(0x83, level);
        }
        else if (_currentMode == NoiseControlMode.Ambient)
        {
            await _bleService.SetLevelAsync(0x84, level);
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_discoveredDevices.Any(d => d.Id == e.Device.Id))
            {
                _discoveredDevices.Add(e.Device);
                DevicePicker.Items.Add(e.Device.Name ?? "Unknown Device");
            }
        });
    }

    private async void OnTestConnectionClicked(object? sender, EventArgs e)
    {
        _testConnectionCts?.Cancel();
        _testConnectionCts = new CancellationTokenSource();

        TestConnectionButton.IsEnabled = false;
        TestConnectionButton.Text = "Testing...";
        BudsConnectionStatus.Text = "Sending command to ESP32...";
        BudsConnectionStatus.TextColor = Colors.Orange;

        try
        {
            var success = await _bleService.TestBudsConnectionAsync();
            if (!success)
            {
                BudsConnectionStatus.Text = "Failed to send command";
                BudsConnectionStatus.TextColor = Colors.Red;
                TestConnectionButton.Text = "Test Buds Connection";
                TestConnectionButton.IsEnabled = true;
                return;
            }

            // 10초 타임아웃 대기
            await Task.Delay(10000, _testConnectionCts.Token);

            // 타임아웃 발생 (결과 안옴)
            BudsConnectionStatus.Text = "Connection timeout";
            BudsConnectionStatus.TextColor = Colors.Red;
            TestConnectionButton.Text = "Test Buds Connection";
            TestConnectionButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // OnCommandResult에서 결과 받아서 취소됨 (정상)
        }
        catch (Exception ex)
        {
            BudsConnectionStatus.Text = $"Error: {ex.Message}";
            BudsConnectionStatus.TextColor = Colors.Red;
            TestConnectionButton.Text = "Test Buds Connection";
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void OnCommandResult(object? sender, CharacteristicUpdatedEventArgs e)
    {
        if (e.Characteristic.Value.Length >= 1)
        {
            var result = e.Characteristic.Value[0];

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (result == 0x01) // RESULT_SUCCESS
                {
                    BudsConnectionStatus.Text = "Connected successfully";
                    BudsConnectionStatus.TextColor = Colors.Green;
                }
                else // RESULT_FAILED
                {
                    BudsConnectionStatus.Text = "Connection failed";
                    BudsConnectionStatus.TextColor = Colors.Red;
                }

                TestConnectionButton.Text = "Test Buds Connection";
                TestConnectionButton.IsEnabled = true;

                // 타임아웃 취소
                _testConnectionCts?.Cancel();
            });
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionStatus.Text = "Connected";
            ConnectionStatus.TextColor = Colors.Green;
            ConnectButton.Text = "Connected";
            TestConnectionButton.IsEnabled = true;
            EnableControls(true);
        });
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionStatus.Text = "Disconnected";
            ConnectionStatus.TextColor = Colors.Red;
            ConnectButton.Text = "Connect";
            ConnectButton.IsEnabled = true;
            TestConnectionButton.IsEnabled = false;
            EnableControls(false);

            // 연결 끊김 시 상태 초기화
            BudsConnectionStatus.Text = "Not tested";
            BudsConnectionStatus.TextColor = Colors.Gray;
            BatteryLeftLabel.Text = "--";
            BatteryRightLabel.Text = "--";
            WearingStatus.Text = "Not Wearing";
            CurrentModeLabel.Text = "Current Mode: --";
            LevelSlider.Value = 0;
            LevelSlider.IsEnabled = false;
            LevelLabel.Text = "Level: --";

            // 진행 중인 작업 취소
            _testConnectionCts?.Cancel();
            _commandCts?.Cancel();

            DisplayAlert("Connection Lost", "ESP32 connection lost. Please reconnect.", "OK");
        });
    }

    private void OnBatteryUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        if (e.Characteristic.Value.Length >= 2)
        {
            var left = e.Characteristic.Value[0];
            var right = e.Characteristic.Value[1];

            _status.BatteryLeft = left;
            _status.BatteryRight = right;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BatteryLeftLabel.Text = $"{left}%";
                BatteryRightLabel.Text = $"{right}%";
            });
        }
    }

    private void OnStatusUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        if (e.Characteristic.Value.Length >= 3)
        {
            var isWearing = e.Characteristic.Value[0] == 1;
            var mode = (NoiseControlMode)e.Characteristic.Value[1];
            var level = e.Characteristic.Value[2];

            MainThread.BeginInvokeOnMainThread(() =>
            {
                WearingStatus.Text = isWearing ? "Wearing" : "Not Wearing";
                _currentMode = mode;
                UpdateModeDisplay();

                if (mode == NoiseControlMode.ANC || mode == NoiseControlMode.Ambient)
                {
                    LevelSlider.Value = level;
                    LevelSlider.IsEnabled = true;
                    LevelLabel.Text = mode == NoiseControlMode.ANC ? "ANC Level" : "Ambient Level";
                }
                else
                {
                    LevelSlider.IsEnabled = false;
                    LevelLabel.Text = "Level: --";
                }

                // 명령 타임아웃 취소 (상태 업데이트 받음)
                _commandCts?.Cancel();
            });
        }
    }

    private void UpdateModeDisplay()
    {
        CurrentModeLabel.Text = _currentMode switch
        {
            NoiseControlMode.Off => "Current Mode: Off",
            NoiseControlMode.ANC => "Current Mode: ANC",
            NoiseControlMode.Ambient => "Current Mode: Ambient",
            NoiseControlMode.Adaptive => "Current Mode: Adaptive",
            _ => "Current Mode: --"
        };
    }

    private void EnableControls(bool enabled)
    {
        OffButton.IsEnabled = enabled;
        AncButton.IsEnabled = enabled;
        AmbientButton.IsEnabled = enabled;
        AdaptiveButton.IsEnabled = enabled;
    }
}
