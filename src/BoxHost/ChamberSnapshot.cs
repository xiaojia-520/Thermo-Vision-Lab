using System;
using System.Collections.Generic;

namespace BoxHost
{
    public sealed class ChamberSnapshot
    {
        public bool IsConnected { get; set; }

        public string ConnectionMessage { get; set; }

        public DateTime ReceivedAt { get; set; }

        public bool IsRunning { get; set; }

        public double Temperature { get; set; }

        public double Humidity { get; set; }

        public double? TemperatureSetpoint { get; set; }

        public double? HumiditySetpoint { get; set; }

        public bool HasComponentStatusData { get; set; }

        public bool CompressorOn { get; set; }

        public bool TemperatureControlOn { get; set; }

        public bool HumidityControlOn { get; set; }

        public bool TemperatureRising { get; set; }

        public bool TemperatureHolding { get; set; }

        public bool TemperatureFalling { get; set; }

        public bool HumidityRising { get; set; }

        public bool HumidityHolding { get; set; }

        public bool HumidityFalling { get; set; }

        public bool DrainOn { get; set; }

        public bool ProgramEnded { get; set; }

        public bool LightOn { get; set; }

        public bool ControllerError { get; set; }

        public bool? TotalAlarm { get; set; }

        public IReadOnlyList<string> ActiveAlarms { get; set; }
    }
}
