using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ThermoVision
{
    internal sealed class MotionHostClient :
        IDisposable
    {
        private readonly object processLock =
            new object();
        private readonly object writerLock =
            new object();
        private readonly object pendingLock =
            new object();
        private readonly Dictionary<
            int,
            TaskCompletionSource<MotionHostResult>>
            pendingRequests =
                new Dictionary<
                    int,
                    TaskCompletionSource<MotionHostResult>>();

        private Process activeProcess;
        private NamedPipeClientStream pipe;
        private StreamReader reader;
        private StreamWriter writer;
        private Task startTask;
        private TaskCompletionSource<bool> readySource;
        private int nextRequestId;
        private bool disposed;
        private volatile bool connected;

        internal event EventHandler<
            MotionControllerStatusEventArgs>
            StatusReceived;

        internal event EventHandler<
            MotionProgressEventArgs>
            ProgressReceived;

        internal event EventHandler ConnectionLost;

        internal Task StartAsync()
        {
            lock (processLock)
            {
                ThrowIfDisposed();

                if (startTask == null ||
                    startTask.IsFaulted ||
                    startTask.IsCanceled ||
                    startTask.IsCompleted &&
                    !connected)
                {
                    ResetConnectionForRestart();
                    startTask = StartCoreAsync();
                }

                return startTask;
            }
        }

        internal async Task<MotionHostResult>
            RunSoftwareZeroAsync(
                int controllerNumber)
        {
            ValidateControllerNumber(
                controllerNumber);

            return await SendRequestAsync(
                "HOME",
                controllerNumber,
                TimeSpan.FromMinutes(10),
                true);
        }

        internal async Task<MotionHostResult>
            RunRangeCalibrationAsync(
                int controllerNumber)
        {
            ValidateControllerNumber(
                controllerNumber);

            return await SendRequestAsync(
                "CALIBRATE_RANGE",
                controllerNumber,
                TimeSpan.FromMinutes(65),
                true);
        }

        internal async Task<MotionHostResult>
            StopAsync(
                int controllerNumber)
        {
            ValidateControllerNumber(
                controllerNumber);

            return await SendRequestAsync(
                "STOP",
                controllerNumber,
                TimeSpan.FromSeconds(10),
                false);
        }

        internal async Task<MotionHostResult>
            StopAllAsync()
        {
            return await SendRequestAsync(
                "STOP_ALL",
                0,
                TimeSpan.FromSeconds(10),
                false);
        }

        internal async Task<MotionHostResult>
            MoveRelativeAsync(
                int controllerNumber,
                int axis,
                float distance)
        {
            ValidateControllerNumber(
                controllerNumber);

            return await SendRequestAsync(
                "MOVE_REL",
                controllerNumber,
                TimeSpan.FromSeconds(150),
                true,
                axis.ToString(
                    CultureInfo.InvariantCulture),
                distance.ToString(
                    "R",
                    CultureInfo.InvariantCulture));
        }

        internal async Task<MotionHostResult>
            MoveAbsoluteAsync(
                int controllerNumber,
                int axis,
                float targetPosition)
        {
            ValidateControllerNumber(
                controllerNumber);

            return await SendRequestAsync(
                "MOVE_ABS",
                controllerNumber,
                TimeSpan.FromSeconds(150),
                true,
                axis.ToString(
                    CultureInfo.InvariantCulture),
                targetPosition.ToString(
                    "R",
                    CultureInfo.InvariantCulture));
        }

        internal async Task<MotionHostResult>
            SetSoftwareLimitsAsync(
                int controllerNumber,
                int axis,
                float minimum,
                float maximum)
        {
            ValidateControllerNumber(
                controllerNumber);

            return await SendRequestAsync(
                "SET_LIMIT",
                controllerNumber,
                TimeSpan.FromSeconds(10),
                false,
                axis.ToString(
                    CultureInfo.InvariantCulture),
                minimum.ToString(
                    "R",
                    CultureInfo.InvariantCulture),
                maximum.ToString(
                    "R",
                    CultureInfo.InvariantCulture));
        }

        public void Dispose()
        {
            lock (processLock)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
            }

            try
            {
                SendLine("SHUTDOWN");
            }
            catch
            {
                // 连接可能已经断开。
            }

            NamedPipeClientStream currentPipe = pipe;

            if (currentPipe != null)
            {
                currentPipe.Dispose();
            }

            Process process = activeProcess;

            if (process != null)
            {
                process.Dispose();
            }

            CompletePendingRequests(
                "运动控制服务已关闭。");
        }

        private async Task StartCoreAsync()
        {
            string executablePath =
                Path.Combine(
                    Path.GetDirectoryName(
                        typeof(MotionHostClient)
                            .Assembly.Location),
                    "MotionHost",
                    "MotionHost.exe");

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "找不到 x86 运动控制程序，请重新生成解决方案。",
                    executablePath);
            }

            string pipeName =
                "ThermoVision.MotionHost." +
                Process.GetCurrentProcess().Id +
                "." +
                Guid.NewGuid().ToString("N");

            Process process = new Process();
            process.StartInfo =
                new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments =
                        "--server --pipe " +
                        pipeName +
                        " --parent-pid " +
                        Process.GetCurrentProcess().Id,
                    WorkingDirectory =
                        Path.GetDirectoryName(
                            executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

            NamedPipeClientStream clientPipe = null;
            bool processStarted = false;

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "运动控制服务启动失败。");
                }

                processStarted = true;
                activeProcess = process;

                clientPipe =
                    new NamedPipeClientStream(
                        ".",
                        pipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous);

                await ConnectToMotionHostAsync(
                    clientPipe,
                    process);

                pipe = clientPipe;
                reader =
                    new StreamReader(
                        clientPipe,
                        Encoding.UTF8);
                writer =
                    new StreamWriter(
                        clientPipe,
                        new UTF8Encoding(false));
                writer.AutoFlush = true;

                readySource =
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);

                Task readLoop = ReadLoopAsync();

                Task completed =
                    await Task.WhenAny(
                        readySource.Task,
                        Task.Delay(5000));

                if (!ReferenceEquals(
                    completed,
                    readySource.Task))
                {
                    throw new TimeoutException(
                        "运动控制服务启动超时。");
                }

                await readySource.Task;

                if (readLoop.IsCompleted)
                {
                    throw new IOException(
                        "运动控制服务在启动后立即断开。");
                }
            }
            catch
            {
                if (clientPipe != null)
                {
                    clientPipe.Dispose();
                }

                if (processStarted &&
                    !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }

                process.Dispose();
                activeProcess = null;
                throw;
            }
        }

        private static async Task ConnectToMotionHostAsync(
            NamedPipeClientStream clientPipe,
            Process process)
        {
            DateTime deadline =
                DateTime.UtcNow.AddSeconds(8);

            while (!clientPipe.IsConnected)
            {
                if (process.HasExited)
                {
                    throw CreateStartupException(
                        process.ExitCode);
                }

                try
                {
                    await Task.Run(
                        delegate
                        {
                            clientPipe.Connect(250);
                        });
                }
                catch (TimeoutException)
                {
                    if (process.HasExited)
                    {
                        throw CreateStartupException(
                            process.ExitCode);
                    }

                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException(
                            "运动控制服务连接超时。请检查是否有残留的 " +
                            "MotionHost.exe 进程。");
                    }
                }
            }
        }

        private static Exception CreateStartupException(
            int exitCode)
        {
            if (exitCode == 7)
            {
                return new InvalidOperationException(
                    "已有 MotionHost 正在占用电机控制器。" +
                    "如果主程序已经关闭，请结束残留的 " +
                    "MotionHost.exe 后重试。");
            }

            return new InvalidOperationException(
                "运动控制服务启动失败，退出码：" +
                exitCode +
                "。");
        }

        private async Task<MotionHostResult>
            SendRequestAsync(
                string command,
                int controllerNumber,
                TimeSpan timeout,
                bool stopOnTimeout,
                params string[] arguments)
        {
            await StartAsync();

            int requestId =
                Interlocked.Increment(
                    ref nextRequestId);

            TaskCompletionSource<MotionHostResult>
                completionSource =
                    new TaskCompletionSource<
                        MotionHostResult>(
                            TaskCreationOptions
                                .RunContinuationsAsynchronously);

            lock (pendingLock)
            {
                pendingRequests.Add(
                    requestId,
                    completionSource);
            }

            try
            {
                StringBuilder line =
                    new StringBuilder();

                line.Append(command);
                line.Append('|');
                line.Append(requestId);
                line.Append('|');
                line.Append(controllerNumber);

                foreach (string argument
                    in arguments)
                {
                    line.Append('|');
                    line.Append(argument);
                }

                SendLine(line.ToString());
            }
            catch
            {
                lock (pendingLock)
                {
                    pendingRequests.Remove(
                        requestId);
                }

                throw;
            }

            Task completed =
                await Task.WhenAny(
                    completionSource.Task,
                    Task.Delay(timeout));

            if (!ReferenceEquals(
                completed,
                completionSource.Task))
            {
                lock (pendingLock)
                {
                    pendingRequests.Remove(
                        requestId);
                }

                string stopDetails = string.Empty;

                if (stopOnTimeout)
                {
                    try
                    {
                        MotionHostResult stopResult =
                            await SendRequestAsync(
                                "STOP",
                                controllerNumber,
                                TimeSpan.FromSeconds(10),
                                false);

                        stopDetails =
                            stopResult.Success
                                ? " 已补发停止命令并确认轴体停止。"
                                : " 补发停止命令失败：" +
                                    stopResult.Output;
                    }
                    catch (Exception exception)
                    {
                        stopDetails =
                            " 补发停止命令时发生异常：" +
                            exception.Message;
                    }
                }

                throw new TimeoutException(
                    "运动控制命令等待结果超时。" +
                    stopDetails);
            }

            return await completionSource.Task;
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!disposed)
                {
                    string line =
                        await reader.ReadLineAsync();

                    if (line == null)
                    {
                        break;
                    }

                    HandleMessage(line);
                }
            }
            catch (ObjectDisposedException)
            {
                // 正常关闭。
            }
            catch (IOException)
            {
                // 由统一断线处理更新界面。
            }
            finally
            {
                bool wasConnected = connected;
                connected = false;

                if (!disposed)
                {
                    CompletePendingRequests(
                        "运动控制服务连接已断开。");

                    if (wasConnected)
                    {
                        EventHandler handler =
                            ConnectionLost;

                        if (handler != null)
                        {
                            handler(
                                this,
                                EventArgs.Empty);
                        }
                    }
                }
            }
        }

        private void HandleMessage(
            string line)
        {
            string[] parts = line.Split('|');

            if (parts.Length == 0)
            {
                return;
            }

            if (parts[0] == "READY")
            {
                connected = true;
                readySource.TrySetResult(true);
                return;
            }

            if (parts[0] == "RESULT" &&
                parts.Length >= 5)
            {
                HandleResult(parts);
                return;
            }

            if (parts[0] == "STATUS" &&
                parts.Length >= 5)
            {
                HandleStatus(parts);
                return;
            }

            if (parts[0] == "PROGRESS" &&
                parts.Length >= 3)
            {
                int controllerNumber;

                if (int.TryParse(
                    parts[1],
                    out controllerNumber))
                {
                    EventHandler<
                        MotionProgressEventArgs>
                        handler = ProgressReceived;

                    if (handler != null)
                    {
                        handler(
                            this,
                            new MotionProgressEventArgs(
                                controllerNumber,
                                DecodeText(parts[2])));
                    }
                }
            }
        }

        private void HandleResult(
            string[] parts)
        {
            int requestId;
            int exitCode;

            if (!int.TryParse(
                parts[1],
                out requestId) ||
                !int.TryParse(
                    parts[3],
                    out exitCode))
            {
                return;
            }

            TaskCompletionSource<MotionHostResult>
                completionSource = null;

            lock (pendingLock)
            {
                if (pendingRequests.TryGetValue(
                    requestId,
                    out completionSource))
                {
                    pendingRequests.Remove(
                        requestId);
                }
            }

            if (completionSource != null)
            {
                completionSource.TrySetResult(
                    new MotionHostResult(
                        exitCode,
                        DecodeText(parts[4])));
            }
        }

        private void HandleStatus(
            string[] parts)
        {
            int controllerNumber;
            int axisCount;

            if (!int.TryParse(
                parts[1],
                out controllerNumber) ||
                !int.TryParse(
                    parts[4],
                    out axisCount) ||
                controllerNumber < 1 ||
                controllerNumber > 3 ||
                parts[2] != "0" &&
                    parts[2] != "1")
            {
                return;
            }

            int expectedAxisCount =
                controllerNumber == 3
                    ? 3
                    : 2;

            if (axisCount != expectedAxisCount ||
                parts.Length !=
                    5 + axisCount * 9)
            {
                return;
            }

            bool isConnected =
                parts[2] == "1";

            List<MotionAxisStatus> axes =
                new List<MotionAxisStatus>();
            bool[] axisSeen = new bool[3];

            int offset = 5;

            for (int index = 0;
                index < axisCount;
                index++)
            {
                int axis;
                float rawPosition;
                float softwarePosition;
                float speed;
                uint status;
                float minimum;
                float maximum;

                if (!int.TryParse(
                        parts[offset],
                        out axis) ||
                    axis < 0 ||
                    axis >= expectedAxisCount ||
                    axisSeen[axis] ||
                    !float.TryParse(
                        parts[offset + 1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out rawPosition) ||
                    !float.TryParse(
                        parts[offset + 2],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out softwarePosition) ||
                    !float.TryParse(
                        parts[offset + 3],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out speed) ||
                    !uint.TryParse(
                        parts[offset + 4],
                        out status) ||
                    parts[offset + 5] != "0" &&
                        parts[offset + 5] != "1" ||
                    parts[offset + 6] != "0" &&
                        parts[offset + 6] != "1" ||
                    !float.TryParse(
                        parts[offset + 7],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out minimum) ||
                    !float.TryParse(
                        parts[offset + 8],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out maximum) ||
                    !IsFinite(rawPosition) ||
                    !IsFinite(softwarePosition) ||
                    !IsFinite(speed) ||
                    !IsFinite(minimum) ||
                    !IsFinite(maximum))
                {
                    return;
                }

                bool hasLimits =
                    parts[offset + 6] == "1";

                if (hasLimits &&
                    (minimum < 0 ||
                        minimum >= maximum))
                {
                    return;
                }

                axisSeen[axis] = true;
                axes.Add(
                    new MotionAxisStatus(
                        axis,
                        rawPosition,
                        softwarePosition,
                        speed,
                        status,
                        parts[offset + 5] == "1",
                        hasLimits,
                        minimum,
                        maximum));

                offset += 9;
            }

            EventHandler<
                MotionControllerStatusEventArgs>
                handler = StatusReceived;

            if (handler != null)
            {
                handler(
                    this,
                    new MotionControllerStatusEventArgs(
                        new MotionControllerStatus(
                            controllerNumber,
                            isConnected,
                            DecodeText(parts[3]),
                            axes.ToArray())));
            }
        }

        private void SendLine(
            string line)
        {
            StreamWriter currentWriter = writer;

            if (currentWriter == null ||
                !connected && line != "SHUTDOWN")
            {
                throw new InvalidOperationException(
                    "运动控制服务尚未连接。");
            }

            lock (writerLock)
            {
                currentWriter.WriteLine(line);
            }
        }

        private void CompletePendingRequests(
            string message)
        {
            TaskCompletionSource<MotionHostResult>[]
                requests;

            lock (pendingLock)
            {
                requests =
                    new List<
                        TaskCompletionSource<
                            MotionHostResult>>(
                                pendingRequests.Values)
                        .ToArray();

                pendingRequests.Clear();
            }

            foreach (
                TaskCompletionSource<MotionHostResult>
                    request in requests)
            {
                request.TrySetResult(
                    new MotionHostResult(
                        6,
                        message));
            }
        }

        private void ResetConnectionForRestart()
        {
            connected = false;

            if (pipe != null)
            {
                pipe.Dispose();
                pipe = null;
            }

            reader = null;
            writer = null;

            if (activeProcess != null)
            {
                activeProcess.Dispose();
                activeProcess = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    "MotionHostClient");
            }
        }

        private static void ValidateControllerNumber(
            int controllerNumber)
        {
            if (controllerNumber < 1 ||
                controllerNumber > 3)
            {
                throw new ArgumentOutOfRangeException(
                    "controllerNumber",
                    "控制器编号必须是 1、2 或 3。");
            }
        }

        private static bool IsFinite(
            float value)
        {
            return !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        private static string DecodeText(
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return value;
            }
        }
    }

    internal sealed class MotionHostResult
    {
        internal MotionHostResult(
            int exitCode,
            string output)
        {
            ExitCode = exitCode;
            Output = output;
        }

        internal int ExitCode { get; private set; }

        internal string Output { get; private set; }

        internal bool Success
        {
            get { return ExitCode == 0; }
        }
    }

    internal sealed class MotionControllerStatus
    {
        internal MotionControllerStatus(
            int controllerNumber,
            bool connected,
            string errorMessage,
            MotionAxisStatus[] axes)
        {
            ControllerNumber = controllerNumber;
            Connected = connected;
            ErrorMessage = errorMessage;
            Axes = axes;
            ReceivedAt = DateTime.Now;
            ReceivedAtTimestamp =
                Stopwatch.GetTimestamp();
        }

        internal int ControllerNumber { get; private set; }

        internal bool Connected { get; private set; }

        internal string ErrorMessage { get; private set; }

        internal MotionAxisStatus[] Axes { get; private set; }

        internal DateTime ReceivedAt { get; private set; }

        internal long ReceivedAtTimestamp
        {
            get;
            private set;
        }
    }

    internal sealed class MotionAxisStatus
    {
        private const uint Running = 0x0001;
        private const uint Paused = 0x0002;
        private const uint Stopped = 0x0008;
        private const uint NegativeLimit = 0x0010;
        private const uint PositiveLimit = 0x0020;
        private const uint HomeDone = 0x0040;
        private const uint Homing = 0x0080;
        private const uint HomeOvertime = 0x1000;

        internal MotionAxisStatus(
            int axis,
            float rawPosition,
            float softwarePosition,
            float speed,
            uint status,
            bool hasSoftwareZero,
            bool hasSoftwareLimits,
            float minimum,
            float maximum)
        {
            Axis = axis;
            RawPosition = rawPosition;
            SoftwarePosition = softwarePosition;
            Speed = speed;
            Status = status;
            HasSoftwareZero = hasSoftwareZero;
            HasSoftwareLimits = hasSoftwareLimits;
            Minimum = minimum;
            Maximum = maximum;
        }

        internal int Axis { get; private set; }

        internal float RawPosition { get; private set; }

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

        internal bool IsRunning
        {
            get { return (Status & Running) != 0; }
        }

        internal bool IsPaused
        {
            get { return (Status & Paused) != 0; }
        }

        internal bool IsStopped
        {
            get { return (Status & Stopped) != 0; }
        }

        internal bool IsNegativeLimitActive
        {
            get
            {
                return (Status & NegativeLimit) != 0;
            }
        }

        internal bool IsPositiveLimitActive
        {
            get
            {
                return (Status & PositiveLimit) != 0;
            }
        }

        internal bool IsHomeDone
        {
            get { return (Status & HomeDone) != 0; }
        }

        internal bool IsHoming
        {
            get { return (Status & Homing) != 0; }
        }

        internal bool IsHomeOvertime
        {
            get
            {
                return (Status & HomeOvertime) != 0;
            }
        }
    }

    internal sealed class MotionControllerStatusEventArgs :
        EventArgs
    {
        internal MotionControllerStatusEventArgs(
            MotionControllerStatus status)
        {
            Status = status;
        }

        internal MotionControllerStatus Status
        {
            get;
            private set;
        }
    }

    internal sealed class MotionProgressEventArgs :
        EventArgs
    {
        internal MotionProgressEventArgs(
            int controllerNumber,
            string message)
        {
            ControllerNumber = controllerNumber;
            Message = message;
        }

        internal int ControllerNumber { get; private set; }

        internal string Message { get; private set; }
    }
}
