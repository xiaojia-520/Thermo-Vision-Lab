using System;
using System.Threading;

namespace MotionHost
{
    internal sealed class FmcSoftwareZero
    {
        private const uint NegativeLimit = 0x0010;
        private const uint PositiveLimit = 0x0020;

        private const int RelativeMove = 1;
        private const int DeceleratedStop = 1;
        private const int PollIntervalMilliseconds = 50;
        private const float SlowdownDistance = 10.0f;
        private const float ProfileMinimumSpeed = 1.0f;

        private bool isEstablished;
        private float rawZeroPosition;
        private volatile bool cancellationRequested;

        internal bool IsEstablished
        {
            get { return isEstablished; }
        }

        internal float RawZeroPosition
        {
            get
            {
                if (!isEstablished)
                {
                    throw new InvalidOperationException(
                        "软件零点尚未建立。");
                }

                return rawZeroPosition;
            }
        }

        internal float EstablishAtPositiveLimit(
            int deviceId,
            int axis,
            float speed,
            float acceleration,
            float deceleration,
            float? knownRawZeroPosition,
            float releaseDistance,
            float maximumSeekDistance,
            TimeSpan moveTimeout)
        {
            ValidateParameters(
                axis,
                speed,
                acceleration,
                deceleration,
                releaseDistance,
                maximumSeekDistance,
                moveTimeout);

            if (knownRawZeroPosition.HasValue &&
                !IsFinite(knownRawZeroPosition.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(knownRawZeroPosition));
            }

            EnsureAxisStopped(deviceId, axis);

            AxisSnapshot initial = ReadSnapshot(deviceId, axis);

            try
            {
                if (initial.PositiveLimitActive)
                {
                    Console.WriteLine(
                        GetAxisName(axis) +
                        " 轴当前压在正限位，先向负方向退出 " +
                        releaseDistance.ToString("F3") + " 个控制器单位。");

                    ReleaseFromPositiveLimit(
                        deviceId,
                        axis,
                        releaseDistance,
                        speed,
                        acceleration,
                        deceleration,
                        knownRawZeroPosition.HasValue,
                        moveTimeout);

                    AxisSnapshot released =
                        ReadSnapshot(deviceId, axis);

                    if (released.PositiveLimitActive)
                    {
                        throw new InvalidOperationException(
                            "负向退出后正限位仍然触发，停止建立软件零点。");
                    }
                }

                Console.WriteLine(
                    "开始低速向正方向寻找正限位，最大距离 " +
                    maximumSeekDistance.ToString("F3") +
                    " 个控制器单位。");

                SeekPositiveLimit(
                    deviceId,
                    axis,
                    maximumSeekDistance,
                    speed,
                    acceleration,
                    deceleration,
                    knownRawZeroPosition,
                    moveTimeout);

                ThrowIfCancellationRequested();

                AxisSnapshot zeroSnapshot =
                    ReadSnapshot(deviceId, axis);

                if (!zeroSnapshot.PositiveLimitActive)
                {
                    throw new InvalidOperationException(
                        "轴已停止，但没有检测到正限位，软件零点建立失败。");
                }

                rawZeroPosition = zeroSnapshot.RawPosition;
                isEstablished = true;

                Console.WriteLine(
                    "正限位已触发并停止，记录软件零点 P0 = " +
                    rawZeroPosition.ToString("F3"));

                Console.WriteLine(
                    "记录零点后向负方向退出 " +
                    releaseDistance.ToString("F3") +
                    " 个控制器单位。");

                ThrowIfCancellationRequested();

                ReleaseFromPositiveLimit(
                    deviceId,
                    axis,
                    releaseDistance,
                    speed,
                    acceleration,
                    deceleration,
                    knownRawZeroPosition.HasValue,
                    moveTimeout);

                AxisSnapshot finalSnapshot =
                    ReadSnapshot(deviceId, axis);

                if (finalSnapshot.PositiveLimitActive)
                {
                    throw new InvalidOperationException(
                        "退出后正限位仍然触发。");
                }

                float softwarePosition =
                    ToSoftwarePosition(
                        finalSnapshot.RawPosition);

                Console.WriteLine(
                    "软件零点建立完成。当前软件 " +
                    GetAxisName(axis) + " = " +
                    softwarePosition.ToString("F3"));

                return rawZeroPosition;
            }
            catch
            {
                isEstablished = false;
                StopWithDeceleration(deviceId, axis);
                throw;
            }
        }

