using System.Threading;
using System.Threading.Tasks;
using ThermoVision.Camera.Models;

namespace ThermoVision.Camera.Services
{
    public interface IFrameStorage
    {
        Task<string> SaveAsync(ThermalFrame frame, CancellationToken cancellationToken);
    }
}
