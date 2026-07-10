using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace FMC4030_Demo_CSharp
{
    class FMC4030
    {
        /* DLL文件路径，此路径需要填写绝对路径，避免出现无法找到dll文件错误 */
        public static String dll_path = "E:\\资料\\售后常用资料\\控制器\\FMC4030\\FMC4030-2024-03-08\\SDK Demo\\FMC4030_Demo_CSharp(c#)\\FMC4030_Demo_CSharp\\FMC4030_Demo_CSharp/FMC4030-Dll.dll";

        /* 打开设备 */
        [DllImport("E:\\资料\\售后常用资料\\控制器\\FMC4030\\FMC4030-2024-03-08\\SDK Demo\\FMC4030_Demo_CSharp(c#)\\FMC4030_Demo_CSharp\\FMC4030_Demo_CSharp/FMC4030-Dll.dll")]
        public static extern int FMC4030_Open_Device(int id, string ip, int port);

        [DllImport("E:\\资料\\售后常用资料\\控制器\\FMC4030\\FMC4030-2024-03-08\\SDK Demo\\FMC4030_Demo_CSharp(c#)\\FMC4030_Demo_CSharp\\FMC4030_Demo_CSharp/FMC4030-Dll.dll")]
        public static extern int FMC4030_Close_Device(int id);

        [DllImport("E:\\资料\\售后常用资料\\控制器\\FMC4030\\FMC4030-2024-03-08\\SDK Demo\\FMC4030_Demo_CSharp(c#)\\FMC4030_Demo_CSharp\\FMC4030_Demo_CSharp/FMC4030-Dll.dll")]
        public static extern int FMC4030_Jog_Single_Axis(int id, int axis, float pos, float speed, float acc, float dec, int mode);

        [DllImport("E:\\资料\\售后常用资料\\控制器\\FMC4030\\FMC4030-2024-03-08\\SDK Demo\\FMC4030_Demo_CSharp(c#)\\FMC4030_Demo_CSharp\\FMC4030_Demo_CSharp/FMC4030-Dll.dll")]
        public static extern int FMC4030_Check_Axis_Is_Stop(int id, int axis);

        [DllImport("E:\\资料\\售后常用资料\\控制器\\FMC4030\\FMC4030-2024-03-08\\SDK Demo\\FMC4030_Demo_CSharp(c#)\\FMC4030_Demo_CSharp\\FMC4030_Demo_CSharp/FMC4030-Dll.dll")]
        public static extern int FMC4030_Get_Machine_Status(int id, byte[] machineData);
    }
}
