using System;
using System.Threading;

namespace ThermoVision.MotionHost
{
    internal class Program
    {
        private const int DeviceId = 0;
        private const string ControllerIp = "192.168.1.31";
        private const int ControllerPort = 8088;

        // FMC4030 轴号：
        // 0 = X
        // 1 = Y
        // 2 = Z
        private const int Axis = 0;

        private static void Main(string[] args)
        {
            Console.WriteLine("FMC4030 连接与轴状态测试");
            Console.WriteLine("控制器 IP：" + ControllerIp);
            Console.WriteLine("端口：" + ControllerPort);
            Console.WriteLine("轴号：" + Axis);
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
                Console.WriteLine("请检查 IP、端口、网线、电源和 DLL 位数。");
                Console.ReadKey();
                return;
            }

            try
            {
                byte[] machineStatus = new byte[1024];

                for (int i = 0; i < 10; i++)
                {
                    int statusResult =
                        FmcNative.FMC4030_Get_Machine_Status(
                            DeviceId,
                            machineStatus);

                    Console.WriteLine(
                        "Get_Machine_Status 返回值：" + statusResult);

                    if (statusResult != 0)
                    {
                        Console.WriteLine("读取机器状态失败。");
                        break;
                    }

                    // realPos[0]、realPos[1]、realPos[2]
                    // 位于缓冲区开头
                    float position =
                        BitConverter.ToSingle(
                            machineStatus,
                            Axis * 4);

                    // realSpeed 数组紧跟在 realPos 后面
                    float speed =
                        BitConverter.ToSingle(
                            machineStatus,
                            12 + Axis * 4);

                    // machine_status 中 axisStatus 的起始偏移
                    uint axisStatus =
                        BitConverter.ToUInt32(
                            machineStatus,
                            44 + Axis * 4);

                    int stopResult =
                        FmcNative.FMC4030_Check_Axis_Is_Stop(
                            DeviceId,
                            Axis);

                    Console.WriteLine(
                        "位置：" + position.ToString("F3"));

                    Console.WriteLine(
                        "速度：" + speed.ToString("F3"));

                    Console.WriteLine(
                        "轴状态：0x" + axisStatus.ToString("X8"));

                    Console.WriteLine(
                        "是否停止：" + stopResult);

                    Console.WriteLine();

                    Thread.Sleep(500);
                }
            }
            finally
            {
                int closeResult =
                    FmcNative.FMC4030_Close_Device(
                        DeviceId);

                Console.WriteLine(
                    "FMC4030_Close_Device 返回值：" + closeResult);
            }

            Console.WriteLine();
            Console.WriteLine("测试结束，按任意键退出。");
            Console.ReadKey();
        }
    }
}