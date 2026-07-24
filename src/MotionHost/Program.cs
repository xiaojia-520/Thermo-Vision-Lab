using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace MotionHost
{
    internal class Program
    {
        private const int DeviceId = 0;
        private const int ControllerPort = 8088;

        private const float ZeroSpeed = 4.0f;
        private const float ZeroAcceleration = 1.0f;
        private const float ZeroDeceleration = 1.0f;
        private const float ReleaseDistance = 30.0f;
        private const float MaximumSeekDistance = 150.0f;
        private const int MoveTimeoutSeconds = 120;
        private const string MotionHostMutexName =
            "ThermoVision.MotionHost.FMC4030.Singleton";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            using (Mutex instanceMutex =
                new Mutex(
                    false,
                    MotionHostMutexName))
            {
                bool ownsMutex;

                try
                {
                    ownsMutex =
                        instanceMutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    Console.WriteLine(
                        "已有 MotionHost 正在控制 FMC4030，" +
                        "为避免重复连接，本次启动已拒绝。");
                    return 7;
                }

                try
                {
                    return Run(args);
                }
                finally
                {
                    instanceMutex.ReleaseMutex();
                }
            }
        }

        private static int Run(string[] args)
        {
            if (HasArgument(args, "--server"))
            {
                return MotionServer.Run(args);
            }

            bool automatic =
                HasArgument(args, "--software-zero");

            int controllerNumber;

            try
            {
                controllerNumber =
                    ReadControllerNumber(args);
            }
            catch (ArgumentException exception)
            {
                Console.WriteLine(exception.Message);
                PauseWhenInteractive(automatic);
                return 5;
            }

            string controllerIp =
                "192.168.1." +
                (30 + controllerNumber).ToString();

            int[] axes =
                controllerNumber == 3
                    ? new int[] { 0, 1, 2 }
                    : new int[] { 0, 1 };

            string axisSummary =
                controllerNumber == 3
                    ? "X、Y、Z"
                    : "X、Y";

            Console.WriteLine(
                "FMC4030 " + controllerNumber +
                " 号轴回零（" + axisSummary + "）");
            Console.WriteLine("控制器 IP：" + controllerIp);
            Console.WriteLine("端口：" + ControllerPort);
            Console.WriteLine(
                "寻零速度：" + ZeroSpeed +
                " 个控制器单位/秒");
            Console.WriteLine(
                "正限位退出距离：" +
                ReleaseDistance + " 个控制器单位");
            Console.WriteLine();

            int openResult =
                FmcNative.FMC4030_Open_Device(
                    DeviceId,
                    controllerIp,
                    ControllerPort);

            Console.WriteLine(
                "FMC4030_Open_Device 返回值：" + openResult);

            if (openResult != 0)
            {
                Console.WriteLine("控制器连接失败。");
                Console.WriteLine(
                    "请检查 IP、端口、网线、电源和 DLL 位数。");
                PauseWhenInteractive(automatic);
                return 1;
            }

            HomingSession homingSession =
                new HomingSession();

            ConsoleCancelEventHandler cancelHandler =
                delegate(
                    object sender,
                    ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    homingSession.RequestStop();
                };

            Console.CancelKeyPress += cancelHandler;

            StartParentWatcher(
                args,
                homingSession);

            int exitCode = 0;

            try
            {
                bool confirmed = automatic;

                if (!automatic)
                {
                    Console.WriteLine(
                        "警告：执行后 " + controllerNumber +
                        " 号轴的 " + axisSummary +
                        " 方向会依次真实运动。");
                    Console.Write(
                        "确认现场安全后输入 ZERO 并按回车：");

                    confirmed = string.Equals(
                        Console.ReadLine(),
                        "ZERO",
                        StringComparison.Ordinal);
                }

                if (!confirmed)
                {
                    Console.WriteLine(
                        "已取消，没有发送运动命令。");
                    exitCode = 3;
                }
                else
                {
                    foreach (int axis in axes)
                    {
                        FmcSoftwareZero softwareZero =
                            new FmcSoftwareZero();

                        if (!homingSession.TryActivate(
                            softwareZero,
                            axis))
                        {
                            throw new OperationCanceledException(
                                "回零已取消。");
                        }

                        try
                        {
                            Console.WriteLine();
                            Console.WriteLine(
                                "开始回 " +
                                GetAxisName(axis) +
                                " 轴。");

                            softwareZero.EstablishAtPositiveLimit(
                                DeviceId,
                                axis,
                                ZeroSpeed,
                                ZeroAcceleration,
                                ZeroDeceleration,
                                ReleaseDistance,
                                MaximumSeekDistance,
                                TimeSpan.FromSeconds(
                                    MoveTimeoutSeconds));
                        }
                        finally
                        {
                            homingSession.ClearActive(
                                softwareZero);
                        }
                    }
                }
            }
            catch (OperationCanceledException exception)
            {
                Console.WriteLine(
                    "回零流程已停止：" +
                    exception.Message);
                exitCode = 3;
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    "建立软件零点失败：" +
                    exception.Message);
                exitCode = 2;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;

                int closeResult =
                    FmcNative.FMC4030_Close_Device(
                        DeviceId);

                Console.WriteLine(
                    "FMC4030_Close_Device 返回值：" +
                    closeResult);

                if (closeResult != 0 &&
                    exitCode == 0)
                {
                    exitCode = 4;
                }
            }

            if (exitCode == 0)
            {
                Console.WriteLine("软件零点流程执行完成。");
            }

            PauseWhenInteractive(automatic);
            return exitCode;
        }

        private static bool HasArgument(
            string[] args,
            string expected)
        {
            foreach (string argument in args)
            {
                if (string.Equals(
                    argument,
                    expected,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ReadParentProcessId(
            string[] args)
        {
            for (int index = 0;
                index < args.Length - 1;
                index++)
            {
                if (!string.Equals(
                    args[index],
                    "--parent-pid",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int parentProcessId;

                if (int.TryParse(
                    args[index + 1],
                    out parentProcessId))
                {
                    return parentProcessId;
                }
            }

            return 0;
        }

        private static int ReadControllerNumber(
            string[] args)
        {
            for (int index = 0;
                index < args.Length;
                index++)
            {
                if (!string.Equals(
                    args[index],
                    "--controller",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int controllerNumber;

                if (index == args.Length - 1 ||
                    !int.TryParse(
                        args[index + 1],
                        out controllerNumber) ||
                    controllerNumber < 1 ||
                    controllerNumber > 3)
                {
                    throw new ArgumentException(
                        "--controller 必须指定 1、2 或 3。");
                }

                return controllerNumber;
            }

            return 1;
        }

        private static void StartParentWatcher(
            string[] args,
            HomingSession homingSession)
        {
            int parentProcessId =
                ReadParentProcessId(args);

            if (parentProcessId <= 0)
            {
                return;
            }

            Thread watcher = new Thread(
                delegate()
                {
                    try
                    {
                        Process parent =
                            Process.GetProcessById(
                                parentProcessId);

                        parent.WaitForExit();

                        homingSession.RequestStop();
                    }
                    catch
                    {
                        // 辅助监控失败不覆盖运动流程中的原始错误。
                    }
                });

            watcher.IsBackground = true;
            watcher.Name = "ThermoVision parent watcher";
            watcher.Start();
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

        private static void PauseWhenInteractive(
            bool automatic)
        {
            if (automatic)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出。");
            Console.ReadKey();
        }

        private sealed class HomingSession
        {
            private readonly object syncRoot =
                new object();

            private bool cancellationRequested;
            private FmcSoftwareZero activeSoftwareZero;
            private int activeAxis = -1;

            internal bool TryActivate(
                FmcSoftwareZero softwareZero,
                int axis)
            {
                lock (syncRoot)
                {
                    if (cancellationRequested)
                    {
                        return false;
                    }

                    activeSoftwareZero = softwareZero;
                    activeAxis = axis;
                    return true;
                }
            }

            internal void ClearActive(
                FmcSoftwareZero softwareZero)
            {
                lock (syncRoot)
                {
                    if (!ReferenceEquals(
                        activeSoftwareZero,
                        softwareZero))
                    {
                        return;
                    }

                    activeSoftwareZero = null;
                    activeAxis = -1;
                }
            }

            internal void RequestStop()
            {
                FmcSoftwareZero softwareZero;
                int axis;

                lock (syncRoot)
                {
                    cancellationRequested = true;
                    softwareZero = activeSoftwareZero;
                    axis = activeAxis;
                }

                if (softwareZero != null &&
                    axis >= 0)
                {
                    softwareZero.RequestStop(
                        DeviceId,
                        axis);
                }
            }
        }
    }
}
