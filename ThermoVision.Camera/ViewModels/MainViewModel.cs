using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ThermoVision.Camera.Models;
using ThermoVision.Camera.Services;

namespace ThermoVision.Camera.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly ICameraService _cameraService;
        private readonly IFrameStorage _frameStorage;
        private string _cameraIp = "192.168.1.201";
        private string _connectionStatus = "未连接（演示服务）";
        private double _emissivity = 0.97;
        private string _centerTemperatureText = "-- °C";
        private string _lastCaptureTimeText = "尚未采集";

        public MainViewModel(ICameraService cameraService, IFrameStorage frameStorage)
        {
            _cameraService = cameraService;
            _frameStorage = frameStorage;
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !_cameraService.IsConnected);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => _cameraService.IsConnected);
            CaptureCommand = new AsyncRelayCommand(CaptureAsync, () => _cameraService.IsConnected);
        }

        public AsyncRelayCommand ConnectCommand { get; }
        public AsyncRelayCommand DisconnectCommand { get; }
        public AsyncRelayCommand CaptureCommand { get; }

        public string CameraIp { get => _cameraIp; set => SetProperty(ref _cameraIp, value); }
        public string ConnectionStatus { get => _connectionStatus; private set => SetProperty(ref _connectionStatus, value); }
        public double Emissivity { get => _emissivity; set => SetProperty(ref _emissivity, value); }
        public string CenterTemperatureText { get => _centerTemperatureText; private set => SetProperty(ref _centerTemperatureText, value); }
        public string LastCaptureTimeText { get => _lastCaptureTimeText; private set => SetProperty(ref _lastCaptureTimeText, value); }

        private async Task ConnectAsync()
        {
            ConnectionStatus = "连接中…";
            try
            {
                await _cameraService.ConnectAsync(new CameraConnectionOptions { IpAddress = CameraIp }, CancellationToken.None);
                ConnectionStatus = "已连接（演示服务；尚未调用 Yoseen SDK）";
            }
            catch (Exception exception)
            {
                ConnectionStatus = "连接失败：" + exception.Message;
            }
            finally { RefreshCommands(); }
        }

        private async Task DisconnectAsync()
        {
            await _cameraService.DisconnectAsync();
            ConnectionStatus = "已断开";
            RefreshCommands();
        }

        private async Task CaptureAsync()
        {
            try
            {
                var frame = await _cameraService.CaptureAsync(Emissivity, CancellationToken.None);
                var path = await _frameStorage.SaveAsync(frame, CancellationToken.None);
                CenterTemperatureText = frame.CenterTemperatureCelsius.ToString("F1", CultureInfo.InvariantCulture) + " °C";
                LastCaptureTimeText = frame.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + Environment.NewLine + path;
                ConnectionStatus = "采集元数据已保存";
            }
            catch (Exception exception)
            {
                ConnectionStatus = "采集失败：" + exception.Message;
            }
        }

        private void RefreshCommands()
        {
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            CaptureCommand.RaiseCanExecuteChanged();
        }
    }
}