        internal float ToSoftwarePosition(
            float rawPosition)
        {
            if (!isEstablished)
            {
                throw new InvalidOperationException(
                    "软件零点尚未建立。");
            }

            return rawZeroPosition - rawPosition;
        }

        internal void RequestStop(
            int deviceId,
            int axis)
        {
            RequestCancellation();
            StopWithDeceleration(deviceId, axis);
        }

        internal void RequestCancellation()
        {
            cancellationRequested = true;
        }

        private void SeekPositiveLimit(
            int deviceId,
            int axis,
            float distance,
            float speed,
            float acceleration,
            float deceleration,
            float? knownRawZeroPosition,
            TimeSpan timeout)
        {
            AxisSnapshot before = ReadSnapshot(deviceId, axis);

            if (before.PositiveLimitActive)
            {
                throw new InvalidOperationException(
                    "寻找正限位前，正限位必须处于释放状态。");
            }

            if (knownRawZeroPosition.HasValue)
            {
                SeekPositiveLimitWithProfile(
                    deviceId,
                    axis,
                    distance,
                    speed,
                    acceleration,
                    deceleration,
                    knownRawZeroPosition.Value,
                    timeout);
                return;
            }

            int moveResult =
                FmcNative.FMC4030_Jog_Single_Axis(
                    deviceId,
                    axis,
                    distance,
                    speed,
                    acceleration,
                    deceleration,
                    RelativeMove);

            if (moveResult != 0)
            {
                throw new InvalidOperationException(
                    "启动正向运动失败，返回值：" + moveResult);
            }

            DateTime deadline = DateTime.UtcNow.Add(timeout);
            int pollCount = 0;

            while (true)
            {
                Thread.Sleep(PollIntervalMilliseconds);
                ThrowIfCancellationRequested();
                pollCount++;

                AxisSnapshot snapshot =
                    ReadSnapshot(deviceId, axis);

                if (pollCount % 5 == 0 ||
                    snapshot.PositiveLimitActive)
                {
                    PrintSnapshot(snapshot);
                }

                if (snapshot.PositiveLimitActive)
                {
                    int stopResult =
                        FmcNative.FMC4030_Stop_Single_Axis(
                            deviceId,
                            axis,
                            DeceleratedStop);

                    if (stopResult != 0)
                    {
                        throw new InvalidOperationException(
                            "正限位触发，但减速停止命令失败，返回值：" +
                            stopResult);
                    }

                    WaitUntilStopped(
                        deviceId,
                        axis,
                        TimeSpan.FromSeconds(3));
                    return;
                }

                int stopState =
                    FmcNative.FMC4030_Check_Axis_Is_Stop(
                        deviceId,
                        axis);

                if (stopState == 1)
                {
                    throw new InvalidOperationException(
                        "到达最大寻找距离前轴已停止，但正限位没有触发。");
                }

                if (stopState < 0)
                {
                    throw new InvalidOperationException(
                        "检查轴停止状态失败，返回值：" + stopState);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        "寻找正限位超时。");
                }
            }
        }

