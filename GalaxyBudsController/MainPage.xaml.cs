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

    public MainPage()
    {
        InitializeComponent();
        _bleService = new BleService();

        _bleService.DeviceDiscovered += OnDeviceDiscovered;
        _bleService.Connected += OnConnected;
        _bleService.Disconnected += OnDisconnected;
        _bleService.BatteryUpdated += OnBatteryUpdated;
        _bleService.StatusUpdated += OnStatusUpdated;
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
        var success = await _bleService.SetNoiseControlAsync((byte)mode);
        if (success)
        {
            _currentMode = mode;
            UpdateModeDisplay();
        }
        else
        {
            await DisplayAlert("Error", "Failed to set noise control mode", "OK");
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

    private void OnConnected(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionStatus.Text = "Connected";
            ConnectionStatus.TextColor = Colors.Green;
            ConnectButton.Text = "Connected";
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
            EnableControls(false);
        });
    }

    private void OnBatteryUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        if (e.Characteristic.Value.Length >= 2)
        {
            var left = e.Characteristic.Value[0];
            var right = e.Characteristic.Value[1];

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
