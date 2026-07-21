using System;

namespace ThermoVision.Camera.Models
{
    public sealed class ThermalFrame
    {
        public DateTimeOffset CapturedAt { get; set; }
        public double CenterTemperatureCelsius { get; set; }
        public double Emissivity { get; set; }
        public string CameraIp { get; set; }
    }
}