        private void SeekPositiveLimitWithProfile(
            int deviceId,
            int axis,
            float maximumSeekDistance,
            float maximumSpeed,
            float acceleration,
            float deceleration,
            float knownRawZeroPosition,
            TimeSpan timeout)
        {
            AxisSnapshot start = ReadSnapshot(deviceId, axis);
            float distanceToReference =
                knownRawZeroPosition - start.RawPosition;
            float minimumSpeed = Math.Min(
                ProfileMinimumSpeed,
                maximumSpeed);

            if (distanceToReference <= 0 ||
                distanceToReference >
                    maximumSeekDistance)
            {
                Console.WriteLine(
                    "已保存 P0 与当前位置不匹配，改用速度 " +
                    minimumSpeed.ToString("F3") +
                    " 安全寻找正限位。");

                StartPositiveSeekAndWait(
                    deviceId,
                    axis,
                    maximumSeekDistance,
                    minimumSpeed,
                    acceleration,
                    deceleration,
                    timeout);
                return;
            }

            if (distanceToReference >
                SlowdownDistance)
            {
                float fastDistance =
                    distanceToReference -
                    SlowdownDistance;

                Console.WriteLine(
                    "距正限位 " +
                    distanceToReference.ToString("F3") +
                    "，先以速度 " +
                    maximumSpeed.ToString("F3") +
                    " 移动到最后 10 个单位边界。");

                MoveRelativeAndWait(
                    deviceId,
                    axis,
                    fastDistance,
                    maximumSpeed,
                    acceleration,
                    deceleration,
                    timeout,
                    false);
            }

            AxisSnapshot slowStart =
                ReadSnapshot(deviceId, axis);
            float remainingToReference =
                knownRawZeroPosition -
                slowStart.RawPosition;
            float movedDistance =
                slowStart.RawPosition -
                start.RawPosition;
            float remainingSearchDistance =
                maximumSeekDistance - movedDistance;
            float slowSeekDistance = Math.Min(
                remainingSearchDistance,
                Math.Max(
                    SlowdownDistance,
                    remainingToReference) +
                2.0f);

            Console.WriteLine(
                "进入正限位安全区，最后约 10 个单位" +
                "固定使用速度 " +
                minimumSpeed.ToString("F3") +
                " 寻找正限位。");

            StartPositiveSeekAndWait(
                deviceId,
                axis,
                slowSeekDistance,
                minimumSpeed,
                acceleration,
                deceleration,
                timeout);
        }

        private void ReleaseFromPositiveLimit(
            int deviceId,
            int axis,
            float releaseDistance,
            float maximumSpeed,
            float acceleration,
            float deceleration,
            bool useProfile,
            TimeSpan timeout)
        {
            if (useProfile &&
                maximumSpeed > ProfileMinimumSpeed &&
                releaseDistance > SlowdownDistance)
            {
                float fastDistance =
                    releaseDistance -
                    SlowdownDistance;

                Console.WriteLine(
                    "正限位退出前 " +
                    fastDistance.ToString("F3") +
                    " 个单位使用速度 " +
                    maximumSpeed.ToString("F3") +
                    "，最后 10 个单位使用速度 " +
                    ProfileMinimumSpeed.ToString("F3") +
                    "。");

                MoveRelativeAndWait(
                    deviceId,
                    axis,
                    -fastDistance,
                    maximumSpeed,
                    acceleration,
                    deceleration,
                    timeout,
                    true);

                MoveRelativeAndWait(
                    deviceId,
                    axis,
                    -SlowdownDistance,
                    ProfileMinimumSpeed,
                    acceleration,
                    deceleration,
                    timeout,
                    true);
                return;
            }

            MoveRelativeAndWait(
                deviceId,
                axis,
                -releaseDistance,
                maximumSpeed,
                acceleration,
                deceleration,
                timeout,
                true);
        }

