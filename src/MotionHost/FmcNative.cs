using System.Runtime.InteropServices;

namespace MotionHost
{
    internal static class FmcNative
    {
        [DllImport(
            "FMC4030-Dll.dll",
            CallingConvention = CallingConvention.StdCall,
            CharSet = CharSet.Ansi)]
        internal static extern int FMC4030_Open_Device(
            int id,
            string ip,
            int port);

        [DllImport(
            "FMC4030-Dll.dll",
            CallingConvention = CallingConvention.StdCall)]
        internal static extern int FMC4030_Close_Device(
            int id);

        [DllImport(
            "FMC4030-Dll.dll",
            CallingConvention = CallingConvention.StdCall)]
        internal static extern int FMC4030_Get_Machine_Status(
            int id,
            [Out] byte[] machineData);

        [DllImport(
            "FMC4030-Dll.dll",
            CallingConvention = CallingConvention.StdCall)]
        internal static extern int FMC4030_Check_Axis_Is_Stop(
            int id,
            int axis);

        [DllImport(
            "FMC4030-Dll.dll",
            CallingConvention = CallingConvention.StdCall)]
        internal static extern int FMC4030_Jog_Single_Axis(
            int id,
            int axis,
            float position,
            float speed,
            float acceleration,
            float deceleration,
            int mode);

        [DllImport(
            "FMC4030-Dll.dll",
            CallingConvention = CallingConvention.StdCall)]
        internal static extern int FMC4030_Stop_Single_Axis(
            int id,
            int axis,
            int mode);
    }
}
