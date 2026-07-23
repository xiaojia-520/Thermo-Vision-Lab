using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace MotionHost
{
    internal class Program
    {
        private const int DeviceId = 0;
        private const string ControllerIp = "192.168.1.31";
        private const int ControllerPort = 8088;

        private const int Axis = 0;
        private const float ZeroSpeed = 4.0f;
        private const float ZeroAcceleration = 1.0f;
        private const float ZeroDeceleration = 1.0f;
        private const float ReleaseDistance = 30.0f;
        private const float MaximumSeekDistance = 50.0f;
        private const int MoveTimeoutSeconds = 120;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool automatic =
                HasArgument(args, "--software-zero");

            Console.WriteLine("FMC4030 X 轴软件零点");
            Console.WriteLine("控制器 IP：" + ControllerIp);
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
                    ControllerIp,
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

            FmcSoftwareZero softwareZero =
                new FmcSoftwareZero();

            ConsoleCancelEventHandler cancelHandler =
                delegate(
                    object sender,
                    ConsoleCancelEventArgs eventArgs)
                {
                    eventArgs.Cancel = true;
                    softwareZero.RequestStop(
                        DeviceId,
                        Axis);
                };

            Console.CancelKeyPress += cancelHandler;

            StartParentWatcher(
                args,
                softwareZero);

            int exitCode = 0;

            try
            {
                bool confirmed = automatic;

                if (!automatic)
                {
                    Console.WriteLine(
                        "警告：执行后 X 轴会真实运动。");
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
                    softwareZero.EstablishAtPositiveLimit(
                        DeviceId,
                        Axis,
                        ZeroSpeed,
                        ZeroAcceleration,
                        ZeroDeceleration,
                        ReleaseDistance,
                        MaximumSeekDistance,
                        TimeSpan.FromSeconds(
                            MoveTimeoutSeconds));
                }
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

        private static void StartParentWatcher(
            string[] args,
            FmcSoftwareZero softwareZero)
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

                        softwareZero.RequestStop(
                            DeviceId,
                            Axis);
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
    }
}
