using BoxHost;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThermoVision
{
    public partial class ChamberControl : UserControl
    {
        private const string ChamberIp =
            "192.168.1.30";
        private const int ChamberPort = 8000;

        private readonly TomiloChamberService service;
        private ChamberSnapshot latestSnapshot;
        private bool monitoringStarted;
        private bool commandRunning;
        private bool setpointInputsInitialized;
        private bool shutdown;

        public ChamberControl()
        {
            InitializeComponent();

            service = new TomiloChamberService(
                ChamberIp,
                ChamberPort);
            service.SnapshotReceived +=
                Service_SnapshotReceived;
            UpdateControlAvailability();
        }

        public event EventHandler BackRequested;

        public async System.Threading.Tasks.Task
            StartMonitoringAsync()
        {
            if (shutdown || monitoringStarted)
            {
                return;
            }

            monitoringStarted = true;
            ConnectionText.Text = "正在连接";
            MonitoringStatusText.Text =
                "正在建立实验舱连接…";
            await service.StartAsync();
        }

        private void Service_SnapshotReceived(
            object sender,
            ChamberSnapshot snapshot)
        {
            if (shutdown)
            {
                return;
            }

            latestSnapshot = snapshot;

            Dispatcher.BeginInvoke(
                new Action(
                    () => ApplySnapshot(snapshot)));
        }

        private void ApplySnapshot(
            ChamberSnapshot snapshot)
        {
            if (shutdown)
            {
                return;
            }

            LastUpdateText.Text =
                "最后更新：" +
                snapshot.ReceivedAt
                    .ToString("HH:mm:ss");
            MonitoringStatusText.Text =
                snapshot.ConnectionMessage ??
                string.Empty;

            if (!snapshot.IsConnected)
            {
                ConnectionDot.Fill =
                    new SolidColorBrush(
                        Color.FromRgb(
                            220,
                            74,
                            74));
                ConnectionText.Text = "连接中断";
                MonitoringStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            54,
                            54));
                UpdateControlAvailability();
                return;
            }

            ConnectionDot.Fill =
                new SolidColorBrush(
                    Color.FromRgb(
                        38,
                        131,
                        74));
            ConnectionText.Text = "已连接";
            MonitoringStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        113,
                        128,
                        150));

            CurrentTemperatureText.Text =
                snapshot.Temperature
                    .ToString("F1") +
                " ℃";
            CurrentHumidityText.Text =
                snapshot.Humidity
                    .ToString("F1") +
                " %RH";
            TemperatureSetpointText.Text =
                "设定值：" +
                FormatNullable(
                    snapshot.TemperatureSetpoint,
                    " ℃");
            HumiditySetpointText.Text =
                "设定值：" +
                FormatNullable(
                    snapshot.HumiditySetpoint,
                    " %RH");

            if (!setpointInputsInitialized &&
                snapshot.TemperatureSetpoint.HasValue &&
                snapshot.HumiditySetpoint.HasValue)
            {
                TemperatureSetpointTextBox.Text =
                    snapshot.TemperatureSetpoint.Value
                        .ToString("F1");
                HumiditySetpointTextBox.Text =
                    snapshot.HumiditySetpoint.Value
                        .ToString("F1");
                setpointInputsInitialized = true;
            }

            RunStateText.Text =
                snapshot.IsRunning
                    ? "运行中"
                    : "已停止";
            RunStateText.Foreground =
                new SolidColorBrush(
                    snapshot.IsRunning
                        ? Color.FromRgb(
                            38,
                            131,
                            74)
                        : Color.FromRgb(
                            82,
                            98,
                            122));
            PhaseText.Text =
                "温度：" +
                GetTemperaturePhase(snapshot) +
                "　湿度：" +
                GetHumidityPhase(snapshot);

            if (snapshot.HasComponentStatusData)
            {
                SetStateText(
                    CompressorText,
                    snapshot.CompressorOn);
                SetStateText(
                    TemperatureControlText,
                    snapshot.TemperatureControlOn);
                SetStateText(
                    HumidityControlText,
                    snapshot.HumidityControlOn);
                SetStateText(
                    DrainText,
                    snapshot.DrainOn);
                SetStateText(
                    LightText,
                    snapshot.LightOn);
                ProgramEndText.Text =
                    snapshot.ProgramEnded
                        ? "已结束"
                        : "未结束";
                ProgramEndText.Foreground =
                    new SolidColorBrush(
                        snapshot.ProgramEnded
                            ? Color.FromRgb(
                                38,
                                131,
                                74)
                            : Color.FromRgb(
                                82,
                                98,
                                122));
            }
            else
            {
                SetUnavailableText(CompressorText);
                SetUnavailableText(
                    TemperatureControlText);
                SetUnavailableText(
                    HumidityControlText);
                SetUnavailableText(DrainText);
                SetUnavailableText(LightText);
                SetUnavailableText(ProgramEndText);
            }

            ApplyAlarmState(snapshot);
            UpdateControlAvailability();
        }

        private async void SetTemperatureButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            double temperature;
            if (!TryReadSetpoint(
                    TemperatureSetpointTextBox.Text,
                    TomiloChamberService
                        .MinimumTemperatureSetpoint,
                    TomiloChamberService
                        .MaximumTemperatureSetpoint,
                    "温度",
                    out temperature))
            {
                return;
            }

            if (!ConfirmOperation(
                    "确认将实验舱目标温度设置为 " +
                    temperature.ToString("F1") +
                    " ℃？",
                    "确认温度设定"))
            {
                return;
            }

            await ExecuteCommandAsync(
                "正在写入温度并回读确认……",
                async delegate
                {
                    double applied =
                        await service
                            .SetTemperatureSetpointAsync(
                                temperature,
                                CancellationToken.None);
                    TemperatureSetpointTextBox.Text =
                        applied.ToString("F1");
                    return "温度设定成功并已回读确认：" +
                           applied.ToString("F1") +
                           " ℃";
                });
        }

        private async void SetHumidityButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            double humidity;
            if (!TryReadSetpoint(
                    HumiditySetpointTextBox.Text,
                    TomiloChamberService
                        .MinimumHumiditySetpoint,
                    TomiloChamberService
                        .MaximumHumiditySetpoint,
                    "湿度",
                    out humidity))
            {
                return;
            }

            if (!ConfirmOperation(
                    "确认将实验舱目标湿度设置为 " +
                    humidity.ToString("F1") +
                    " %RH？",
                    "确认湿度设定"))
            {
                return;
            }

            await ExecuteCommandAsync(
                "正在写入湿度并回读确认……",
                async delegate
                {
                    double applied =
                        await service
                            .SetHumiditySetpointAsync(
                                humidity,
                                CancellationToken.None);
                    HumiditySetpointTextBox.Text =
                        applied.ToString("F1");
                    return "湿度设定成功并已回读确认：" +
                           applied.ToString("F1") +
                           " %RH";
                });
        }

        private async void StartChamberButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            string setpointSummary =
                latestSnapshot == null
                    ? string.Empty
                    : Environment.NewLine +
                      "当前设定：" +
                      FormatNullable(
                          latestSnapshot
                              .TemperatureSetpoint,
                          " ℃") +
                      "，" +
                      FormatNullable(
                          latestSnapshot
                              .HumiditySetpoint,
                          " %RH");

            if (!ConfirmOperation(
                    "确认启动实验舱？" +
                    setpointSummary,
                    "确认启动"))
            {
                return;
            }

            await ExecuteCommandAsync(
                "正在发送启动命令并确认运行状态……",
                async delegate
                {
                    await service.SetRunningAsync(
                        true,
                        CancellationToken.None);
                    return "启动成功，实验舱已确认进入运行状态。";
                });
        }

        private async void StopChamberButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            if (!ConfirmOperation(
                    "确认停止实验舱？",
                    "确认停止"))
            {
                return;
            }

            await ExecuteCommandAsync(
                "正在发送停止命令并确认停止状态……",
                async delegate
                {
                    await service.SetRunningAsync(
                        false,
                        CancellationToken.None);
                    return "停止成功，实验舱已确认停止。";
                });
        }

        private async Task ExecuteCommandAsync(
            string progressMessage,
            Func<Task<string>> command)
        {
            if (shutdown || commandRunning)
            {
                return;
            }

            commandRunning = true;
            CommandStatusText.Text = progressMessage;
            CommandStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        182,
                        106,
                        0));
            UpdateControlAvailability();

            try
            {
                string successMessage = await command();
                CommandStatusText.Text = successMessage;
                CommandStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            38,
                            131,
                            74));
            }
            catch (Exception exception)
            {
                CommandStatusText.Text =
                    "操作失败：" +
                    exception.Message;
                CommandStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            54,
                            54));

                MessageBox.Show(
                    exception.Message,
                    "实验舱操作失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                commandRunning = false;
                UpdateControlAvailability();
            }
        }

        private void UpdateControlAvailability()
        {
            bool connected =
                !shutdown &&
                latestSnapshot != null &&
                latestSnapshot.IsConnected;
            bool ready =
                connected &&
                !commandRunning;

            TemperatureSetpointTextBox.IsEnabled = ready;
            HumiditySetpointTextBox.IsEnabled = ready;
            SetTemperatureButton.IsEnabled = ready;
            SetHumidityButton.IsEnabled = ready;

            bool hasAlarm =
                connected &&
                (latestSnapshot.TotalAlarm == true ||
                 latestSnapshot.ActiveAlarms != null &&
                 latestSnapshot.ActiveAlarms.Any());

            StartChamberButton.IsEnabled =
                ready &&
                !latestSnapshot.IsRunning &&
                !hasAlarm;
            StopChamberButton.IsEnabled =
                ready &&
                latestSnapshot.IsRunning;
        }

        private static bool TryReadSetpoint(
            string text,
            double minimum,
            double maximum,
            string valueName,
            out double value)
        {
            bool parsed = double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out value) ||
                double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);

            double scaled = value * 10.0;
            bool hasOneDecimalPlace =
                parsed &&
                Math.Abs(
                    scaled -
                    Math.Round(
                        scaled,
                        MidpointRounding.AwayFromZero)) <
                0.000001;

            if (parsed &&
                !double.IsNaN(value) &&
                !double.IsInfinity(value) &&
                value >= minimum &&
                value <= maximum &&
                hasOneDecimalPlace)
            {
                return true;
            }

            MessageBox.Show(
                valueName + "设定值必须在 " +
                minimum.ToString("F1") + " 到 " +
                maximum.ToString("F1") +
                " 之间，并且最多保留一位小数。",
                "设定值无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private static bool ConfirmOperation(
            string message,
            string title)
        {
            return MessageBox.Show(
                       message,
                       title,
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Question) ==
                   MessageBoxResult.Yes;
        }

        private void ApplyAlarmState(
            ChamberSnapshot snapshot)
        {
            bool hasControllerAlarm =
                snapshot.ControllerError;
            bool hasInputAlarm =
                snapshot.TotalAlarm == true ||
                snapshot.ActiveAlarms.Any();

            if (!snapshot.TotalAlarm.HasValue)
            {
                TotalAlarmText.Text =
                    hasControllerAlarm
                        ? "报警状态：控制器异常"
                        : "报警状态：输入暂无数据";
                AlarmDetailsText.Text =
                    hasControllerAlarm
                        ? "控制器 ERROR 状态有效"
                        : "温湿度数据正常读取，报警输入暂不可用。";
                return;
            }

            bool hasAlarm =
                hasControllerAlarm ||
                hasInputAlarm;
            TotalAlarmText.Text =
                hasAlarm
                    ? "报警状态：存在报警"
                    : "报警状态：正常";
            TotalAlarmText.Foreground =
                new SolidColorBrush(
                    hasAlarm
                        ? Color.FromRgb(
                            190,
                            54,
                            54)
                        : Color.FromRgb(
                            38,
                            131,
                            74));

            string alarmText = string.Join(
                "、",
                snapshot.ActiveAlarms);
            if (hasControllerAlarm &&
                !alarmText.Contains(
                    "控制器 ERROR"))
            {
                alarmText =
                    string.IsNullOrEmpty(alarmText)
                        ? "控制器 ERROR 状态"
                        : alarmText +
                          "、控制器 ERROR 状态";
            }

            AlarmDetailsText.Text =
                string.IsNullOrEmpty(alarmText)
                    ? "未检测到活动报警。"
                    : alarmText;
            AlarmDetailsText.Foreground =
                new SolidColorBrush(
                    hasAlarm
                        ? Color.FromRgb(
                            190,
                            54,
                            54)
                        : Color.FromRgb(
                            102,
                            117,
                            139));
        }

        private static string GetTemperaturePhase(
            ChamberSnapshot snapshot)
        {
            if (!snapshot.HasComponentStatusData)
            {
                return "无数据";
            }

            if (snapshot.TemperatureRising)
            {
                return "升温";
            }

            if (snapshot.TemperatureHolding)
            {
                return "恒温";
            }

            if (snapshot.TemperatureFalling)
            {
                return "降温";
            }

            return "待机";
        }

        private static string GetHumidityPhase(
            ChamberSnapshot snapshot)
        {
            if (!snapshot.HasComponentStatusData)
            {
                return "无数据";
            }

            if (snapshot.HumidityRising)
            {
                return "加湿";
            }

            if (snapshot.HumidityHolding)
            {
                return "恒湿";
            }

            if (snapshot.HumidityFalling)
            {
                return "除湿";
            }

            return "待机";
        }

        private static string FormatNullable(
            double? value,
            string suffix)
        {
            return value.HasValue
                ? value.Value.ToString("F1") +
                  suffix
                : "--.-" + suffix;
        }

        private static void SetStateText(
            TextBlock target,
            bool isOn)
        {
            target.Text = isOn ? "开启" : "关闭";
            target.Foreground =
                new SolidColorBrush(
                    isOn
                        ? Color.FromRgb(
                            38,
                            131,
                            74)
                        : Color.FromRgb(
                            82,
                            98,
                            122));
        }

        private static void SetUnavailableText(
            TextBlock target)
        {
            target.Text = "无数据";
            target.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        138,
                        151,
                        169));
        }

        private void BackButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            BackRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        public void Shutdown()
        {
            if (shutdown)
            {
                return;
            }

            shutdown = true;
            service.SnapshotReceived -=
                Service_SnapshotReceived;
            service.Dispose();
        }
    }
}
