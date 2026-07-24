using System.Windows.Controls;
using System.Windows.Media;

namespace ThermoVision
{
    public partial class AxisStatusControl :
        UserControl
    {
        private string axisName;

        public AxisStatusControl()
        {
            InitializeComponent();
        }

        public string AxisName
        {
            get { return axisName; }
            set
            {
                axisName = value;
                AxisNameText.Text = value;
            }
        }

        internal void UpdateStatus(
            MotionAxisStatus status)
        {
            PositionText.Text =
                status.HasSoftwareZero
                    ? "S " +
                        status.SoftwarePosition
                            .ToString("F3")
                    : "R " +
                        status.RawPosition
                            .ToString("F3");
            PositionText.ToolTip =
                "原始位置：" +
                status.RawPosition.ToString("F3");
            SpeedText.Text =
                "V " +
                status.Speed.ToString("F3");

            if (status.IsHomeOvertime)
            {
                SetState(
                    "回零超时",
                    "#FDE8E8",
                    "#C93636");
            }
            else if (status.IsPositiveLimitActive)
            {
                SetState(
                    "正限位",
                    "#FDE8E8",
                    "#C93636");
            }
            else if (status.IsNegativeLimitActive)
            {
                SetState(
                    "负限位",
                    "#FDE8E8",
                    "#C93636");
            }
            else if (status.HasSoftwareZero &&
                status.HasSoftwareLimits &&
                (status.SoftwarePosition <
                        status.Minimum - 0.01f ||
                    status.SoftwarePosition >
                        status.Maximum + 0.01f))
            {
                SetState(
                    "软件越界",
                    "#FDE8E8",
                    "#C93636");
            }
            else if (status.IsHoming)
            {
                SetState(
                    "回零中",
                    "#FFF2D8",
                    "#A96800");
            }
            else if (status.IsRunning)
            {
                SetState(
                    "运行中",
                    "#E8F0FF",
                    "#276EF1");
            }
            else if (status.IsPaused)
            {
                SetState(
                    "已暂停",
                    "#FFF2D8",
                    "#A96800");
            }
            else if (status.IsStopped)
            {
                if (!status.HasSoftwareZero)
                {
                    SetState(
                        "未回零",
                        "#FFF2D8",
                        "#A96800");
                }
                else if (!status.HasSoftwareLimits)
                {
                    SetState(
                        "未设限",
                        "#FFF2D8",
                        "#A96800");
                }
                else
                {
                    SetState(
                        "已就绪",
                        "#EAF7EF",
                        "#26834A");
                }
            }
            else
            {
                SetState(
                    "状态 0x" +
                    status.Status.ToString("X4"),
                    "#F0F3F7",
                    "#758399");
            }
        }

        internal void SetDisconnected()
        {
            PositionText.Text = "P --";
            PositionText.ToolTip = null;
            SpeedText.Text = "V --";

            SetState(
                "离线",
                "#F0F3F7",
                "#758399");
        }

        private void SetState(
            string text,
            string background,
            string foreground)
        {
            StateText.Text = text;
            StateBorder.Background =
                new SolidColorBrush(
                    (Color)ColorConverter
                        .ConvertFromString(background));
            StateText.Foreground =
                new SolidColorBrush(
                    (Color)ColorConverter
                        .ConvertFromString(foreground));
        }
    }
}
