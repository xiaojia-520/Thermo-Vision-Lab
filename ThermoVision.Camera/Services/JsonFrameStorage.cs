using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ThermoVision.Camera.Models;

namespace ThermoVision.Camera.Services
{
    public sealed class JsonFrameStorage : IFrameStorage
    {
        public async Task<string> SaveAsync(ThermalFrame frame, CancellationToken cancellationToken)
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ThermoVision",
                "Data",
                frame.CapturedAt.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"frame_{frame.CapturedAt:HHmmssfff}.json");
            var json = new JavaScriptSerializer().Serialize(frame);
            using (var writer = new StreamWriter(path, false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(json);
            }

            return path;
        }
    }
}
