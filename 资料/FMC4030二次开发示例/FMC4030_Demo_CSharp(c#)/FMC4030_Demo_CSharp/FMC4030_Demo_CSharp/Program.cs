using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;

namespace FMC4030_Demo_CSharp
{
    class Program
    {
        [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct machine_status
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
            public float[] realPos;                          //当前各轴实时位置
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
            public float[] realSpeed;                        //当前各轴实时速度
            public uint inputStatus;                         //输入状态
            public uint outputStatus;                        //输出状态
            public uint limitNStatus;                        //负限位状态
            public uint limitPStatus;                        //正限位状态
            public uint machineRunStatus;                    //设备运行状态
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
            public uint[] axisStatus;                        //轴状态
            public uint homeStatus;                          //回零状态，未使用，通过轴状态来获取回零状态
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 600)]
            public char[] file;                              //控制器内程序文件，至多20个文件
            //char currentRunFile[30];                //当前正在运行的文件
            //int currentRunLine;                     //当前运行程序所处的行号
        };

        static void Main(string[] args)
        {
            machine_status ms = new machine_status();
            ms.realPos = new float[3];
            ms.realSpeed = new float[3];
            ms.axisStatus = new uint[3];
            ms.file = new char[600];
            byte[] arr_status = new byte[1024];
            
           Console.WriteLine(FMC4030.FMC4030_Open_Device(0, "192.168.0.30", 8088));
            
            Console.WriteLine("Jog: " + FMC4030.FMC4030_Jog_Single_Axis(0, 1, 10, 200, 200, 200, 1));

            while (FMC4030.FMC4030_Check_Axis_Is_Stop(0, 1) == 0)
            {
                /* 通过Byte数组获取设备状态 */
                FMC4030.FMC4030_Get_Machine_Status(0, arr_status);

                /* 将Byte数组转为浮点数 */
                float posy = BitConverter.ToSingle(arr_status, 0);

                Console.WriteLine("Axis X Pos: " + posy);
            }

            Console.WriteLine("Run Finish");

            Console.ReadKey();

        }

        public static T ByteToStructure<T>(byte[] dataBuffer)
        {
            object structure = null;
            int size = 659;
            IntPtr allocIntPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(dataBuffer, 0, allocIntPtr, size);
                structure = Marshal.PtrToStructure(allocIntPtr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(allocIntPtr);
            }
            return (T)structure;
        }
    }
}
