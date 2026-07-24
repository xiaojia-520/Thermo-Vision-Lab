using BoxHost;
using System;
using System.Linq;
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
        private bool monitoringStarted;
        private bool shutdown;

        public ChamberControl()
        {
            InitializeComponent();

            service = new TomiloChamberService(
                ChamberIp,
                ChamberPort);
            service.SnapshotReceived +=
                Service_SnapshotReceived;
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
                "正在建立只读连接…";
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
