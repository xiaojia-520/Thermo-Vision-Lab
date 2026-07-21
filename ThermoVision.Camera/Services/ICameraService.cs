using System.Threading;
using System.Threading.Tasks;
using ThermoVision.Camera.Models;

namespace ThermoVision.Camera.Services
{
    public interface ICameraService
    {
        bool IsConnected { get; }

        Task ConnectAsync(CameraConnectionOptions options, CancellationToken cancellationToken);
        Task DisconnectAsync();
        Task<ThermalFrame> CaptureAsync(double emissivity, CancellationToken cancellationToken);
    }
}
