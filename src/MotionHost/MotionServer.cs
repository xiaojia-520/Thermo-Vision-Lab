using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace MotionHost
{
    internal sealed class MotionServer
    {
        private const int StatusIntervalMilliseconds = 200;
        private const int StopAllWaitTimeoutMilliseconds = 3500;
        private const int ShutdownStopWaitTimeoutMilliseconds =
            500;

        private readonly string pipeName;
        private readonly int parentProcessId;
        private readonly object writerLock = new object();
        private readonly ControllerSession[] controllers;
        private readonly MotionSettingsStore settingsStore;

        private volatile bool shutdownRequested;
        private NamedPipeServerStream pipe;
        private StreamWriter writer;
        private Thread statusThread;

        private MotionServer(
            string pipeName,
            int parentProcessId)
        {
            this.pipeName = pipeName;
            this.parentProcessId = parentProcessId;
            settingsStore =
                new MotionSettingsStore();

            controllers =
                new ControllerSession[]
                {
                    new ControllerSession(
                        1,
                        0,
                        "192.168.1.31",
                        new int[] { 0, 1 },
                        settingsStore),
                    new ControllerSession(
                        2,
                        1,
                        "192.168.1.32",
                        new int[] { 0, 1 },
                        settingsStore),
                    new ControllerSession(
                        3,
                        2,
                        "192.168.1.33",
                        new int[] { 0, 1, 2 },
                        settingsStore)
                };
        }

        internal static int Run(
            string[] args)
        {
            string requestedPipeName =
                ReadArgument(args, "--pipe");

            int requestedParentProcessId =
                ReadIntArgument(
                    args,
                    "--parent-pid");

            if (string.IsNullOrWhiteSpace(
                requestedPipeName))
            {
                Console.WriteLine(
                    "--server 模式必须指定 --pipe。");
                return 5;
            }

            MotionServer server =
                new MotionServer(
                    requestedPipeName,
                    requestedParentProcessId);

            return server.RunInternal();
        }

        private int RunInternal()
        {
            try
            {
                pipe =
                    new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                StartParentWatcher();

                pipe.WaitForConnection();

                if (shutdownRequested)
                {
                    return 0;
                }

                writer =
                    new StreamWriter(
                        pipe,
                        new UTF8Encoding(false));
                writer.AutoFlush = true;

                StreamReader reader =
                    new StreamReader(
                        pipe,
                        Encoding.UTF8);

                SendLine("READY|1");
                StartStatusLoop();

                while (!shutdownRequested &&
                    pipe.IsConnected)
                {
                    string line = reader.ReadLine();

                    if (line == null)
                    {
                        break;
                    }

                    HandleCommand(line);
                }
            }
            catch (ObjectDisposedException)
            {
                if (!shutdownRequested)
                {
                    return 6;
                }
            }
            catch (IOException exception)
            {
                Console.WriteLine(
                    "命名管道通信失败：" +
                    exception.Message);

                if (!shutdownRequested)
                {
                    return 6;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    "MotionHost 常驻服务失败：" +
                    exception.Message);
                return 6;
            }
            finally
            {
                RequestShutdown();

                Thread[] shutdownStopThreads =
                    new Thread[controllers.Length];

                for (int index = 0;
                    index < controllers.Length;
                    index++)
                {
                    shutdownStopThreads[index] =
                        controllers[index]
                            .RequestStopForShutdown();
                }

                if (statusThread != null &&
                    statusThread.IsAlive)
                {
                    statusThread.Join(1500);
                }

                foreach (Thread stopThread
                    in shutdownStopThreads)
                {
                    if (stopThread != null &&
                        stopThread.IsAlive)
                    {
                        stopThread.Join(
                            ShutdownStopWaitTimeoutMilliseconds);
                    }
                }

                foreach (ControllerSession controller
                    in controllers)
                {
                    controller.Shutdown();
                }

                if (writer != null)
                {
                    try
                    {
                        writer.Dispose();
                    }
                    catch
                    {
                        // 管道可能已由退出请求提前关闭。
                    }
                }

                if (pipe != null)
                {
                    try
                    {
                        pipe.Dispose();
                    }
                    catch
                    {
                        // 管道关闭是幂等的退出清理。
                    }
                }
            }

            return 0;
        }

        private void StartStatusLoop()
        {
            statusThread =
                new Thread(
                    delegate()
                    {
                        while (!shutdownRequested)
                        {
                            foreach (
                                ControllerSession controller
                                in controllers)
                            {
                                ControllerSnapshot snapshot;

                                try
                                {
                                    snapshot =
                                        controller.ReadSnapshot();
                                }
                                catch (Exception exception)
                                {
                                    controller.RequestStop();
                                    controller
                                        .InvalidateZeroReferences();
                                    snapshot =
                                        controller
                                            .CreateFailureSnapshot(
                                                "状态轮询异常：" +
                                                exception.Message);
                                }

                                SendStatus(snapshot);
                            }

                            Thread.Sleep(
                                StatusIntervalMilliseconds);
                        }
                    });

            statusThread.IsBackground = true;
            statusThread.Name =
                "FMC4030 status polling";
            statusThread.Start();
        }

        private void HandleCommand(
            string line)
        {
            string[] parts = line.Split('|');

            if (parts.Length == 0)
            {
                return;
            }

            string command =
                parts[0].ToUpperInvariant();

            if (command == "HOME" &&
                parts.Length == 3)
            {
                int requestId;
                int controllerNumber;

                if (!int.TryParse(
                    parts[1],
                    out requestId) ||
                    !int.TryParse(
                        parts[2],
                        out controllerNumber))
                {
                    return;
                }

                ControllerSession controller =
                    FindController(
                        controllerNumber);

                if (controller == null)
                {
                    SendResult(
                        requestId,
                        false,
                        5,
                        "控制器编号必须是 1、2 或 3。");
                    return;
                }

                string error;

                bool started =
                    controller.TryStartHome(
                        requestId,
                        SendProgress,
                        SendResult,
                        out error);

                if (!started)
                {
                    SendResult(
                        requestId,
                        false,
                        2,
                        error);
                }

                return;
            }

            if (command == "CALIBRATE_RANGE" &&
                parts.Length == 3)
            {
                int requestId;
                int controllerNumber;

                if (!int.TryParse(
                        parts[1],
                        out requestId) ||
                    !int.TryParse(
                        parts[2],
                        out controllerNumber))
                {
                    return;
                }

                ControllerSession controller =
                    FindController(
                        controllerNumber);

                if (controller == null)
                {
                    SendResult(
                        requestId,
                        false,
                        5,
                        "控制器编号必须是 1、2 或 3。");
                    return;
                }

                string error;

                bool started =
                    controller
                        .TryStartRangeCalibration(
                            requestId,
                            SendProgress,
                            SendResult,
                            out error);

                if (!started)
                {
                    SendResult(
                        requestId,
                        false,
                        2,
                        error);
                }

                return;
            }

            if (command == "STOP" &&
                parts.Length == 3)
            {
                int requestId;
                int controllerNumber;

                if (!int.TryParse(
                    parts[1],
                    out requestId) ||
                    !int.TryParse(
                        parts[2],
                        out controllerNumber))
                {
                    return;
                }

                ControllerSession controller =
                    FindController(
                        controllerNumber);

                if (controller == null)
                {
                    SendResult(
                        requestId,
                        false,
                        5,
                        "找不到指定控制器。");
                    return;
                }

                controller.RequestStop();

                ThreadPool.QueueUserWorkItem(
                    delegate
                    {
                        string stopError;
                        bool stopped =
                            controller
                                .VerifyStopAndWait(
                                    out stopError);

                        SendResult(
                            requestId,
                            stopped,
                            stopped ? 0 : 2,
                            stopped
                                ? "已发送停止命令并确认全部轴停止。"
                                : stopError);
                    });
                return;
            }

            if (command == "STOP_ALL" &&
                parts.Length == 3)
            {
                int requestId;

                if (!int.TryParse(
                    parts[1],
                    out requestId))
                {
                    return;
                }

                foreach (ControllerSession controller
                    in controllers)
                {
                    controller.RequestStop();
                }

                ThreadPool.QueueUserWorkItem(
                    delegate
                    {
                        StopAllControllers(requestId);
                    });
                return;
            }

            if ((command == "MOVE_REL" ||
                command == "MOVE_ABS") &&
                parts.Length == 5)
            {
                int requestId;
                int controllerNumber;
                int axis;
                float value;

                if (!int.TryParse(
                        parts[1],
                        out requestId) ||
                    !int.TryParse(
                        parts[2],
                        out controllerNumber) ||
                    !int.TryParse(
                        parts[3],
                        out axis) ||
                    !float.TryParse(
                        parts[4],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    return;
                }

                ControllerSession controller =
                    FindController(
                        controllerNumber);

                if (controller == null)
                {
                    SendResult(
                        requestId,
                        false,
                        5,
                        "找不到指定控制器。");
                    return;
                }

                string error;

                bool started =
                    controller.TryStartMove(
                        requestId,
                        axis,
                        value,
                        command == "MOVE_ABS",
                        SendProgress,
                        SendResult,
                        out error);

                if (!started)
                {
                    SendResult(
                        requestId,
                        false,
                        2,
                        error);
                }

                return;
            }

            if (command == "SET_LIMIT" &&
                parts.Length == 6)
            {
                int requestId;
                int controllerNumber;
                int axis;
                float minimum;
                float maximum;

                if (!int.TryParse(
                        parts[1],
                        out requestId) ||
                    !int.TryParse(
                        parts[2],
                        out controllerNumber) ||
                    !int.TryParse(
                        parts[3],
                        out axis) ||
                    !float.TryParse(
                        parts[4],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out minimum) ||
                    !float.TryParse(
                        parts[5],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out maximum))
                {
                    return;
                }

                ControllerSession controller =
                    FindController(
                        controllerNumber);

                if (controller == null)
                {
                    SendResult(
                        requestId,
                        false,
                        5,
                        "找不到指定控制器。");
                    return;
                }

                try
                {
                    controller.SetSoftwareLimits(
                        axis,
                        minimum,
                        maximum);

                    SendResult(
                        requestId,
                        true,
                        0,
                        GetAxisName(axis) +
                        " 轴软件限位已保存。");
                }
                catch (Exception exception)
                {
                    SendResult(
                        requestId,
                        false,
                        2,
                        exception.Message);
                }

                return;
            }

            if (command == "PING")
            {
                SendLine("PONG");
                return;
            }

            if (command == "SHUTDOWN")
            {
                RequestShutdown();
            }
        }

        private ControllerSession FindController(
            int controllerNumber)
        {
            foreach (ControllerSession controller
                in controllers)
            {
                if (controller.ControllerNumber ==
                    controllerNumber)
                {
                    return controller;
                }
            }

            return null;
        }

        private void StopAllControllers(
            int requestId)
        {
            bool[] stopped =
                new bool[controllers.Length];
            string[] errors =
                new string[controllers.Length];
            ManualResetEvent completed =
                new ManualResetEvent(false);
            int remaining = controllers.Length;

            foreach (ControllerSession controller
                in controllers)
            {
                try
                {
                    controller.RequestStop();
                }
                catch (Exception exception)
                {
                    errors[
                        controller.ControllerNumber - 1] =
                            exception.Message;
                }
            }

            for (int index = 0;
                index < controllers.Length;
                index++)
            {
                int controllerIndex = index;

                ThreadPool.QueueUserWorkItem(
                    delegate
                    {
                        try
                        {
                            if (errors[controllerIndex] == null)
                            {
                                string error;
                                stopped[controllerIndex] =
                                    controllers[controllerIndex]
                                        .VerifyStopAndWait(
                                            out error);
                                errors[controllerIndex] =
                                    error;
                            }
                        }
                        catch (Exception exception)
                        {
                            errors[controllerIndex] =
                                exception.Message;
                        }
                        finally
                        {
                            if (Interlocked.Decrement(
                                ref remaining) == 0)
                            {
                                completed.Set();
                            }
                        }
                    });
            }

            bool allCompleted =
                completed.WaitOne(
                    StopAllWaitTimeoutMilliseconds);
            StringBuilder message =
                new StringBuilder();
            bool allStopped =
                allCompleted;

            for (int index = 0;
                index < controllers.Length;
                index++)
            {
                bool controllerStopped =
                    stopped[index] &&
                    string.IsNullOrWhiteSpace(
                        errors[index]);
                allStopped =
                    allStopped &&
                    controllerStopped;

                message.Append(
                    controllers[index].ControllerNumber);
                message.Append(
                    controllerStopped
                        ? " 号控制器已确认停止。"
                        : " 号控制器未确认停止：");

                if (!controllerStopped)
                {
                    message.Append(
                        string.IsNullOrWhiteSpace(
                            errors[index])
                            ? "停止确认超时。"
                            : errors[index]);
                }

                if (index < controllers.Length - 1)
                {
                    message.AppendLine();
                }
            }

            SendResult(
                requestId,
                allStopped,
                allStopped ? 0 : 2,
                message.ToString());
        }

        private void SendStatus(
            ControllerSnapshot snapshot)
        {
            StringBuilder line =
                new StringBuilder();

            line.Append("STATUS|");
            line.Append(snapshot.ControllerNumber);
            line.Append('|');
            line.Append(
                snapshot.Connected ? "1" : "0");
            line.Append('|');
            line.Append(
                EncodeText(snapshot.ErrorMessage));
            line.Append('|');
            line.Append(snapshot.Axes.Length);

            foreach (AxisSnapshot axis
                in snapshot.Axes)
            {
                line.Append('|');
                line.Append(axis.Axis);
                line.Append('|');
                line.Append(
                    axis.Position.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
                line.Append('|');
                line.Append(
                    axis.SoftwarePosition.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
                line.Append('|');
                line.Append(
                    axis.Speed.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
                line.Append('|');
                line.Append(axis.Status);
                line.Append('|');
                line.Append(
                    axis.HasSoftwareZero
                        ? "1"
                        : "0");
                line.Append('|');
                line.Append(
                    axis.HasSoftwareLimits
                        ? "1"
                        : "0");
                line.Append('|');
                line.Append(
                    axis.Minimum.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
                line.Append('|');
                line.Append(
                    axis.Maximum.ToString(
                        "R",
                        CultureInfo.InvariantCulture));
            }

            SendLine(line.ToString());
        }

        private void SendProgress(
            int controllerNumber,
            string message)
        {
            SendLine(
                "PROGRESS|" +
                controllerNumber +
                "|" +
                EncodeText(message));
        }

        private void SendResult(
            int requestId,
            bool success,
            int exitCode,
            string message)
        {
            SendLine(
                "RESULT|" +
                requestId +
                "|" +
                (success ? "1" : "0") +
                "|" +
                exitCode +
                "|" +
                EncodeText(message));
        }

        private void SendLine(
            string line)
        {
            if (shutdownRequested ||
                writer == null)
            {
                return;
            }

            try
            {
                lock (writerLock)
                {
                    writer.WriteLine(line);
                }
            }
            catch
            {
                RequestShutdown();
            }
        }

        private void StartParentWatcher()
        {
            if (parentProcessId <= 0)
            {
                return;
            }

            Thread watcher =
                new Thread(
                    delegate()
                    {
                        try
                        {
                            System.Diagnostics.Process parent =
                                System.Diagnostics.Process
                                    .GetProcessById(
                                        parentProcessId);

                            parent.WaitForExit();
                        }
                        catch
                        {
                            // 父进程不存在时同样关闭服务。
                        }

                        RequestShutdown();
                    });

            watcher.IsBackground = true;
            watcher.Name =
                "ThermoVision parent watcher";
            watcher.Start();
        }

        private void RequestShutdown()
        {
            if (shutdownRequested)
            {
                return;
            }

            shutdownRequested = true;

            NamedPipeServerStream currentPipe = pipe;

            if (currentPipe != null)
            {
                try
                {
                    currentPipe.Dispose();
                }
                catch
                {
                    // 服务正在退出，无需覆盖原始错误。
                }
            }
        }

        private static string ReadArgument(
            string[] args,
            string expected)
        {
            for (int index = 0;
                index < args.Length - 1;
                index++)
            {
                if (string.Equals(
                    args[index],
                    expected,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        private static int ReadIntArgument(
            string[] args,
            string expected)
        {
            int value;

            return int.TryParse(
                ReadArgument(args, expected),
                out value)
                    ? value
                    : 0;
        }

        private static string EncodeText(
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value));
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

    }

    internal sealed class ControllerSession
    {
        private const int ControllerPort = 8088;
        private const int ReachabilityTimeoutMilliseconds =
            250;
        private const int DeceleratedStop = 1;
        private const int RelativeMove = 1;
        private const int PollIntervalMilliseconds = 50;
        private const float MoveSpeed = 4.0f;
        private const float KnownZeroSpeed = 10.0f;
        private const float UnknownZeroSpeed = 1.0f;
        private const float SlowMoveSpeed = 1.0f;
        private const float SlowdownDistance = 10.0f;
        private const float SlowdownSegmentDistance = 2.0f;
        private const float ZeroAcceleration = 1.0f;
        private const float ZeroDeceleration = 1.0f;
        private const float ReleaseDistance = 30.0f;
        private const float MaximumSeekDistance = 150.0f;
        private const float MaximumRangeCalibrationDistance =
            3000.0f;
        private const float RangeCalibrationStartTolerance =
            1.0f;
        private const int MoveTimeoutSeconds = 120;
        private const int RangeCalibrationTimeoutMinutes = 60;
        private const int StopVerificationTimeoutMilliseconds =
            3000;
        private const int CloseDeviceTimeoutMilliseconds =
            1500;

        private readonly object stateLock =
            new object();
        private readonly int deviceId;
        private readonly string ipAddress;
        private readonly int[] axes;
        private readonly MotionSettingsStore settingsStore;
        private readonly bool[] zeroReferenceValid =
            new bool[3];
        private readonly ManualResetEvent commandFinished =
            new ManualResetEvent(true);

        private bool connected;
        private bool commandRunning;
        private bool stopRequested;
        private int consecutiveReadFailures;
        private DateTime nextConnectAttemptUtc;
        private FmcSoftwareZero activeSoftwareZero;
        private int activeAxis = -1;

        internal ControllerSession(
            int controllerNumber,
            int deviceId,
            string ipAddress,
            int[] axes,
            MotionSettingsStore settingsStore)
        {
            ControllerNumber = controllerNumber;
            this.deviceId = deviceId;
            this.ipAddress = ipAddress;
            this.axes = axes;
            this.settingsStore = settingsStore;

            foreach (int axis in axes)
            {
                zeroReferenceValid[axis] = true;
            }
        }

        internal int ControllerNumber
        {
            get;
            private set;
        }

        internal bool TryConnect()
        {
            lock (stateLock)
            {
                if (connected ||
                    commandRunning ||
                    DateTime.UtcNow <
                        nextConnectAttemptUtc)
                {
                    return connected;
                }

                nextConnectAttemptUtc =
                    DateTime.UtcNow.AddSeconds(2);
            }

            if (!IsControllerReachable())
            {
                return false;
            }

            int result =
                FmcNative.FMC4030_Open_Device(
                    deviceId,
                    ipAddress,
                    ControllerPort);

            lock (stateLock)
            {
                connected = result == 0;
                consecutiveReadFailures = 0;
                return connected;
            }
        }

        private bool IsControllerReachable()
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply =
                        ping.Send(
                            ipAddress,
                            ReachabilityTimeoutMilliseconds);

                    return reply != null &&
                        reply.Status ==
                            IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        internal ControllerSnapshot ReadSnapshot()
        {
            if (!IsConnected())
            {
                TryConnect();
            }

            if (!IsConnected())
            {
                return ControllerSnapshot.Disconnected(
                    ControllerNumber,
                    axes,
                    "控制器未连接");
            }

            byte[] machineStatus =
                new byte[1024];

            int result =
                FmcNative.FMC4030_Get_Machine_Status(
                    deviceId,
                    machineStatus);

            if (result != 0)
            {
                HandleReadFailure();

                return ControllerSnapshot.Disconnected(
                    ControllerNumber,
                    axes,
                    "读取状态失败，返回值：" +
                    result);
            }

            lock (stateLock)
            {
                consecutiveReadFailures = 0;
            }

            AxisSnapshot[] axisSnapshots =
                new AxisSnapshot[axes.Length];

            for (int index = 0;
                index < axes.Length;
                index++)
            {
                int axis = axes[index];

                float rawPosition =
                    BitConverter.ToSingle(
                        machineStatus,
                        axis * 4);

                float speed =
                    BitConverter.ToSingle(
                        machineStatus,
                        12 + axis * 4);

                if (!IsFinite(rawPosition) ||
                    !IsFinite(speed))
                {
                    return ControllerSnapshot.Disconnected(
                        ControllerNumber,
                        axes,
                        "控制器返回了无效的位置或速度数据");
                }

                float rawZeroPosition;
                bool hasSoftwareZero =
                    TryGetUsableZero(
                        axis,
                        out rawZeroPosition);

                float minimum;
                float maximum;
                bool hasSoftwareLimits =
                    settingsStore.TryGetLimits(
                        ControllerNumber,
                        axis,
                        out minimum,
                        out maximum);

                float softwarePosition =
                    hasSoftwareZero
                        ? rawZeroPosition -
                            rawPosition
                        : 0;

                if (!IsFinite(softwarePosition) ||
                    !IsFinite(minimum) ||
                    !IsFinite(maximum))
                {
                    return ControllerSnapshot.Disconnected(
                        ControllerNumber,
                        axes,
                        "软件坐标或软件限位包含无效数据");
                }

                axisSnapshots[index] =
                    new AxisSnapshot(
                        axis,
                        rawPosition,
                        softwarePosition,
                        speed,
                        BitConverter.ToUInt32(
                            machineStatus,
                            44 + axis * 4),
                        hasSoftwareZero,
                        hasSoftwareLimits,
                        minimum,
                        maximum);
            }

            return new ControllerSnapshot(
                ControllerNumber,
                true,
                string.Empty,
                axisSnapshots);
        }

        internal ControllerSnapshot CreateFailureSnapshot(
            string errorMessage)
        {
            return ControllerSnapshot.Disconnected(
                ControllerNumber,
                axes,
                errorMessage);
        }

        internal void InvalidateZeroReferences()
        {
            lock (stateLock)
            {
                foreach (int axis in axes)
                {
                    zeroReferenceValid[axis] = false;
                }
            }

            foreach (int axis in axes)
            {
                try
                {
                    settingsStore.RemoveZero(
                        ControllerNumber,
                        axis);
                }
                catch
                {
                    // 本次会话的内存标记已失效，移动仍会被禁止。
                    // 磁盘写入问题会在下次回零保存时明确报错。
                }
            }
        }

        internal bool TryStartHome(
            int requestId,
            Action<int, string> progress,
            Action<int, bool, int, string> result,
            out string error)
        {
            lock (stateLock)
            {
                if (!connected)
                {
                    error =
                        "控制器未连接，不能执行回零。";
                    return false;
                }

                if (commandRunning)
                {
                    error =
                        "该控制器正在执行运动命令。";
                    return false;
                }

                commandRunning = true;
                stopRequested = false;
                commandFinished.Reset();
            }

            ThreadPool.QueueUserWorkItem(
                delegate
                {
                    RunHome(
                        requestId,
                        progress,
                        result);
                });

            error = null;
            return true;
        }

        internal bool TryStartMove(
            int requestId,
            int axis,
            float value,
            bool absolute,
            Action<int, string> progress,
            Action<int, bool, int, string> result,
            out string error)
        {
            if (!IsSupportedAxis(axis))
            {
                error =
                    "当前控制器不支持该轴。";
                return false;
            }

            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                !absolute && value == 0)
            {
                error =
                    "移动距离或目标位置无效。";
                return false;
            }

            float rawZeroPosition;

            if (!TryGetUsableZero(
                axis,
                out rawZeroPosition))
            {
                error =
                    GetAxisName(axis) +
                    " 轴尚未回零，禁止移动。";
                return false;
            }

            float minimum;
            float maximum;

            if (!settingsStore.TryGetLimits(
                ControllerNumber,
                axis,
                out minimum,
                out maximum))
            {
                error =
                    GetAxisName(axis) +
                    " 轴尚未配置软件限位，禁止移动。";
                return false;
            }

            lock (stateLock)
            {
                if (!connected)
                {
                    error =
                        "控制器未连接，不能执行移动。";
                    return false;
                }

                if (commandRunning)
                {
                    error =
                        "该控制器正在执行运动命令。";
                    return false;
                }

                commandRunning = true;
                stopRequested = false;
                activeAxis = axis;
                commandFinished.Reset();
            }

            ThreadPool.QueueUserWorkItem(
                delegate
                {
                    RunMove(
                        requestId,
                        axis,
                        value,
                        absolute,
                        progress,
                        result);
                });

            error = null;
            return true;
        }

        internal bool TryStartRangeCalibration(
            int requestId,
            Action<int, string> progress,
            Action<int, bool, int, string> result,
            out string error)
        {
            foreach (int axis in axes)
            {
                float rawZeroPosition;

                if (!TryGetUsableZero(
                        axis,
                        out rawZeroPosition))
                {
                    error =
                        GetAxisName(axis) +
                        " 轴尚未完成回零，不能标定负限位。";
                    return false;
                }
            }

            lock (stateLock)
            {
                if (!connected)
                {
                    error =
                        "控制器未连接，不能标定负限位。";
                    return false;
                }

                if (commandRunning)
                {
                    error =
                        "该控制器正在执行运动命令。";
                    return false;
                }

                commandRunning = true;
                stopRequested = false;
                commandFinished.Reset();
            }

            ThreadPool.QueueUserWorkItem(
                delegate
                {
                    RunRangeCalibration(
                        requestId,
                        progress,
                        result);
                });

            error = null;
            return true;
        }

        internal void SetSoftwareLimits(
            int axis,
            float minimum,
            float maximum)
        {
            if (!IsSupportedAxis(axis))
            {
                throw new ArgumentOutOfRangeException(
                    "axis",
                    "当前控制器不支持该轴。");
            }

            lock (stateLock)
            {
                if (commandRunning)
                {
                    throw new InvalidOperationException(
                        "轴体运动过程中不能修改软件限位。");
                }
            }

            settingsStore.SetLimits(
                ControllerNumber,
                axis,
                minimum,
                maximum);
        }

        internal void RequestStop()
        {
            SignalStopRequest();
            SendDeceleratedStopCommands();
        }

        internal Thread RequestStopForShutdown()
        {
            SignalStopRequest();

            if (!IsConnected())
            {
                return null;
            }

            Thread stopThread =
                new Thread(
                    delegate()
                    {
                        SendDeceleratedStopCommands();
                    });

            stopThread.IsBackground = true;
            stopThread.Name =
                "FMC4030 shutdown stop " +
                deviceId;
            stopThread.Start();
            return stopThread;
        }

        private void SignalStopRequest()
        {
            FmcSoftwareZero softwareZero;

            lock (stateLock)
            {
                stopRequested = true;
                softwareZero = activeSoftwareZero;
            }

            if (softwareZero != null)
            {
                softwareZero.RequestCancellation();
            }
        }

        internal bool RequestStopAndVerify(
            out string error)
        {
            RequestStop();

            return VerifyStopAndWait(
                out error);
        }

        internal bool VerifyStopAndWait(
            out string error)
        {
            DateTime deadline =
                DateTime.UtcNow.AddMilliseconds(
                    StopVerificationTimeoutMilliseconds);
            string lastError = null;
            DateTime nextStopRetryUtc =
                DateTime.MinValue;

            while (DateTime.UtcNow < deadline)
            {
                if (DateTime.UtcNow >=
                    nextStopRetryUtc)
                {
                    string stopError =
                        SendDeceleratedStopCommands();

                    if (!string.IsNullOrWhiteSpace(
                        stopError))
                    {
                        lastError = stopError;
                    }

                    nextStopRetryUtc =
                        DateTime.UtcNow
                            .AddMilliseconds(200);
                }

                bool allStopped = true;

                foreach (int axis in axes)
                {
                    try
                    {
                        int stopState =
                            FmcNative
                                .FMC4030_Check_Axis_Is_Stop(
                                    deviceId,
                                    axis);

                        if (stopState != 1)
                        {
                            allStopped = false;

                            if (stopState < 0)
                            {
                                lastError =
                                    GetAxisName(axis) +
                                    " 轴停止状态返回 " +
                                    stopState;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        allStopped = false;
                        lastError =
                            GetAxisName(axis) +
                            " 轴停止确认异常：" +
                            exception.Message;
                    }
                }

                if (allStopped)
                {
                    error = null;
                    return true;
                }

                Thread.Sleep(
                    PollIntervalMilliseconds);
            }

            error =
                "停止命令已发送，但在 3 秒内未能确认全部轴停止。" +
                (string.IsNullOrWhiteSpace(lastError)
                    ? string.Empty
                    : " " + lastError) +
                " 请立即检查现场并使用硬件急停。";
            return false;
        }

        private string SendDeceleratedStopCommands()
        {
            if (!IsConnected())
            {
                return null;
            }

            StringBuilder errors =
                new StringBuilder();

            foreach (int axis in axes)
            {
                try
                {
                    int stopResult =
                        FmcNative
                            .FMC4030_Stop_Single_Axis(
                                deviceId,
                                axis,
                                DeceleratedStop);

                    if (stopResult != 0)
                    {
                        if (errors.Length > 0)
                        {
                            errors.Append(" ");
                        }

                        errors.Append(
                            GetAxisName(axis));
                        errors.Append(
                            " 轴立即停止返回 ");
                        errors.Append(
                            stopResult);
                        errors.Append("。");
                    }
                }
                catch (Exception exception)
                {
                    if (errors.Length > 0)
                    {
                        errors.Append(" ");
                    }

                    errors.Append(
                        GetAxisName(axis));
                    errors.Append(
                        " 轴立即停止异常：");
                    errors.Append(
                        exception.Message);
                    errors.Append("。");
                }
            }

            return errors.Length == 0
                ? null
                : errors.ToString();
        }

        internal void Shutdown()
        {
            SignalStopRequest();

            if (!commandFinished.WaitOne(
                StopVerificationTimeoutMilliseconds))
            {
                // 工作线程仍可能位于本机 DLL 中；不要在它返回前关闭
                // 设备或释放同步对象，进程退出时由系统回收资源。
                return;
            }

            bool shouldClose;

            lock (stateLock)
            {
                shouldClose = connected;
                connected = false;
            }

            if (shouldClose)
            {
                Thread closeThread =
                    new Thread(
                        delegate()
                        {
                            try
                            {
                                FmcNative.FMC4030_Close_Device(
                                    deviceId);
                            }
                            catch
                            {
                                // 退出时不覆盖原始错误。
                            }
                        });

                closeThread.IsBackground = true;
                closeThread.Name =
                    "FMC4030 close device " +
                    deviceId;
                closeThread.Start();
                closeThread.Join(
                    CloseDeviceTimeoutMilliseconds);
            }

            commandFinished.Dispose();
        }

        private void RunHome(
            int requestId,
            Action<int, string> progress,
            Action<int, bool, int, string> result)
        {
            try
            {
                ThrowIfStopRequested();

                bool[] zeroKnownBeforeHome =
                    new bool[3];
                float[] knownRawZeroPositions =
                    new float[3];

                foreach (int configuredAxis in axes)
                {
                    int stopState =
                        FmcNative
                            .FMC4030_Check_Axis_Is_Stop(
                                deviceId,
                                configuredAxis);

                    if (stopState != 1)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(configuredAxis) +
                            " 轴当前没有停止，不能开始回零。" +
                            "软件零点尚未清除。返回值：" +
                            stopState);
                    }
                }

                foreach (int configuredAxis in axes)
                {
                    float existingRawZeroPosition;

                    zeroKnownBeforeHome[configuredAxis] =
                        settingsStore.TryGetZero(
                            ControllerNumber,
                            configuredAxis,
                            out existingRawZeroPosition);
                    knownRawZeroPositions[configuredAxis] =
                        existingRawZeroPosition;

                    SetZeroReferenceValid(
                        configuredAxis,
                        false);

                    settingsStore.RemoveZero(
                        ControllerNumber,
                        configuredAxis);
                }

                foreach (int axis in axes)
                {
                    ThrowIfStopRequested();

                    FmcSoftwareZero softwareZero =
                        new FmcSoftwareZero();

                    bool shouldStop;

                    lock (stateLock)
                    {
                        activeSoftwareZero =
                            softwareZero;
                        activeAxis = axis;
                        shouldStop = stopRequested;
                    }

                    if (shouldStop)
                    {
                        softwareZero.RequestStop(
                            deviceId,
                            axis);
                    }

                    progress(
                        ControllerNumber,
                        GetAxisName(axis) +
                        " 轴正在回零");

                    float homingSpeed =
                        zeroKnownBeforeHome[axis]
                            ? KnownZeroSpeed
                            : UnknownZeroSpeed;

                    float rawZeroPosition =
                        softwareZero
                            .EstablishAtPositiveLimit(
                                deviceId,
                                axis,
                                homingSpeed,
                                ZeroAcceleration,
                                ZeroDeceleration,
                                zeroKnownBeforeHome[axis]
                                    ? (float?)
                                        knownRawZeroPositions[axis]
                                    : null,
                                ReleaseDistance,
                                MaximumSeekDistance,
                                TimeSpan.FromSeconds(
                                    MoveTimeoutSeconds));

                    try
                    {
                        settingsStore.SetZero(
                            ControllerNumber,
                            axis,
                            rawZeroPosition);
                        SetZeroReferenceValid(
                            axis,
                            true);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴机械回零已完成，但软件零点保存失败；" +
                            "为安全起见该轴仍禁止定位。原因：" +
                            exception.Message,
                            exception);
                    }

                    progress(
                        ControllerNumber,
                        GetAxisName(axis) +
                        " 轴回零完成");

                    lock (stateLock)
                    {
                        activeSoftwareZero = null;
                        activeAxis = -1;
                    }
                }

                result(
                    requestId,
                    true,
                    0,
                    ControllerNumber +
                    " 号轴回零完成。");
            }
            catch (OperationCanceledException exception)
            {
                string message =
                    AppendStopVerificationWarning(
                        exception.Message);

                result(
                    requestId,
                    false,
                    3,
                    message);
            }
            catch (Exception exception)
            {
                string message =
                    AppendStopVerificationWarning(
                        exception.Message);

                result(
                    requestId,
                    false,
                    2,
                    message);
            }
            finally
            {
                lock (stateLock)
                {
                    activeSoftwareZero = null;
                    activeAxis = -1;
                    commandRunning = false;
                }

                commandFinished.Set();
            }
        }

        private void RunRangeCalibration(
            int requestId,
            Action<int, string> progress,
            Action<int, bool, int, string> result)
        {
            try
            {
                ThrowIfStopRequested();

                float[] rawZeroPositions =
                    new float[3];

                foreach (int axis in axes)
                {
                    int stopState =
                        FmcNative
                            .FMC4030_Check_Axis_Is_Stop(
                                deviceId,
                                axis);

                    if (stopState != 1)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴当前没有停止，不能开始标定负限位。" +
                            "返回值：" +
                            stopState);
                    }

                    if (!TryGetUsableZero(
                            axis,
                            out rawZeroPositions[axis]))
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴软件零点已失效，请重新回零。");
                    }

                    AxisMotionState state =
                        ReadAxisMotionState(axis);
                    float softwarePosition =
                        rawZeroPositions[axis] -
                        state.RawPosition;

                    if (Math.Abs(
                            softwarePosition -
                            ReleaseDistance) >
                        RangeCalibrationStartTolerance)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴当前软件位置为 " +
                            softwarePosition.ToString("F3") +
                            "，必须重新回零并停在正限位退出 " +
                            ReleaseDistance.ToString("F0") +
                            " 的位置后才能标定负限位。");
                    }

                    const uint negativeLimit = 0x0010;
                    const uint positiveLimit = 0x0020;

                    if ((state.Status & negativeLimit) != 0 ||
                        (state.Status & positiveLimit) != 0)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴当前仍触发物理限位，不能开始标定。");
                    }
                }

                StringBuilder summary =
                    new StringBuilder();

                foreach (int axis in axes)
                {
                    ThrowIfStopRequested();

                    lock (stateLock)
                    {
                        activeAxis = axis;
                    }

                    progress(
                        ControllerNumber,
                        GetAxisName(axis) +
                        " 轴正以速度 " +
                        SlowMoveSpeed.ToString("F1") +
                        " 寻找负限位");

                    float maximumTravel =
                        CalibrateAxisRange(
                            axis,
                            rawZeroPositions[axis]);
                    float rawNegativeLimitPosition =
                        rawZeroPositions[axis] -
                        maximumTravel;

                    settingsStore.SetLimits(
                        ControllerNumber,
                        axis,
                        0,
                        maximumTravel);

                    progress(
                        ControllerNumber,
                        GetAxisName(axis) +
                        " 轴负限位标定完成，原始位置 " +
                        rawNegativeLimitPosition
                            .ToString("F3") +
                        "，最大行程 " +
                        maximumTravel.ToString("F3"));

                    if (summary.Length > 0)
                    {
                        summary.AppendLine();
                    }

                    summary.Append(
                        GetAxisName(axis));
                    summary.Append(
                        " 轴负限位原始位置：");
                    summary.Append(
                        rawNegativeLimitPosition
                            .ToString("F3"));
                    summary.Append(
                        "，最大行程：");
                    summary.Append(
                        maximumTravel.ToString("F3"));
                }

                result(
                    requestId,
                    true,
                    0,
                    ControllerNumber +
                    " 号控制器负限位与最大行程标定完成。" +
                    Environment.NewLine +
                    summary);
            }
            catch (OperationCanceledException exception)
            {
                string message =
                    AppendStopVerificationWarning(
                        exception.Message);

                result(
                    requestId,
                    false,
                    3,
                    message);
            }
            catch (Exception exception)
            {
                string message =
                    AppendStopVerificationWarning(
                        exception.Message);

                result(
                    requestId,
                    false,
                    2,
                    message);
            }
            finally
            {
                lock (stateLock)
                {
                    activeAxis = -1;
                    commandRunning = false;
                }

                commandFinished.Set();
            }
        }

        private float CalibrateAxisRange(
            int axis,
            float rawZeroPosition)
        {
            const uint negativeLimit = 0x0010;
            const uint positiveLimit = 0x0020;

            DateTime deadline =
                DateTime.UtcNow.AddMinutes(
                    RangeCalibrationTimeoutMinutes);

            int moveResult =
                FmcNative
                    .FMC4030_Jog_Single_Axis(
                        deviceId,
                        axis,
                        -MaximumRangeCalibrationDistance,
                        SlowMoveSpeed,
                        ZeroAcceleration,
                        ZeroDeceleration,
                        RelativeMove);

            if (moveResult != 0)
            {
                throw new InvalidOperationException(
                    GetAxisName(axis) +
                    " 轴启动负限位搜索失败，返回值：" +
                    moveResult);
            }

            while (true)
            {
                Thread.Sleep(
                    PollIntervalMilliseconds);
                ThrowIfStopRequested();

                AxisMotionState state =
                    ReadAxisMotionState(axis);

                if ((state.Status & positiveLimit) != 0)
                {
                    throw new InvalidOperationException(
                        GetAxisName(axis) +
                        " 轴负向搜索过程中异常触发正限位。");
                }

                if ((state.Status & negativeLimit) != 0)
                {
                    float triggeredRawPosition =
                        state.RawPosition;
                    string stopError;

                    if (!TryStopAxisAndVerify(
                            axis,
                            out stopError))
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴负限位已触发，但停止确认失败：" +
                            stopError);
                    }

                    float maximumTravel =
                        rawZeroPosition -
                        triggeredRawPosition;

                    if (!IsFinite(maximumTravel) ||
                        maximumTravel <= ReleaseDistance)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴测得的最大行程无效：" +
                            maximumTravel.ToString("F3"));
                    }

                    ReleaseNegativeLimit(
                        axis);

                    return maximumTravel;
                }

                int stopState =
                    FmcNative
                        .FMC4030_Check_Axis_Is_Stop(
                            deviceId,
                            axis);

                if (stopState == 1)
                {
                    throw new InvalidOperationException(
                        GetAxisName(axis) +
                        " 轴已走完最大搜索距离 " +
                        MaximumRangeCalibrationDistance
                            .ToString("F0") +
                        "，但负限位没有触发。");
                }

                if (stopState < 0)
                {
                    throw new InvalidOperationException(
                        GetAxisName(axis) +
                        " 轴读取停止状态失败，返回值：" +
                        stopState);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        GetAxisName(axis) +
                        " 轴标定负限位超时。");
                }
            }
        }

        private void ReleaseNegativeLimit(
            int axis)
        {
            const uint negativeLimit = 0x0010;
            const uint positiveLimit = 0x0020;

            int moveResult =
                FmcNative
                    .FMC4030_Jog_Single_Axis(
                        deviceId,
                        axis,
                        ReleaseDistance,
                        SlowMoveSpeed,
                        ZeroAcceleration,
                        ZeroDeceleration,
                        RelativeMove);

            if (moveResult != 0)
            {
                throw new InvalidOperationException(
                    GetAxisName(axis) +
                    " 轴退出负限位失败，返回值：" +
                    moveResult);
            }

            DateTime releaseDeadline =
                DateTime.UtcNow.AddSeconds(
                    MoveTimeoutSeconds);

            while (true)
            {
                Thread.Sleep(
                    PollIntervalMilliseconds);
                ThrowIfStopRequested();

                AxisMotionState state =
                    ReadAxisMotionState(axis);

                if ((state.Status & positiveLimit) != 0)
                {
                    throw new InvalidOperationException(
                        GetAxisName(axis) +
                        " 轴退出负限位时异常触发正限位。");
                }

                int stopState =
                    FmcNative
                        .FMC4030_Check_Axis_Is_Stop(
                            deviceId,
                            axis);

                if (stopState == 1)
                {
                    if ((state.Status & negativeLimit) != 0)
                    {
                        throw new InvalidOperationException(
                            GetAxisName(axis) +
                            " 轴退出 " +
                            ReleaseDistance.ToString("F0") +
                            " 后负限位仍然触发。");
                    }

                    return;
                }

                if (stopState < 0)
                {
                    throw new InvalidOperationException(
                        GetAxisName(axis) +
                        " 轴退出负限位时读取停止状态失败，" +
                        "返回值：" +
                        stopState);
                }

                if (DateTime.UtcNow >= releaseDeadline)
                {
                    throw new TimeoutException(
                        GetAxisName(axis) +
                        " 轴退出负限位超时。");
                }
            }
        }

        private void RunMove(
            int requestId,
            int axis,
            float value,
            bool absolute,
            Action<int, string> progress,
            Action<int, bool, int, string> result)
        {
            try
            {
                ThrowIfStopRequested();

                float rawZeroPosition;
                float minimum;
                float maximum;

                if (!TryGetUsableZero(
                        axis,
                        out rawZeroPosition) ||
                    !settingsStore.TryGetLimits(
                        ControllerNumber,
                        axis,
                        out minimum,
                        out maximum))
                {
                    throw new InvalidOperationException(
                        "软件零点或软件限位配置已失效。");
                }

                if (!IsFinite(rawZeroPosition) ||
                    !IsFinite(minimum) ||
                    !IsFinite(maximum) ||
                    minimum < 0 ||
                    minimum >= maximum)
                {
                    throw new InvalidOperationException(
                        "软件零点或软件限位包含无效数值。");
                }

                AxisMotionState current =
                    ReadAxisMotionState(axis);

                float currentSoftwarePosition =
                    rawZeroPosition -
                    current.RawPosition;

                EnsureSoftwarePositionWithinLimits(
                    currentSoftwarePosition,
                    minimum,
                    maximum,
                    "当前位置");

                float targetSoftwarePosition =
                    absolute
                        ? value
                        : currentSoftwarePosition +
                            value;

                const float tolerance = 0.0001f;

                if (!IsFinite(targetSoftwarePosition) ||
                    targetSoftwarePosition <
                        minimum - tolerance ||
                    targetSoftwarePosition >
                        maximum + tolerance)
                {
                    throw new InvalidOperationException(
                        "目标位置 " +
                        targetSoftwarePosition
                            .ToString("F3") +
                        " 超出软件限位 [" +
                        minimum.ToString("F3") +
                        ", " +
                        maximum.ToString("F3") +
                        "]。");
                }

                int stopState =
                    FmcNative
                        .FMC4030_Check_Axis_Is_Stop(
                            deviceId,
                            axis);

                if (stopState != 1)
                {
                    throw new InvalidOperationException(
                        GetAxisName(axis) +
                        " 轴当前没有停止，不能开始移动。返回值：" +
                        stopState);
                }

                float rawTargetPosition =
                    rawZeroPosition -
                    targetSoftwarePosition;

                float rawDirection =
                    rawTargetPosition -
                    current.RawPosition;

                if (!IsFinite(rawTargetPosition) ||
                    !IsFinite(rawDirection))
                {
                    throw new InvalidOperationException(
                        "计算得到的控制器移动距离无效，命令已拒绝。");
                }

                if (Math.Abs(rawDirection) <=
                    tolerance)
                {
                    result(
                        requestId,
                        true,
                        0,
                        GetAxisName(axis) +
                        " 轴已在目标位置。");
                    return;
                }

                ThrowIfPhysicalLimitBlocksMove(
                    current.Status,
                    rawDirection);

                progress(
                    ControllerNumber,
                    GetAxisName(axis) +
                    " 轴正在移动到 " +
                    targetSoftwarePosition
                        .ToString("F3"));

                DateTime deadline =
                    DateTime.UtcNow.AddSeconds(
                        MoveTimeoutSeconds);

                MoveToTargetWithApproachProfile(
                    axis,
                    rawZeroPosition,
                    current.RawPosition,
                    rawDirection,
                    targetSoftwarePosition,
                    minimum,
                    maximum,
                    deadline);

                AxisMotionState state =
                    ReadAxisMotionState(axis);

                float finalSoftwarePosition =
                    rawZeroPosition -
                    state.RawPosition;

                EnsureSoftwarePositionWithinLimits(
                    finalSoftwarePosition,
                    minimum,
                    maximum,
                    "停止后位置");

                const float positionTolerance =
                    0.05f;

                if (Math.Abs(
                    finalSoftwarePosition -
                    targetSoftwarePosition) >
                    positionTolerance)
                {
                    throw new InvalidOperationException(
                        "轴提前停止，目标位置 " +
                        targetSoftwarePosition
                            .ToString("F3") +
                        "，实际位置 " +
                        finalSoftwarePosition
                            .ToString("F3") +
                        "。");
                }

                result(
                    requestId,
                    true,
                    0,
                    GetAxisName(axis) +
                    " 轴移动完成，当前位置 " +
                    finalSoftwarePosition
                        .ToString("F3") +
                    "。");
                return;
            }
            catch (OperationCanceledException exception)
            {
                string message =
                    AppendAxisStopVerificationWarning(
                        axis,
                        exception.Message);

                result(
                    requestId,
                    false,
                    3,
                    message);
            }
            catch (Exception exception)
            {
                string message =
                    AppendAxisStopVerificationWarning(
                        axis,
                        exception.Message);

                result(
                    requestId,
                    false,
                    2,
                    message);
            }
            finally
            {
                lock (stateLock)
                {
                    activeAxis = -1;
                    commandRunning = false;
                }

                commandFinished.Set();
            }
        }

        private void MoveToTargetWithApproachProfile(
            int axis,
            float rawZeroPosition,
            float currentRawPosition,
            float rawDirection,
            float targetSoftwarePosition,
            float minimum,
            float maximum,
            DateTime deadline)
        {
            float softwareRange =
                maximum - minimum;
            float slowdownDistance =
                Math.Min(
                    SlowdownDistance,
                    softwareRange / 2.0f);

            bool approachingMinimum =
                rawDirection > 0 &&
                targetSoftwarePosition <=
                    minimum + slowdownDistance;
            bool approachingMaximum =
                rawDirection < 0 &&
                targetSoftwarePosition >=
                    maximum - slowdownDistance;

            if (slowdownDistance <= 0 ||
                (!approachingMinimum &&
                 !approachingMaximum))
            {
                MoveSegmentAndWait(
                    axis,
                    rawDirection,
                    MoveSpeed,
                    rawZeroPosition,
                    minimum,
                    maximum,
                    rawDirection,
                    deadline);
                return;
            }

            float slowdownStartSoftwarePosition =
                approachingMinimum
                    ? minimum + slowdownDistance
                    : maximum - slowdownDistance;
            float slowdownStartRawPosition =
                rawZeroPosition -
                slowdownStartSoftwarePosition;
            float fastDistance =
                slowdownStartRawPosition -
                currentRawPosition;

            if ((rawDirection > 0 && fastDistance > 0) ||
                (rawDirection < 0 && fastDistance < 0))
            {
                MoveSegmentAndWait(
                    axis,
                    fastDistance,
                    MoveSpeed,
                    rawZeroPosition,
                    minimum,
                    maximum,
                    rawDirection,
                    deadline);
            }

            float slowTotalDistance =
                Math.Abs(
                    rawZeroPosition -
                    targetSoftwarePosition -
                    slowdownStartRawPosition);

            if (slowTotalDistance <= 0.0001f)
            {
                return;
            }

            while (true)
            {
                AxisMotionState state =
                    ReadAxisMotionState(axis);
                float remainingRawDistance =
                    rawZeroPosition -
                    targetSoftwarePosition -
                    state.RawPosition;

                if (Math.Abs(remainingRawDistance) <=
                    0.0001f)
                {
                    return;
                }

                if ((rawDirection > 0 &&
                     remainingRawDistance < 0) ||
                    (rawDirection < 0 &&
                     remainingRawDistance > 0))
                {
                    throw new InvalidOperationException(
                        "减速段检测到轴体越过目标位置。");
                }

                float remainingDistance =
                    Math.Abs(remainingRawDistance);
                float speedRatio =
                    Math.Min(
                        1.0f,
                        remainingDistance /
                            slowTotalDistance);
                float segmentSpeed =
                    remainingDistance <=
                        SlowdownSegmentDistance
                        ? SlowMoveSpeed
                        : SlowMoveSpeed +
                            (MoveSpeed - SlowMoveSpeed) *
                            speedRatio;
                float segmentDistance =
                    Math.Min(
                        SlowdownSegmentDistance,
                        remainingDistance);

                if (remainingRawDistance < 0)
                {
                    segmentDistance =
                        -segmentDistance;
                }

                MoveSegmentAndWait(
                    axis,
                    segmentDistance,
                    segmentSpeed,
                    rawZeroPosition,
                    minimum,
                    maximum,
                    rawDirection,
                    deadline);
            }
        }

        private void MoveSegmentAndWait(
            int axis,
            float distance,
            float speed,
            float rawZeroPosition,
            float minimum,
            float maximum,
            float overallDirection,
            DateTime deadline)
        {
            if (Math.Abs(distance) <= 0.0001f)
            {
                return;
            }

            int moveResult =
                FmcNative
                    .FMC4030_Jog_Single_Axis(
                        deviceId,
                        axis,
                        distance,
                        speed,
                        ZeroAcceleration,
                        ZeroDeceleration,
                        RelativeMove);

            if (moveResult != 0)
            {
                throw new InvalidOperationException(
                    "启动轴移动失败，返回值：" +
                    moveResult);
            }

            while (true)
            {
                Thread.Sleep(
                    PollIntervalMilliseconds);
                ThrowIfStopRequested();

                AxisMotionState state =
                    ReadAxisMotionState(axis);
                float liveSoftwarePosition =
                    rawZeroPosition -
                    state.RawPosition;

                EnsureSoftwarePositionWithinLimits(
                    liveSoftwarePosition,
                    minimum,
                    maximum,
                    "运动中位置");

                ThrowIfPhysicalLimitBlocksMove(
                    state.Status,
                    overallDirection);

                int stopState =
                    FmcNative
                        .FMC4030_Check_Axis_Is_Stop(
                            deviceId,
                            axis);

                if (stopState == 1)
                {
                    return;
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
                        "轴移动超时。");
                }
            }
        }

        private AxisMotionState ReadAxisMotionState(
            int axis)
        {
            byte[] machineStatus =
                new byte[1024];

            int readResult =
                FmcNative.FMC4030_Get_Machine_Status(
                    deviceId,
                    machineStatus);

            if (readResult != 0)
            {
                throw new InvalidOperationException(
                    "读取轴状态失败，返回值：" +
                    readResult);
            }

            return new AxisMotionState(
                ValidatePosition(
                    BitConverter.ToSingle(
                        machineStatus,
                        axis * 4)),
                BitConverter.ToUInt32(
                    machineStatus,
                    44 + axis * 4));
        }

        private static void ThrowIfPhysicalLimitBlocksMove(
            uint status,
            float rawDirection)
        {
            const uint negativeLimit = 0x0010;
            const uint positiveLimit = 0x0020;

            if (rawDirection < 0 &&
                (status & negativeLimit) != 0)
            {
                throw new InvalidOperationException(
                    "负限位已触发，禁止继续负向运动。");
            }

            if (rawDirection > 0 &&
                (status & positiveLimit) != 0)
            {
                throw new InvalidOperationException(
                    "正限位已触发，禁止继续正向运动。");
            }
        }

        private static void EnsureSoftwarePositionWithinLimits(
            float softwarePosition,
            float minimum,
            float maximum,
            string description)
        {
            const float limitTolerance = 0.01f;

            if (!IsFinite(softwarePosition))
            {
                throw new InvalidOperationException(
                    description +
                    "不是有限数值，运动已中止。");
            }

            if (softwarePosition <
                    minimum - limitTolerance ||
                softwarePosition >
                    maximum + limitTolerance)
            {
                throw new InvalidOperationException(
                    description +
                    " " +
                    softwarePosition.ToString("F3") +
                    " 超出软件限位 [" +
                    minimum.ToString("F3") +
                    ", " +
                    maximum.ToString("F3") +
                    "]。");
            }
        }

        private string AppendStopVerificationWarning(
            string message)
        {
            string stopError;

            if (RequestStopAndVerify(
                out stopError))
            {
                return message;
            }

            return message +
                "；严重警告：" +
                stopError;
        }

        private string AppendAxisStopVerificationWarning(
            int axis,
            string message)
        {
            string stopError;

            if (TryStopAxisAndVerify(
                axis,
                out stopError))
            {
                return message;
            }

            return message +
                "；严重警告：" +
                stopError;
        }

        private bool TryStopAxisAndVerify(
            int axis,
            out string error)
        {
            if (!IsConnected())
            {
                error =
                    GetAxisName(axis) +
                    " 轴控制器未连接，无法确认停止。" +
                    "请立即检查现场并使用硬件急停。";
                return false;
            }

            string stopCommandError = null;

            try
            {
                int stopResult =
                    FmcNative
                        .FMC4030_Stop_Single_Axis(
                            deviceId,
                            axis,
                            DeceleratedStop);

                if (stopResult != 0)
                {
                    stopCommandError =
                        "停止命令返回 " +
                        stopResult + "。";
                }
            }
            catch (Exception exception)
            {
                stopCommandError =
                    "停止命令异常：" +
                    exception.Message + "。";
            }

            DateTime deadline =
                DateTime.UtcNow.AddMilliseconds(
                    StopVerificationTimeoutMilliseconds);
            string statusError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    int stopState =
                        FmcNative
                            .FMC4030_Check_Axis_Is_Stop(
                                deviceId,
                                axis);

                    if (stopState == 1)
                    {
                        error = null;
                        return true;
                    }

                    if (stopState < 0)
                    {
                        statusError =
                            "停止状态返回 " +
                            stopState + "。";
                    }
                }
                catch (Exception exception)
                {
                    statusError =
                        "停止确认异常：" +
                        exception.Message + "。";
                }

                Thread.Sleep(
                    PollIntervalMilliseconds);
            }

            error =
                GetAxisName(axis) +
                " 轴在 3 秒内未确认停止。" +
                (string.IsNullOrWhiteSpace(
                    stopCommandError)
                    ? string.Empty
                    : " " + stopCommandError) +
                (string.IsNullOrWhiteSpace(
                    statusError)
                    ? string.Empty
                    : " " + statusError) +
                " 请立即检查现场并使用硬件急停。";
            return false;
        }

        private bool IsSupportedAxis(
            int axis)
        {
            foreach (int configuredAxis in axes)
            {
                if (configuredAxis == axis)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetUsableZero(
            int axis,
            out float rawZeroPosition)
        {
            lock (stateLock)
            {
                if (axis < 0 ||
                    axis >= zeroReferenceValid.Length ||
                    !zeroReferenceValid[axis])
                {
                    rawZeroPosition = 0;
                    return false;
                }
            }

            return settingsStore.TryGetZero(
                ControllerNumber,
                axis,
                out rawZeroPosition);
        }

        private void SetZeroReferenceValid(
            int axis,
            bool valid)
        {
            lock (stateLock)
            {
                if (axis >= 0 &&
                    axis < zeroReferenceValid.Length)
                {
                    zeroReferenceValid[axis] = valid;
                }
            }
        }

        private static float ValidatePosition(
            float position)
        {
            if (!IsFinite(position))
            {
                throw new InvalidOperationException(
                    "控制器返回了无效的位置数据。");
            }

            return position;
        }

        private static bool IsFinite(
            float value)
        {
            return !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private void ThrowIfStopRequested()
        {
            lock (stateLock)
            {
                if (stopRequested)
                {
                    throw new OperationCanceledException(
                        "运动已取消。");
                }
            }
        }

        private bool IsConnected()
        {
            lock (stateLock)
            {
                return connected;
            }
        }

        private void HandleReadFailure()
        {
            bool disconnect = false;

            lock (stateLock)
            {
                consecutiveReadFailures++;

                if (consecutiveReadFailures >= 5 &&
                    !commandRunning)
                {
                    connected = false;
                    disconnect = true;
                    nextConnectAttemptUtc =
                        DateTime.UtcNow.AddSeconds(2);
                }
            }

            if (disconnect)
            {
                InvalidateZeroReferences();

                try
                {
                    FmcNative.FMC4030_Close_Device(
                        deviceId);
                }
                catch
                {
                    // 后续轮询会尝试重新连接。
                }
            }
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
    }

    internal sealed class AxisMotionState
    {
        internal AxisMotionState(
            float rawPosition,
            uint status)
        {
            RawPosition = rawPosition;
            Status = status;
        }

        internal float RawPosition
        {
            get;
            private set;
        }

        internal uint Status
        {
            get;
            private set;
        }
    }

    internal sealed class ControllerSnapshot
    {
        internal ControllerSnapshot(
            int controllerNumber,
            bool connected,
            string errorMessage,
            AxisSnapshot[] axes)
        {
            ControllerNumber = controllerNumber;
            Connected = connected;
            ErrorMessage = errorMessage;
            Axes = axes;
        }

        internal int ControllerNumber { get; private set; }

        internal bool Connected { get; private set; }

        internal string ErrorMessage { get; private set; }

        internal AxisSnapshot[] Axes { get; private set; }

        internal static ControllerSnapshot Disconnected(
            int controllerNumber,
            int[] axes,
            string errorMessage)
        {
            AxisSnapshot[] snapshots =
                new AxisSnapshot[axes.Length];

            for (int index = 0;
                index < axes.Length;
                index++)
            {
                snapshots[index] =
                    new AxisSnapshot(
                        axes[index],
                        0,
                        0,
                        0,
                        0,
                        false,
                        false,
                        0,
                        0);
            }

            return new ControllerSnapshot(
                controllerNumber,
                false,
                errorMessage,
                snapshots);
        }
    }

    internal sealed class AxisSnapshot
    {
        internal AxisSnapshot(
            int axis,
            float position,
            float softwarePosition,
            float speed,
            uint status,
            bool hasSoftwareZero,
            bool hasSoftwareLimits,
            float minimum,
            float maximum)
        {
            Axis = axis;
            Position = position;
            SoftwarePosition = softwarePosition;
            Speed = speed;
            Status = status;
            HasSoftwareZero = hasSoftwareZero;
            HasSoftwareLimits = hasSoftwareLimits;
            Minimum = minimum;
            Maximum = maximum;
        }

        internal int Axis { get; private set; }

        internal float Position { get; private set; }

        internal float SoftwarePosition
        {
            get;
            private set;
        }

        internal float Speed { get; private set; }

        internal uint Status { get; private set; }

        internal bool HasSoftwareZero
        {
            get;
            private set;
        }

        internal bool HasSoftwareLimits
        {
            get;
            private set;
        }

        internal float Minimum { get; private set; }

        internal float Maximum { get; private set; }
    }
}