        private void StartPositiveSeekAndWait(
            int deviceId,
            int axis,
            float distance,
            float speed,
            float acceleration,
            float deceleration,
            TimeSpan timeout)
        {
            int moveResult =
                FmcNative.FMC4030_Jog_Single_Axis(
                    deviceId,
                    axis,
                    distance,
                    speed,
                    acceleration,
                    deceleration,
                    RelativeMove);

            if (moveResult != 0)
            {
                throw new InvalidOperationException(
                    "启动连续正限位搜索失败，返回值：" +
                    moveResult);
            }

            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (true)
            {
                Thread.Sleep(PollIntervalMilliseconds);
                ThrowIfCancellationRequested();

                AxisSnapshot snapshot = ReadSnapshot(deviceId, axis);
                if (snapshot.PositiveLimitActive)
                {
                    StopAtPositiveLimit(deviceId, axis);
                    return;
                }

                int stopState =
                    FmcNative.FMC4030_Check_Axis_Is_Stop(
                        deviceId,
                        axis);

                if (stopState == 1)
                {
                    throw new InvalidOperationException(
                        "轴已停止，但正限位没有触发。");
                }

                if (stopState < 0)
                {
                    throw new InvalidOperationException(
                        "检查轴停止状态失败，返回值：" +
                        stopState);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        "连续正限位搜索超时。");
                }
            }
        }

        private void StopAtPositiveLimit(
            int deviceId,
            int axis)
        {
            int stopResult =
                FmcNative.FMC4030_Stop_Single_Axis(
                    deviceId,
                    axis,
                    DeceleratedStop);

            if (stopResult != 0)
            {
                throw new InvalidOperationException(
                    "正限位触发，但减速停止命令失败，返回值：" +
                    stopResult);
            }

            WaitUntilStopped(
                deviceId,
                axis,
                TimeSpan.FromSeconds(3));
        }

        private static bool IsFinite(
            float value)
        {
            return !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private void MoveRelativeAndWait(
            int deviceId,
            int axis,
            float distance,
            float speed,
            float acceleration,
            float deceleration,
            TimeSpan timeout,
            bool allowPositiveLimitWhileReleasing)
        {
            AxisSnapshot before = ReadSnapshot(deviceId, axis);

            if (distance < 0 && before.NegativeLimitActive)
            {
                throw new InvalidOperationException(
                    "负限位已触发，禁止继续负向运动。");
            }

            if (distance > 0 && before.PositiveLimitActive)
            {
                throw new InvalidOperationException(
                    "正限位已触发，禁止继续正向运动。");
            }

            int moveResult =
                FmcNative.FMC4030_Jog_Single_Axis(
                    deviceId,
                    axis,
                    distance,
                    speed,
                    acceleration,
                    deceleration,
                    RelativeMove);

            if (moveResult != 0)
            {
                throw new InvalidOperationException(
                    "启动相对运动失败，返回值：" + moveResult);
            }

            DateTime deadline = DateTime.UtcNow.Add(timeout);
            int pollCount = 0;

            while (true)
            {
                Thread.Sleep(PollIntervalMilliseconds);
                ThrowIfCancellationRequested();
                pollCount++;

                AxisSnapshot snapshot =
                    ReadSnapshot(deviceId, axis);

                if (pollCount % 5 == 0)
                {
                    PrintSnapshot(snapshot);
                }

                if (distance < 0 && snapshot.NegativeLimitActive)
                {
                    throw new InvalidOperationException(
                        "负向退出时触发负限位。");
                }

                if (distance > 0 && snapshot.PositiveLimitActive)
                {
                    throw new InvalidOperationException(
                        "正向运动时触发正限位。");
                }

                if (!allowPositiveLimitWhileReleasing &&
                    snapshot.PositiveLimitActive)
                {
                    throw new InvalidOperationException(
                        "运动过程中检测到正限位。");
                }

                int stopState =
                    FmcNative.FMC4030_Check_Axis_Is_Stop(
                        deviceId,
                        axis);

                if (stopState == 1)
                {
                    return;
                }

                if (stopState < 0)
                {
                    throw new InvalidOperationException(
                        "检查轴停止状态失败，返回值：" + stopState);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        "相对运动超时。");
                }
            }
        }

        private static AxisSnapshot ReadSnapshot(
            int deviceId,
            int axis)
        {
            byte[] machineStatus = new byte[1024];

            int readResult =
                FmcNative.FMC4030_Get_Machine_Status(
                    deviceId,
                    machineStatus);

            if (readResult != 0)
            {
                throw new InvalidOperationException(
                    "读取轴状态失败，返回值：" + readResult);
            }

            return new AxisSnapshot(
                BitConverter.ToSingle(
                    machineStatus,
                    axis * 4),
                BitConverter.ToSingle(
                    machineStatus,
                    12 + axis * 4),
                BitConverter.ToUInt32(
                    machineStatus,
                    44 + axis * 4));
        }

        private static void EnsureAxisStopped(
            int deviceId,
            int axis)
        {
            int stopState =
                FmcNative.FMC4030_Check_Axis_Is_Stop(
                    deviceId,
                    axis);

            if (stopState != 1)
            {
                throw new InvalidOperationException(
                    GetAxisName(axis) +
                    " 轴当前没有停止，不能建立软件零点。返回值：" +
                    stopState);
            }
        }

        private void WaitUntilStopped(
            int deviceId,
            int axis,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < deadline)
            {
                ThrowIfCancellationRequested();

                int stopState =
                    FmcNative.FMC4030_Check_Axis_Is_Stop(
                        deviceId,
                        axis);

                if (stopState == 1)
                {
                    return;
                }

                if (stopState < 0)
                {
                    throw new InvalidOperationException(
                        "等待轴停止时读取状态失败，返回值：" +
                        stopState);
                }

                Thread.Sleep(PollIntervalMilliseconds);
            }

            throw new TimeoutException(
                "发送停止命令后，" +
                GetAxisName(axis) +
                " 轴没有及时停止。");
        }

        private void ThrowIfCancellationRequested()
        {
            if (cancellationRequested)
            {
                throw new OperationCanceledException(
                    "回零已取消，运动已停止。");
            }
        }

        private static void StopWithDeceleration(
            int deviceId,
            int axis)
        {
            try
            {
                FmcNative.FMC4030_Stop_Single_Axis(
                    deviceId,
                    axis,
                    DeceleratedStop);
            }
            catch
            {
                // 保留原始异常；通信中断时停止命令也可能失败。
            }
        }

        private static void PrintSnapshot(
            AxisSnapshot snapshot)
        {
            Console.WriteLine(
                "原始位置：" +
                snapshot.RawPosition.ToString("F3") +
                "，速度：" +
                snapshot.Speed.ToString("F3") +
                "，状态：0x" +
                snapshot.AxisStatus.ToString("X8"));
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

        private static void ValidateParameters(
            int axis,
            float speed,
            float acceleration,
            float deceleration,
            float releaseDistance,
            float maximumSeekDistance,
            TimeSpan moveTimeout)
        {
            if (axis < 0 || axis > 2)
            {
                throw new ArgumentOutOfRangeException(
                    "axis",
                    "轴号必须是 0、1 或 2。");
            }

            if (speed <= 0 ||
                acceleration <= 0 ||
                deceleration <= 0 ||
                releaseDistance <= 0 ||
                maximumSeekDistance <= 0 ||
                moveTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    "软件零点运动参数必须全部大于 0。");
            }
        }

        private sealed class AxisSnapshot
        {
            internal AxisSnapshot(
                float rawPosition,
                float speed,
                uint axisStatus)
            {
                RawPosition = rawPosition;
                Speed = speed;
                AxisStatus = axisStatus;
            }

            internal float RawPosition { get; private set; }

            internal float Speed { get; private set; }

            internal uint AxisStatus { get; private set; }

            internal bool NegativeLimitActive
            {
                get
                {
                    return (AxisStatus & NegativeLimit) != 0;
                }
            }

            internal bool PositiveLimitActive
            {
                get
                {
                    return (AxisStatus & PositiveLimit) != 0;
                }
            }
        }
    }
}
