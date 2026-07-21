using System;
using System.Threading;
using System.Threading.Tasks;
using ThermoVision.Camera.Models;

namespace ThermoVision.Camera.Services
{
    // 临时替身：用于验证 WPF 流程。接入设备时，以 YoseenCameraService 替换此类。
    public sealed class DemoCameraService : ICameraService
    {
        private string _cameraIp;

        public bool IsConnected { get; private set; }

        public async Task ConnectAsync(CameraConnectionOptions options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(options.IpAddress))
            {
                throw new ArgumentException("相机 IP 不能为空。", nameof(options));
            }

            await Task.Delay(250, cancellationToken);
            _cameraIp = options.IpAddress;
            IsConnected = true;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public async Task<ThermalFrame> CaptureAsync(double emissivity, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("请先连接相机。");
            }

            await Task.Delay(100, cancellationToken);
            return new ThermalFrame
            {
                CameraIp = _cameraIp,
                CapturedAt = DateTimeOffset.Now,
                CenterTemperatureCelsius = 35.0,
                Emissivity = emissivity
            };
        }
    }
}
