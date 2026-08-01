using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BoxHost
{
    public sealed class TomiloChamberService : IDisposable
    {
        private const byte UnitId = 1;
        private const ushort WorkRegisterStart = 7990;
        private const ushort WorkRegisterCount = 4;
        private const ushort ComponentRegisterStart = 7994;
        private const ushort ComponentRegisterCount = 5;
        private const ushort SetpointRegisterStart = 8024;
        private const ushort SetpointRegisterCount = 2;
        private const ushort AlarmInputStart = 8055;
        private const ushort AlarmInputCount = 36;
        private const ushort StartCoilAddress = 8000;
        private const ushort StopCoilAddress = 8001;
        private const ushort FixedTemperatureRegister = 8100;
        private const ushort FixedHumidityRegister = 8101;

        public const double MinimumTemperatureSetpoint = -100.0;
        public const double MaximumTemperatureSetpoint = 300.0;
        public const double MinimumHumiditySetpoint = 0.0;
        public const double MaximumHumiditySetpoint = 100.0;

        private static readonly IReadOnlyDictionary<int, string>
            AlarmNames = new Dictionary<int, string>
            {
                { 0, "总报警" },
                { 9, "ERR1 / PT1 异常" },
                { 10, "ERR2 / PT2 异常" },
                { 12, "X1 报警" },
                { 13, "X2 报警" },
                { 14, "X3 报警" },
                { 15, "X4 报警" },
                { 16, "X5 报警" },
                { 17, "X6 报警" },
                { 18, "X7 报警" },
                { 19, "X8 报警" },
                { 20, "X9 报警" },
                { 21, "X10 报警" },
                { 22, "X11 报警" },
                { 23, "X12 报警" },
                { 24, "X13 报警" },
                { 25, "X14 报警" },
                { 26, "X15 报警" },
                { 27, "X16 报警" },
                { 28, "X17 报警" },
                { 29, "X18 报警" },
                { 30, "X19 报警" },
                { 31, "X20 报警" },
                { 32, "X21 报警" },
                { 33, "X22 报警" },
                { 34, "温度上下限报警" },
                { 35, "湿度上下限报警" }
            };

        private readonly ModbusTcpClient client;
        private readonly object syncRoot = new object();
        private CancellationTokenSource cancellation;
        private Task pollingTask;
        private bool disposed;

        public TomiloChamberService(
            string host,
            int port)
        {
            client = new ModbusTcpClient(
                host,
                port,
                TimeSpan.FromSeconds(2));
        }

        public event EventHandler<ChamberSnapshot>
            SnapshotReceived;

        public Task StartAsync()
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();

                if (pollingTask != null &&
                    !pollingTask.IsCompleted)
                {
                    return Task.CompletedTask;
                }

                cancellation =
                    new CancellationTokenSource();
                pollingTask = Task.Run(
                    () => PollingLoopAsync(
                        cancellation.Token));
            }

            return Task.CompletedTask;
        }

        public async Task<double> SetTemperatureSetpointAsync(
            double temperature,
            CancellationToken cancellationToken)
        {
            return await SetSetpointAsync(
                    temperature,
                    MinimumTemperatureSetpoint,
                    MaximumTemperatureSetpoint,
                    FixedTemperatureRegister,
                    SetpointRegisterStart,
                    true,
                    "温度",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<double> SetHumiditySetpointAsync(
            double humidity,
            CancellationToken cancellationToken)
        {
            return await SetSetpointAsync(
                    humidity,
                    MinimumHumiditySetpoint,
                    MaximumHumiditySetpoint,
                    FixedHumidityRegister,
                    (ushort)(SetpointRegisterStart + 1),
                    false,
                    "湿度",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task SetRunningAsync(
            bool shouldRun,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            ChamberSnapshot before =
                await ReadSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (before.IsRunning == shouldRun)
            {
                Publish(before);
                return;
            }

            if (shouldRun &&
                (before.TotalAlarm == true ||
                 before.ActiveAlarms.Any()))
            {
                throw new InvalidOperationException(
                    "实验舱存在活动报警，禁止远程启动。");
            }

            await client.WriteSingleCoilAsync(
                    UnitId,
                    shouldRun
                        ? StartCoilAddress
                        : StopCoilAddress,
                    true,
                    cancellationToken)
                .ConfigureAwait(false);

            const int maximumChecks = 12;
            for (int check = 0;
                check < maximumChecks;
                check++)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(250),
                        cancellationToken)
                    .ConfigureAwait(false);

                ushort[] work =
                    await client.ReadInputRegistersAsync(
                            UnitId,
                            WorkRegisterStart,
                            1,
                            cancellationToken)
                        .ConfigureAwait(false);

                if ((work[0] == 1) == shouldRun)
                {
                    ChamberSnapshot snapshot =
                        await ReadSnapshotAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                    Publish(snapshot);
                    return;
                }
            }

            throw new TimeoutException(
                shouldRun
                    ? "启动命令已发送，但没有确认实验舱进入运行状态。"
                    : "停止命令已发送，但没有确认实验舱停止。");
        }

        private async Task<double> SetSetpointAsync(
            double value,
            double minimum,
            double maximum,
            ushort writeRegister,
            ushort readbackRegister,
            bool signedValue,
            string valueName,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < minimum ||
                value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    valueName + "设定值必须在 " +
                    minimum.ToString("F1") + " 到 " +
                    maximum.ToString("F1") + " 之间。");
            }

            double scaledValue = value * 10.0;
            int roundedValue = (int)Math.Round(
                scaledValue,
                MidpointRounding.AwayFromZero);
            if (Math.Abs(scaledValue - roundedValue) >
                0.000001)
            {
                throw new ArgumentException(
                    valueName + "设定值最多保留一位小数。",
                    nameof(value));
            }

            ushort rawValue = signedValue
                ? unchecked((ushort)(short)roundedValue)
                : checked((ushort)roundedValue);

            await client.WriteSingleRegisterAsync(
                    UnitId,
                    writeRegister,
                    rawValue,
                    cancellationToken)
                .ConfigureAwait(false);

            ushort[] holdingReadback =
                await client.ReadHoldingRegistersAsync(
                        UnitId,
                        writeRegister,
                        1,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (holdingReadback[0] != rawValue)
            {
                throw new InvalidOperationException(
                    valueName +
                    "写入应答成功，但保持寄存器回读不一致。");
            }

            const int maximumChecks = 10;
            for (int check = 0;
                check < maximumChecks;
                check++)
            {
                ushort[] inputReadback =
                    await client.ReadInputRegistersAsync(
                            UnitId,
                            readbackRegister,
                            1,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (inputReadback[0] == rawValue)
                {
                    ChamberSnapshot snapshot =
                        await ReadSnapshotAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                    Publish(snapshot);
                    return roundedValue / 10.0;
                }

                await Task.Delay(
                        TimeSpan.FromMilliseconds(200),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                valueName +
                "保持寄存器已更新，但设备显示设定值没有同步。");
        }

        private async Task PollingLoopAsync(
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ChamberSnapshot snapshot =
                        await ReadSnapshotAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                    Publish(snapshot);
                    await Task.Delay(
                            TimeSpan.FromSeconds(1),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken
                        .IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    client.Disconnect();
                    Publish(
                        new ChamberSnapshot
                        {
                            IsConnected = false,
                            ConnectionMessage =
                                ToUserMessage(exception),
                            ReceivedAt = DateTime.Now,
                            ActiveAlarms =
                                Array.Empty<string>()
                        });

                    try
                    {
                        await Task.Delay(
                                TimeSpan.FromSeconds(2),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task<ChamberSnapshot>
            ReadSnapshotAsync(
                CancellationToken cancellationToken)
        {
            ushort[] work =
                await client.ReadInputRegistersAsync(
                        UnitId,
                        WorkRegisterStart,
                        WorkRegisterCount,
                        cancellationToken)
                    .ConfigureAwait(false);

            ushort[] setpoints = null;
            string optionalMessage = null;
            try
            {
                setpoints =
                    await client.ReadInputRegistersAsync(
                            UnitId,
                            SetpointRegisterStart,
                            SetpointRegisterCount,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!(exception is
                    OperationCanceledException))
            {
                optionalMessage =
                    "设定值暂不可用：" +
                    ToUserMessage(exception);
            }

            ushort[] components = null;
            try
            {
                components =
                    await client.ReadHoldingRegistersAsync(
                            UnitId,
                            ComponentRegisterStart,
                            ComponentRegisterCount,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!(exception is
                    OperationCanceledException))
            {
                string componentMessage =
                    "部件状态暂不可用：" +
                    ToUserMessage(exception);
                optionalMessage =
                    AppendOptionalMessage(
                        optionalMessage,
                        componentMessage);
            }

            bool[] alarms = null;
            try
            {
                alarms =
                    await client.ReadDiscreteInputsAsync(
                            UnitId,
                            AlarmInputStart,
                            AlarmInputCount,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (!(exception is
                    OperationCanceledException))
            {
                string alarmMessage =
                    "报警输入暂不可用：" +
                    ToUserMessage(exception);
                optionalMessage =
                    AppendOptionalMessage(
                        optionalMessage,
                        alarmMessage);
            }

            bool isRunning = work[0] == 1;
            ushort status =
                components == null
                    ? (ushort)0
                    : components[2];
            bool hasComponentStatusData =
                components != null &&
                (!isRunning ||
                 status != 0);

            if (components != null &&
                isRunning &&
                status == 0)
            {
                optionalMessage =
                    AppendOptionalMessage(
                        optionalMessage,
                        "部件状态暂不可用：设备返回全 0");
            }

            List<string> activeAlarms =
                alarms == null
                    ? new List<string>()
                    : AlarmNames
                        .Where(pair =>
                            alarms[pair.Key])
                        .Select(pair => pair.Value)
                        .ToList();

            return new ChamberSnapshot
            {
                IsConnected = true,
                ConnectionMessage =
                    string.IsNullOrEmpty(optionalMessage)
                        ? "通讯正常"
                        : optionalMessage,
                ReceivedAt = DateTime.Now,
                IsRunning = isRunning,
                Temperature =
                    (short)work[1] / 10.0,
                Humidity = work[2] / 10.0,
                TemperatureSetpoint =
                    setpoints == null
                        ? (double?)null
                        : (short)setpoints[0] / 10.0,
                HumiditySetpoint =
                    setpoints == null
                        ? (double?)null
                        : setpoints[1] / 10.0,
                HasComponentStatusData =
                    hasComponentStatusData,
                CompressorOn = IsBitSet(status, 0),
                TemperatureControlOn =
                    IsBitSet(status, 2),
                HumidityControlOn =
                    IsBitSet(status, 3),
                TemperatureRising =
                    IsBitSet(status, 6),
                TemperatureHolding =
                    IsBitSet(status, 7),
                TemperatureFalling =
                    IsBitSet(status, 8),
                HumidityRising =
                    IsBitSet(status, 9),
                HumidityHolding =
                    IsBitSet(status, 10),
                HumidityFalling =
                    IsBitSet(status, 11),
                DrainOn = IsBitSet(status, 12),
                ControllerError =
                    IsBitSet(status, 13),
                ProgramEnded = IsBitSet(status, 14),
                LightOn = IsBitSet(status, 15),
                TotalAlarm =
                    alarms == null
                        ? (bool?)null
                        : alarms[0],
                ActiveAlarms = activeAlarms
            };
        }

        private static string AppendOptionalMessage(
            string currentMessage,
            string newMessage)
        {
            return string.IsNullOrEmpty(currentMessage)
                ? newMessage
                : currentMessage + "；" +
                  newMessage;
        }

        private void Publish(
            ChamberSnapshot snapshot)
        {
            EventHandler<ChamberSnapshot> handler =
                SnapshotReceived;
            handler?.Invoke(this, snapshot);
        }

        private static bool IsBitSet(
            ushort value,
            int bit)
        {
            return (value & (1 << bit)) != 0;
        }

        private static string ToUserMessage(
            Exception exception)
        {
            ModbusProtocolException protocolException =
                exception as ModbusProtocolException;
            if (protocolException != null)
            {
                return "设备返回 Modbus 异常码 " +
                       protocolException.ExceptionCode;
            }

            if (exception is TimeoutException)
            {
                return "连接或通信超时";
            }

            if (exception is SocketException)
            {
                return "无法连接设备";
            }

            return exception.Message;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(TomiloChamberService));
            }
        }

        public void Dispose()
        {
            Task taskToWait;

            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                cancellation?.Cancel();
                taskToWait = pollingTask;
            }

            client.Disconnect();

            if (taskToWait != null)
            {
                try
                {
                    taskToWait.Wait(
                        TimeSpan.FromSeconds(3));
                }
                catch (AggregateException)
                {
                    // Shutdown is best-effort.
                }
            }

            cancellation?.Dispose();
            client.Dispose();
        }
    }
}
