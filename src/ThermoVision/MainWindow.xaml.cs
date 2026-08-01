using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ThermoVision
{
    public partial class MainWindow : Window
    {
        private const int RestoreWindow = 9;
        private const string InfraredCameraIp =
            "192.168.1.201";

        private readonly DispatcherTimer
            infraredConnectionTimer;
        private bool infraredConnectionCheckRunning;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(
            IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(
            IntPtr windowHandle,
            int command);

        public MainWindow()
        {
            InitializeComponent();

            infraredConnectionTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
            infraredConnectionTimer.Tick +=
                InfraredConnectionTimer_Tick;
        }

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs eventArgs)
        {
            infraredConnectionTimer.Start();
            await RefreshInfraredConnectionAsync();

            try
            {
                await ChamberControlView
                    .StartMonitoringAsync();
            }
            catch
            {
                // ChamberControl 页面会持续显示连接状态。
            }
        }

        private async void InfraredConnectionTimer_Tick(
            object sender,
            EventArgs eventArgs)
        {
            await RefreshInfraredConnectionAsync();
        }

        private async System.Threading.Tasks.Task
            RefreshInfraredConnectionAsync()
        {
            if (infraredConnectionCheckRunning)
            {
                return;
            }

            infraredConnectionCheckRunning = true;

            try
            {
                bool connected;
                using (Ping ping = new Ping())
                {
                    PingReply reply =
                        await ping.SendPingAsync(
                            InfraredCameraIp,
                            800);
                    connected =
                        reply.Status ==
                        IPStatus.Success;
                }

                ApplyInfraredConnectionState(
                    connected);
            }
            catch
            {
                ApplyInfraredConnectionState(false);
            }
            finally
            {
                infraredConnectionCheckRunning = false;
            }
        }

        private void ApplyInfraredConnectionState(
            bool connected)
        {
            InfraredConnectionText.Text =
                connected
                    ? "已连接"
                    : "未连接";
            InfraredConnectionText.Foreground =
                new SolidColorBrush(
                    connected
                        ? Color.FromRgb(
                            38,
                            131,
                            74)
                        : Color.FromRgb(
                            190,
                            54,
                            54));
            InfraredConnectionBadge.Background =
                new SolidColorBrush(
                    connected
                        ? Color.FromRgb(
                            234,
                            247,
                            239)
                        : Color.FromRgb(
                            253,
                            238,
                            238));
        }

        private async void AxisControlButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DashboardView.Visibility =
                Visibility.Collapsed;
            AxisControlView.Visibility =
                Visibility.Visible;

            try
            {
                await AxisControlView
                    .StartMonitoringAsync();
            }
            catch
            {
                // AxisControl 页面已显示具体启动错误。
            }
        }

        private void AxisControlView_BackRequested(
            object sender,
            System.EventArgs eventArgs)
        {
            AxisControlView.Visibility =
                Visibility.Collapsed;
            DashboardView.Visibility =
                Visibility.Visible;
        }

        private async void CabinetButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            DashboardView.Visibility =
                Visibility.Collapsed;
            ChamberControlView.Visibility =
                Visibility.Visible;

            try
            {
                await ChamberControlView
                    .StartMonitoringAsync();
            }
            catch
            {
                // ChamberControl 页面会持续显示连接状态。
            }
        }

        private void ChamberControlView_BackRequested(
            object sender,
            System.EventArgs eventArgs)
        {
            ChamberControlView.Visibility =
                Visibility.Collapsed;
            DashboardView.Visibility =
                Visibility.Visible;
        }

        private void InfraredButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            Process[] runningProcesses =
                Process.GetProcessesByName(
                    "IRToolPro");
            bool runningProcessFound = false;

            try
            {
                foreach (Process process
                    in runningProcesses)
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    runningProcessFound = true;
                    process.Refresh();

                    if (process.MainWindowHandle !=
                        IntPtr.Zero)
                    {
                        ShowWindowAsync(
                            process.MainWindowHandle,
                            RestoreWindow);
                        SetForegroundWindow(
                            process.MainWindowHandle);
                        return;
                    }
                }

                if (runningProcessFound)
                {
                    MessageBox.Show(
                        "IRToolPro 已经运行，但暂时没有可激活的主窗口。" +
                        Environment.NewLine +
                        "请在任务栏中切换到 IRToolPro。",
                        "红外相机",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }
            finally
            {
                foreach (Process process
                    in runningProcesses)
                {
                    process.Dispose();
                }
            }

            string executablePath =
                FindInfraredToolExecutable();

            if (executablePath == null)
            {
                MessageBox.Show(
                    "找不到 IRToolPro.exe。" +
                    Environment.NewLine +
                    "请确认 IRToolPro_v2.4.0.0626 文件夹位于 src 根目录。",
                    "红外相机启动失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory =
                            Path.GetDirectoryName(
                                executablePath),
                        UseShellExecute = true
                    });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "IRToolPro 启动失败：" +
                    exception.Message,
                    "红外相机启动失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string
            FindInfraredToolExecutable()
        {
            const string infraredToolDirectory =
                "IRToolPro_v2.4.0.0626";
            const string executableName =
                "IRToolPro.exe";

            DirectoryInfo directory =
                new DirectoryInfo(
                    AppDomain.CurrentDomain
                        .BaseDirectory);

            while (directory != null)
            {
                string candidate =
                    Path.Combine(
                        directory.FullName,
                        infraredToolDirectory,
                        executableName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        protected override void OnClosed(
            EventArgs eventArgs)
        {
            infraredConnectionTimer.Stop();
            infraredConnectionTimer.Tick -=
                InfraredConnectionTimer_Tick;
            AxisControlView.Shutdown();
            ChamberControlView.Shutdown();
            base.OnClosed(eventArgs);
        }
    }
}
