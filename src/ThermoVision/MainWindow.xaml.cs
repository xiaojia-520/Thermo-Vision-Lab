using System;
using System.Windows;

namespace ThermoVision
{
    public partial class MainWindow : Window
    {
        private readonly MotionHostClient motionHostClient;

        public MainWindow()
        {
            InitializeComponent();
            motionHostClient = new MotionHostClient();
        }

        private async void SoftwareZeroButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            MessageBoxResult confirmation =
                MessageBox.Show(
                    "X 轴将真实运动并寻找正限位。" +
                    Environment.NewLine +
                    "请确认现场无人、急停可用且运动方向安全。",
                    "确认 X 轴回零",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.OK)
            {
                MotionStatusText.Text = "已取消";
                return;
            }

            SoftwareZeroButton.IsEnabled = false;
            MotionStatusText.Text =
                "正在建立软件零点，请勿关闭程序……";

            try
            {
                MotionHostResult result =
                    await motionHostClient.RunSoftwareZeroAsync();

                if (result.Success)
                {
                    MotionStatusText.Text =
                        "X 轴软件零点建立完成";
                    MessageBox.Show(
                        "X 轴软件零点建立完成。",
                        "回零完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MotionStatusText.Text =
                        "回零失败，错误码：" +
                        result.ExitCode;

                    MessageBox.Show(
                        result.Output,
                        "回零失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                MotionStatusText.Text = "无法启动运动控制";

                MessageBox.Show(
                    exception.Message,
                    "运动控制错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SoftwareZeroButton.IsEnabled = true;
            }
        }
    }
}
