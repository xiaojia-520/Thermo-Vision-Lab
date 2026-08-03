using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ThermoVision
{
    public partial class AxisControl : UserControl
    {
        private const float PhysicalLimitSafetyMargin =
            30.0f;
        private const float MaximumMoveSpeed = 50.0f;

        private readonly MotionHostClient motionHostClient;
        private readonly bool[] controllerConnected =
            new bool[4];
        private readonly bool[] controllerBusy =
            new bool[4];
        private readonly MotionControllerStatus[]
            latestStatuses =
                new MotionControllerStatus[4];
        private readonly DispatcherTimer
            statusFreshnessTimer;

        private Task monitoringTask;
        private int selectedControllerNumber = 1;
        private bool stopAllInProgress;
        private bool updatingAxisSelection;
        private bool updatingOperationInputs;
        private bool targetInputDirty;
        private bool limitsInputDirty;

        internal event EventHandler BackRequested;

        public AxisControl()
        {
            InitializeComponent();

            motionHostClient = new MotionHostClient();
            motionHostClient.StatusReceived +=
                MotionHostClient_StatusReceived;
            motionHostClient.ProgressReceived +=
                MotionHostClient_ProgressReceived;
            motionHostClient.ConnectionLost +=
                MotionHostClient_ConnectionLost;

            TargetPositionTextBox.TextChanged +=
                TargetPositionTextBox_TextChanged;
            LimitMinimumTextBox.TextChanged +=
                LimitTextBox_TextChanged;
            LimitMaximumTextBox.TextChanged +=
                LimitTextBox_TextChanged;

            statusFreshnessTimer =
                new DispatcherTimer();
            statusFreshnessTimer.Interval =
                TimeSpan.FromMilliseconds(500);
            statusFreshnessTimer.Tick +=
                StatusFreshnessTimer_Tick;
            statusFreshnessTimer.Start();

            SetAllControllersDisconnected(
                "等待连接");
            SelectController(1);
        }

        internal Task StartMonitoringAsync()
        {
            if (monitoringTask == null ||
                monitoringTask.IsFaulted ||
                monitoringTask.IsCanceled)
            {
                monitoringTask =
                    StartMonitoringCoreAsync();
            }

            return monitoringTask;
        }

        internal void Shutdown()
        {
            statusFreshnessTimer.Stop();
            motionHostClient.Dispose();
        }

        private async Task StartMonitoringCoreAsync()
        {
            MotionStatusText.Text =
                "正在启动运动控制服务……";

            try
            {
                await motionHostClient.StartAsync();

                MotionStatusText.Text =
                    "运动控制服务已启动，正在读取控制器状态……";
            }
            catch (Exception exception)
            {
                MotionStatusText.Text =
                    "运动控制服务启动失败：" +
                    exception.Message;

                SetAllControllersDisconnected(
                    "服务启动失败");
                throw;
            }
        }

        private void ControllerNavigationButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            Button button = sender as Button;

            if (button == null)
            {
                return;
            }

            int controllerNumber;

            if (int.TryParse(
                button.Tag.ToString(),
                out controllerNumber))
            {
                SelectController(controllerNumber);
            }
        }

        private void OperationAxisComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs eventArgs)
        {
            if (updatingAxisSelection)
            {
                return;
            }

            targetInputDirty = false;
            limitsInputDirty = false;
            LoadOperationSettings();
            RefreshButtonStates();
        }

        private void TargetPositionTextBox_TextChanged(
            object sender,
            TextChangedEventArgs eventArgs)
        {
            if (!updatingOperationInputs)
            {
                targetInputDirty = true;
            }
        }

        private void LimitTextBox_TextChanged(
            object sender,
            TextChangedEventArgs eventArgs)
        {
            if (!updatingOperationInputs)
            {
                limitsInputDirty = true;
            }
        }

        private async void RelativeMoveButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            Button button = sender as Button;
            float distance;
            float speed;

            if (button == null ||
                !TryReadFiniteFloat(
                    StepDistanceTextBox.Text,
                    out distance) ||
                distance <= 0)
            {
                ShowInputError(
                    "步进距离必须是大于 0 的有限数值。");
                return;
            }

            if (!TryReadMoveSpeed(out speed))
            {
                return;
            }

            int direction =
                button.Tag.ToString() == "-1"
                    ? -1
                    : 1;

            await ExecuteMoveAsync(
                GetSelectedOperationAxis(),
                distance * direction,
                speed,
                false);
        }

        private async void MoveToTargetButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            float targetPosition;
            float speed;

            if (!TryReadFiniteFloat(
                TargetPositionTextBox.Text,
                out targetPosition))
            {
                ShowInputError(
                    "目标位置必须是有限数值。");
                return;
            }

            if (!TryReadMoveSpeed(out speed))
            {
                return;
            }

            int axis =
                GetSelectedOperationAxis();

            MessageBoxResult confirmation =
                MessageBox.Show(
                    selectedControllerNumber +
                    " 号轴的 " +
                    GetAxisName(axis) +
                    " 轴将移动到软件坐标 " +
                    targetPosition.ToString("F3") +
                    "，速度 " +
                    speed.ToString("F3") +
                    "。" +
                    Environment.NewLine +
                    "确认现场运动路径安全后继续。",
                    "确认目标定位",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.OK)
            {
                return;
            }

            await ExecuteMoveAsync(
                axis,
                targetPosition,
                speed,
                true);
        }

        private async void SaveLimitsButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            float minimum;
            float maximum;

            if (!TryReadFiniteFloat(
                    LimitMinimumTextBox.Text,
                    out minimum) ||
                !TryReadFiniteFloat(
                    LimitMaximumTextBox.Text,
                    out maximum))
            {
                ShowInputError(
                    "软件限位必须是有限数值。");
                return;
            }

            if (minimum < PhysicalLimitSafetyMargin ||
                minimum >= maximum)
            {
                ShowInputError(
                    "软件限位必须满足：30 ≤ 最小值 < 最大值；最大值也必须保留负限位安全余量。");
                return;
            }

            int controllerNumber =
                selectedControllerNumber;
            int axis =
                GetSelectedOperationAxis();

            controllerBusy[
                controllerNumber] = true;
            RefreshButtonStates();

            try
            {
                MotionHostResult result =
                    await motionHostClient
                        .SetSoftwareLimitsAsync(
                            controllerNumber,
                            axis,
                            minimum,
                            maximum);

                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        result.Output);
                }

                MotionStatusText.Text =
                    result.Output;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "保存软件限位失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                controllerBusy[
                    controllerNumber] = false;
                RefreshButtonStates();
            }
        }

        private async Task ExecuteMoveAsync(
            int axis,
            float value,
            float speed,
            bool absolute)
        {
            int controllerNumber =
                selectedControllerNumber;

            controllerBusy[
                controllerNumber] = true;
            RefreshButtonStates();

            MotionStatusText.Text =
                controllerNumber +
                " 号轴 " +
                GetAxisName(axis) +
                (absolute
                    ? " 正在定位……"
                    : " 正在步进移动……");

            try
            {
                MotionHostResult result =
                    absolute
                        ? await motionHostClient
                            .MoveAbsoluteAsync(
                                controllerNumber,
                                axis,
                                value,
                                speed)
                        : await motionHostClient
                            .MoveRelativeAsync(
                                controllerNumber,
                                axis,
                                value,
                                speed);

                MotionStatusText.Text =
                    result.Output;

                if (!result.Success)
                {
                    MessageBox.Show(
                        result.Output,
                        "轴体移动失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                MotionStatusText.Text =
                    "轴体移动失败：" +
                    exception.Message;

                MessageBox.Show(
                    exception.Message,
                    "轴体移动失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                controllerBusy[
                    controllerNumber] = false;
                RefreshButtonStates();
            }
        }

        private async void ControllerZeroButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            int controllerNumber =
                selectedControllerNumber;

            string axisSummary =
                controllerNumber == 3
                    ? "X、Y、Z"
                    : "X、Y";

            string controllerIp =
                "192.168.1." +
                (30 + controllerNumber).ToString();

            MessageBoxResult confirmation =
                MessageBox.Show(
                    controllerNumber +
                    " 号轴（" + controllerIp + "）的 " +
                    axisSummary +
                    " 方向将依次真实运动并寻找正限位。" +
                    Environment.NewLine +
                    "请确认现场无人、急停可用且运动方向安全。",
                    "确认 " + controllerNumber +
                    " 号轴回零",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.OK)
            {
                MotionStatusText.Text = "已取消";
                return;
            }

            targetInputDirty = false;
            updatingOperationInputs = true;
            TargetPositionTextBox.Text =
                string.Empty;
            updatingOperationInputs = false;

            controllerBusy[
                controllerNumber] = true;
            RefreshButtonStates();

            MotionStatusText.Text =
                "正在回 " + controllerNumber +
                " 号轴（" + axisSummary +
                "），状态数据会持续刷新……";

            try
            {
                MotionHostResult result =
                    await motionHostClient.RunSoftwareZeroAsync(
                        controllerNumber);

                if (result.Success)
                {
                    MotionStatusText.Text =
                        controllerNumber +
                        " 号轴（" + axisSummary +
                        "）回零完成";

                    MessageBox.Show(
                        controllerNumber +
                        " 号轴的 " + axisSummary +
                        " 方向已全部回零完成。",
                        "回零完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MotionStatusText.Text =
                        "回零失败，错误码：" +
                        result.ExitCode +
                        "，" +
                        result.Output;

                    MessageBox.Show(
                        result.Output,
                        "回零失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                MotionStatusText.Text =
                    "运动控制错误：" +
                    exception.Message;

                MessageBox.Show(
                    exception.Message,
                    "运动控制错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                controllerBusy[
                    controllerNumber] = false;
                RefreshButtonStates();
            }
        }

        private async void ControllerRangeButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            int controllerNumber =
                selectedControllerNumber;

            string axisSummary =
                controllerNumber == 3
                    ? "X、Y、Z"
                    : "X、Y";

            string controllerIp =
                "192.168.1." +
                (30 + controllerNumber).ToString();

            MessageBoxResult confirmation =
                MessageBox.Show(
                    controllerNumber +
                    " 号控制器（" + controllerIp + "）的 " +
                    axisSummary +
                    " 轴将依次以速度 1 向负限位真实运动。" +
                    Environment.NewLine +
                    "每个轴触发负限位后会立即停止、记录最大行程，" +
                    "再向正方向退出 30 个控制器单位。" +
                    Environment.NewLine +
                    "该过程可能持续较长时间，请确认现场无人、" +
                    "负限位有效且急停可用。",
                    "确认标定负限位和最大行程",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.OK)
            {
                MotionStatusText.Text =
                    "已取消负限位标定";
                return;
            }

            controllerBusy[
                controllerNumber] = true;
            RefreshButtonStates();

            MotionStatusText.Text =
                "正在标定 " +
                controllerNumber +
                " 号控制器的负限位和最大行程……";

            try
            {
                MotionHostResult result =
                    await motionHostClient
                        .RunRangeCalibrationAsync(
                            controllerNumber);

                MotionStatusText.Text =
                    result.Output;

                if (result.Success)
                {
                    MessageBox.Show(
                        result.Output,
                        "负限位和最大行程标定完成",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        result.Output,
                        "负限位标定失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                MotionStatusText.Text =
                    "负限位标定错误：" +
                    exception.Message;

                MessageBox.Show(
                    exception.Message,
                    "负限位标定错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                controllerBusy[
                    controllerNumber] = false;
                RefreshButtonStates();
            }
        }

        private async void StopAllButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            if (stopAllInProgress)
            {
                return;
            }

            stopAllInProgress = true;
            RefreshButtonStates();
            MotionStatusText.Text =
                "正在向三个控制器发送停止命令并确认停止状态……";

            try
            {
                MotionHostResult result =
                    await motionHostClient.StopAllAsync();

                MotionStatusText.Text =
                    result.Output;

                if (!result.Success)
                {
                    MessageBox.Show(
                        result.Output +
                        Environment.NewLine +
                        "请立即检查现场并使用硬件急停。",
                        "停止状态未确认",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception exception)
            {
                MotionStatusText.Text =
                    "发送停止命令失败：" +
                    exception.Message;
            }
            finally
            {
                stopAllInProgress = false;
                RefreshButtonStates();
            }
        }

        private void MotionHostClient_StatusReceived(
            object sender,
            MotionControllerStatusEventArgs eventArgs)
        {
            RunOnUiThread(
                delegate
                {
                    ApplyControllerStatus(
                        eventArgs.Status);
                });
        }

        private void MotionHostClient_ProgressReceived(
            object sender,
            MotionProgressEventArgs eventArgs)
        {
            RunOnUiThread(
                delegate
                {
                    MotionStatusText.Text =
                        eventArgs.ControllerNumber +
                        " 号轴：" +
                        eventArgs.Message;
                });
        }

        private void MotionHostClient_ConnectionLost(
            object sender,
            EventArgs eventArgs)
        {
            RunOnUiThread(
                delegate
                {
                    monitoringTask = null;
                    SetAllControllersDisconnected(
                        "服务已断开");
                    MotionStatusText.Text =
                        "运动控制服务连接已断开；重新进入页面时会尝试恢复。";
                });
        }

        private void ApplyControllerStatus(
            MotionControllerStatus status)
        {
            int controllerNumber =
                status.ControllerNumber;

            controllerConnected[
                controllerNumber] =
                    status.Connected;
            latestStatuses[
                controllerNumber] =
                    status;

            UpdateNavigationConnection(
                controllerNumber,
                status);

            if (selectedControllerNumber ==
                controllerNumber)
            {
                RenderSelectedController();

                if (!LimitMinimumTextBox
                        .IsKeyboardFocusWithin &&
                    !LimitMaximumTextBox
                        .IsKeyboardFocusWithin &&
                    !TargetPositionTextBox
                        .IsKeyboardFocusWithin)
                {
                    LoadOperationSettings();
                }
            }

            RefreshButtonStates();

            if (!IsAnyControllerBusy() &&
                status.Connected)
            {
                MotionStatusText.Text =
                    "实时状态已更新：" +
                    status.ReceivedAt.ToString(
                        "HH:mm:ss");
            }
        }

        private void SelectController(
            int controllerNumber)
        {
            if (controllerNumber < 1 ||
                controllerNumber > 3)
            {
                return;
            }

            selectedControllerNumber =
                controllerNumber;

            SetNavigationSelection(
                Controller1NavigationButton,
                controllerNumber == 1);
            SetNavigationSelection(
                Controller2NavigationButton,
                controllerNumber == 2);
            SetNavigationSelection(
                Controller3NavigationButton,
                controllerNumber == 3);

            PopulateOperationAxes();
            RenderSelectedController();
            RefreshButtonStates();
        }

        private void PopulateOperationAxes()
        {
            updatingAxisSelection = true;

            OperationAxisComboBox.Items.Clear();
            OperationAxisComboBox.Items.Add("X 轴");
            OperationAxisComboBox.Items.Add("Y 轴");

            if (selectedControllerNumber == 3)
            {
                OperationAxisComboBox.Items.Add(
                    "Z 轴");
            }

            OperationAxisComboBox.SelectedIndex = 0;
            updatingAxisSelection = false;

            targetInputDirty = false;
            limitsInputDirty = false;
            LoadOperationSettings();
        }

        private void LoadOperationSettings()
        {
            int axis =
                GetSelectedOperationAxis();

            MotionControllerStatus controller =
                latestStatuses[
                    selectedControllerNumber];

            MotionAxisStatus status =
                controller == null
                    ? null
                    : FindAxis(
                        controller,
                        axis);

            updatingOperationInputs = true;

            if (!limitsInputDirty)
            {
                if (status != null &&
                    status.HasSoftwareLimits)
                {
                    LimitMinimumTextBox.Text =
                        status.Minimum.ToString("F3");
                    LimitMaximumTextBox.Text =
                        status.Maximum.ToString("F3");
                }
                else
                {
                    LimitMinimumTextBox.Text = "30.000";
                    LimitMaximumTextBox.Text = "150.000";
                }
            }

            if (!targetInputDirty)
            {
                TargetPositionTextBox.Text =
                    status != null &&
                    status.HasSoftwareZero
                        ? status.SoftwarePosition
                            .ToString("F3")
                        : string.Empty;
            }

            updatingOperationInputs = false;

            RefreshButtonStates();
        }

        private void RenderSelectedController()
        {
            int controllerNumber =
                selectedControllerNumber;

            SelectedControllerTitle.Text =
                controllerNumber +
                " 号轴";
            SelectedControllerIpText.Text =
                "192.168.1." +
                (30 + controllerNumber);
            SelectedControllerAxesText.Text =
                controllerNumber == 3
                    ? "X / Y / Z"
                    : "X / Y";

            SelectedZStatus.Visibility =
                controllerNumber == 3
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            MotionControllerStatus status =
                latestStatuses[
                    controllerNumber];

            if (status == null)
            {
                SetDisconnectedDisplay(
                    SelectedControllerConnectionDot,
                    SelectedControllerConnectionText,
                    "等待连接");
                SetSelectedAxesDisconnected();
                return;
            }

            if (!IsStatusFresh(
                controllerNumber))
            {
                SetDisconnectedDisplay(
                    SelectedControllerConnectionDot,
                    SelectedControllerConnectionText,
                    "状态超时");
                SetSelectedAxesDisconnected();
                return;
            }

            SetConnectionDisplay(
                SelectedControllerConnectionDot,
                SelectedControllerConnectionText,
                status);

            UpdateSelectedAxis(
                SelectedXStatus,
                FindAxis(status, 0),
                status.Connected);
            UpdateSelectedAxis(
                SelectedYStatus,
                FindAxis(status, 1),
                status.Connected);

            if (controllerNumber == 3)
            {
                UpdateSelectedAxis(
                    SelectedZStatus,
                    FindAxis(status, 2),
                    status.Connected);
            }
        }

        private void UpdateNavigationConnection(
            int controllerNumber,
            MotionControllerStatus status)
        {
            if (controllerNumber == 1)
            {
                SetConnectionDisplay(
                    Controller1ConnectionDot,
                    Controller1ConnectionText,
                    status);
            }
            else if (controllerNumber == 2)
            {
                SetConnectionDisplay(
                    Controller2ConnectionDot,
                    Controller2ConnectionText,
                    status);
            }
            else if (controllerNumber == 3)
            {
                SetConnectionDisplay(
                    Controller3ConnectionDot,
                    Controller3ConnectionText,
                    status);
            }
        }

        private static void SetNavigationSelection(
            Button button,
            bool selected)
        {
            button.Background =
                CreateBrush(
                    selected
                        ? "#EAF1FF"
                        : "#FFFFFF");
            button.BorderBrush =
                CreateBrush(
                    selected
                        ? "#9DBAF4"
                        : "#E1E7EF");
            button.BorderThickness =
                new Thickness(
                    selected ? 2 : 1);
        }

        private static void SetConnectionDisplay(
            System.Windows.Shapes.Ellipse dot,
            TextBlock text,
            MotionControllerStatus status)
        {
            if (status.Connected)
            {
                dot.Fill = CreateBrush("#35A45B");
                text.Foreground =
                    CreateBrush("#26834A");
                text.Text = "已连接";
            }
            else
            {
                dot.Fill = CreateBrush("#D44A4A");
                text.Foreground =
                    CreateBrush("#B53A3A");
                text.Text =
                    string.IsNullOrWhiteSpace(
                        status.ErrorMessage)
                        ? "未连接"
                        : status.ErrorMessage;
            }
        }

        private static MotionAxisStatus FindAxis(
            MotionControllerStatus controller,
            int axisNumber)
        {
            foreach (MotionAxisStatus axis
                in controller.Axes)
            {
                if (axis.Axis == axisNumber)
                {
                    return axis;
                }
            }

            return null;
        }

        private static void UpdateSelectedAxis(
            AxisStatusControl control,
            MotionAxisStatus status,
            bool connected)
        {
            if (!connected ||
                status == null)
            {
                control.SetDisconnected();
                return;
            }

            control.UpdateStatus(status);
        }

        private void SetSelectedAxesDisconnected()
        {
            SelectedXStatus.SetDisconnected();
            SelectedYStatus.SetDisconnected();
            SelectedZStatus.SetDisconnected();
        }

        private void SetAllControllersDisconnected(
            string message)
        {
            for (int index = 1;
                index <= 3;
                index++)
            {
                controllerConnected[index] = false;
                latestStatuses[index] = null;
            }

            SetDisconnectedDisplay(
                Controller1ConnectionDot,
                Controller1ConnectionText,
                message);
            SetDisconnectedDisplay(
                Controller2ConnectionDot,
                Controller2ConnectionText,
                message);
            SetDisconnectedDisplay(
                Controller3ConnectionDot,
                Controller3ConnectionText,
                message);

            SetSelectedAxesDisconnected();
            RenderSelectedController();
            RefreshButtonStates();
        }

        private static void SetDisconnectedDisplay(
            System.Windows.Shapes.Ellipse dot,
            TextBlock text,
            string message)
        {
            dot.Fill = CreateBrush("#98A5B7");
            text.Foreground =
                CreateBrush("#758399");
            text.Text = message;
        }

        private void RefreshButtonStates()
        {
            int controllerNumber =
                selectedControllerNumber;
            bool busy =
                controllerBusy[
                    controllerNumber] ||
                stopAllInProgress;
            bool fresh =
                IsStatusFresh(
                    controllerNumber);
            bool connected =
                controllerConnected[
                    controllerNumber] &&
                fresh;

            MotionControllerStatus controller =
                latestStatuses[
                    controllerNumber];
            bool controllerSafelyStopped =
                connected &&
                AreAllControllerAxesSafelyStopped(
                    controller);

            SelectedControllerHomeButton.IsEnabled =
                !busy &&
                controllerSafelyStopped;
            SelectedControllerRangeButton.IsEnabled =
                !busy &&
                controllerSafelyStopped &&
                AreAllControllerAxesReadyForRangeCalibration(
                    controller);

            int axis =
                GetSelectedOperationAxis();
            MotionAxisStatus axisStatus =
                controller == null
                    ? null
                    : FindAxis(
                        controller,
                        axis);

            bool readyToMove =
                !busy &&
                connected &&
                axisStatus != null &&
                IsAxisSafelyStopped(axisStatus) &&
                axisStatus.HasSoftwareZero &&
                axisStatus.HasSoftwareLimits &&
                IsSoftwarePositionWithinLimits(
                    axisStatus);

            NegativeStepButton.IsEnabled =
                readyToMove &&
                axisStatus.SoftwarePosition >
                    axisStatus.Minimum + 0.0001f;
            PositiveStepButton.IsEnabled =
                readyToMove &&
                axisStatus.SoftwarePosition <
                    axisStatus.Maximum - 0.0001f;
            MoveToTargetButton.IsEnabled =
                readyToMove;

            bool canEditLimits =
                !busy &&
                connected &&
                axisStatus != null &&
                axisStatus.HasSoftwareLimits;

            OperationAxisComboBox.IsEnabled =
                !busy;
            StepDistanceTextBox.IsEnabled =
                !busy;
            MoveSpeedTextBox.IsEnabled =
                !busy;
            TargetPositionTextBox.IsEnabled =
                !busy;
            LimitMinimumTextBox.IsEnabled =
                canEditLimits;
            LimitMaximumTextBox.IsEnabled =
                canEditLimits;
            SaveLimitsButton.IsEnabled =
                canEditLimits &&
                monitoringTask != null &&
                monitoringTask.Status ==
                    TaskStatus.RanToCompletion;
            StopAllButton.IsEnabled =
                !stopAllInProgress;

            if (busy)
            {
                OperationReadinessText.Text =
                    "当前控制器正在执行命令";
                OperationReadinessText.Foreground =
                    CreateBrush("#276EF1");
            }
            else if (!connected)
            {
                OperationReadinessText.Text =
                    fresh
                        ? "控制器未连接，移动已禁用"
                        : "状态数据超时，移动已禁用";
                OperationReadinessText.Foreground =
                    CreateBrush("#B53A3A");
            }
            else if (axisStatus == null)
            {
                OperationReadinessText.Text =
                    "没有读取到当前轴状态";
                OperationReadinessText.Foreground =
                    CreateBrush("#B53A3A");
            }
            else if (!IsAxisSafelyStopped(
                axisStatus))
            {
                OperationReadinessText.Text =
                    "当前轴未确认停止或状态异常，移动已禁用";
                OperationReadinessText.Foreground =
                    CreateBrush("#B53A3A");
            }
            else if (!axisStatus.HasSoftwareZero)
            {
                OperationReadinessText.Text =
                    "当前轴未回零，步进和定位已禁用";
                OperationReadinessText.Foreground =
                    CreateBrush("#A96800");
            }
            else if (!axisStatus.HasSoftwareLimits)
            {
                OperationReadinessText.Text =
                    "请先执行负限位/行程标定，生成带安全余量的软件限位范围";
                OperationReadinessText.Foreground =
                    CreateBrush("#A96800");
            }
            else if (!IsSoftwarePositionWithinLimits(
                axisStatus))
            {
                OperationReadinessText.Text =
                    "当前软件坐标已越过软件限位，移动已禁用";
                OperationReadinessText.Foreground =
                    CreateBrush("#B53A3A");
            }
            else
            {
                OperationReadinessText.Text =
                    "已就绪，允许范围 [" +
                    axisStatus.Minimum.ToString("F3") +
                    ", " +
                    axisStatus.Maximum.ToString("F3") +
                    "]；控制器掉电或坐标基准变化后必须重新回零";
                OperationReadinessText.Foreground =
                    CreateBrush("#26834A");
            }
        }

        private static bool
            AreAllControllerAxesSafelyStopped(
                MotionControllerStatus controller)
        {
            if (controller == null ||
                !controller.Connected ||
                controller.Axes == null ||
                controller.Axes.Length == 0)
            {
                return false;
            }

            foreach (MotionAxisStatus axis
                in controller.Axes)
            {
                if (!IsAxisSafelyStopped(axis))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAxisSafelyStopped(
            MotionAxisStatus axis)
        {
            return axis != null &&
                axis.IsStopped &&
                !axis.IsRunning &&
                !axis.IsPaused &&
                !axis.IsHoming &&
                !axis.IsHomeOvertime;
        }

        private static bool
            AreAllControllerAxesReadyForRangeCalibration(
                MotionControllerStatus controller)
        {
            const float expectedPosition = 30.0f;
            const float positionTolerance = 1.0f;

            if (controller == null ||
                controller.Axes == null ||
                controller.Axes.Length == 0)
            {
                return false;
            }

            foreach (MotionAxisStatus axis
                in controller.Axes)
            {
                if (!axis.HasSoftwareZero ||
                    axis.IsNegativeLimitActive ||
                    axis.IsPositiveLimitActive ||
                    Math.Abs(
                        axis.SoftwarePosition -
                        expectedPosition) >
                        positionTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool
            IsSoftwarePositionWithinLimits(
                MotionAxisStatus axis)
        {
            const float tolerance = 0.01f;

            return axis != null &&
                axis.HasSoftwareZero &&
                axis.HasSoftwareLimits &&
                axis.SoftwarePosition >=
                    axis.Minimum - tolerance &&
                axis.SoftwarePosition <=
                    axis.Maximum + tolerance;
        }

        private void StatusFreshnessTimer_Tick(
            object sender,
            EventArgs eventArgs)
        {
            for (int controllerNumber = 1;
                controllerNumber <= 3;
                controllerNumber++)
            {
                MotionControllerStatus status =
                    latestStatuses[
                        controllerNumber];

                if (status != null &&
                    status.Connected &&
                    !IsStatusFresh(
                        controllerNumber))
                {
                    if (controllerNumber == 1)
                    {
                        SetDisconnectedDisplay(
                            Controller1ConnectionDot,
                            Controller1ConnectionText,
                            "状态超时");
                    }
                    else if (controllerNumber == 2)
                    {
                        SetDisconnectedDisplay(
                            Controller2ConnectionDot,
                            Controller2ConnectionText,
                            "状态超时");
                    }
                    else
                    {
                        SetDisconnectedDisplay(
                            Controller3ConnectionDot,
                            Controller3ConnectionText,
                            "状态超时");
                    }
                }
            }

            RenderSelectedController();
            RefreshButtonStates();
        }

        private bool IsStatusFresh(
            int controllerNumber)
        {
            MotionControllerStatus status =
                latestStatuses[
                    controllerNumber];

            if (status == null)
            {
                return false;
            }

            long elapsedTicks =
                Stopwatch.GetTimestamp() -
                status.ReceivedAtTimestamp;

            return elapsedTicks >= 0 &&
                elapsedTicks <=
                    Stopwatch.Frequency;
        }

        private bool IsAnyControllerBusy()
        {
            return controllerBusy[1] ||
                controllerBusy[2] ||
                controllerBusy[3];
        }

        private int GetSelectedOperationAxis()
        {
            int selectedIndex =
                OperationAxisComboBox
                    .SelectedIndex;

            return selectedIndex < 0
                ? 0
                : selectedIndex;
        }

        private bool TryReadMoveSpeed(
            out float speed)
        {
            if (!TryReadFiniteFloat(
                    MoveSpeedTextBox.Text,
                    out speed) ||
                speed <= 0 ||
                speed > MaximumMoveSpeed)
            {
                ShowInputError(
                    "移动速度必须是大于 0 且不超过 50 的有限数值。");
                return false;
            }

            return true;
        }

        private static bool TryReadFiniteFloat(
            string text,
            out float value)
        {
            bool parsed =
                float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out value) ||
                float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);

            return parsed &&
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private static void ShowInputError(
            string message)
        {
            MessageBox.Show(
                message,
                "输入无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static string GetAxisName(
            int axis)
        {
            switch (axis)
            {
                case 0:
                    return "X";
                case 1:
                    return "Y";
                case 2:
                    return "Z";
                default:
                    return axis.ToString();
            }
        }

        private void BackButton_Click(
            object sender,
            RoutedEventArgs eventArgs)
        {
            if (IsAnyControllerBusy())
            {
                MessageBox.Show(
                    "回零过程中不能离开轴体控制页面。" +
                    Environment.NewLine +
                    "如遇异常，请使用“全部停止”或现场急停。",
                    "轴体正在运动",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            EventHandler handler = BackRequested;

            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RunOnUiThread(
            Action action)
        {
            if (Dispatcher.HasShutdownStarted ||
                Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.BeginInvoke(action);
        }

        private static SolidColorBrush CreateBrush(
            string color)
        {
            return new SolidColorBrush(
                (Color)ColorConverter
                    .ConvertFromString(color));
        }
    }
}
