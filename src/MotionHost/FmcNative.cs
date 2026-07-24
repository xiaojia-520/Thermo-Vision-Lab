using System.Runtime.InteropServices;

namespace MotionHost
{
    internal static class FmcNative
    {
        private static readonly object CallLock =
            new object();

        [DllImport(
            "FMC4030-Dll.dll",
            EntryPoint = "FMC4030_Open_Device",
            CallingConvention = CallingConvention.StdCall,
            CharSet = CharSet.Ansi)]
        private static extern int OpenDeviceNative(
            int id,
            string ip,
            int port);

        [DllImport(
            "FMC4030-Dll.dll",
            EntryPoint = "FMC4030_Close_Device",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int CloseDeviceNative(
            int id);

        [DllImport(
            "FMC4030-Dll.dll",
            EntryPoint = "FMC4030_Get_Machine_Status",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int GetMachineStatusNative(
            int id,
            [Out] byte[] machineData);

        [DllImport(
            "FMC4030-Dll.dll",
            EntryPoint = "FMC4030_Check_Axis_Is_Stop",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int CheckAxisIsStopNative(
            int id,
            int axis);

        [DllImport(
            "FMC4030-Dll.dll",
            EntryPoint = "FMC4030_Jog_Single_Axis",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int JogSingleAxisNative(
            int id,
            int axis,
            float position,
            float speed,
            float acceleration,
            float deceleration,
            int mode);

        [DllImport(
            "FMC4030-Dll.dll",
            EntryPoint = "FMC4030_Stop_Single_Axis",
            CallingConvention = CallingConvention.StdCall)]
        private static extern int StopSingleAxisNative(
            int id,
            int axis,
            int mode);

        internal static int FMC4030_Open_Device(
            int id,
            string ip,
            int port)
        {
            lock (CallLock)
            {
                return OpenDeviceNative(
                    id,
                    ip,
                    port);
            }
        }

        internal static int FMC4030_Close_Device(
            int id)
        {
            lock (CallLock)
            {
                return CloseDeviceNative(id);
            }
        }

        internal static int FMC4030_Get_Machine_Status(
            int id,
            byte[] machineData)
        {
            lock (CallLock)
            {
                return GetMachineStatusNative(
                    id,
                    machineData);
            }
        }

        internal static int FMC4030_Check_Axis_Is_Stop(
            int id,
            int axis)
        {
            lock (CallLock)
            {
                return CheckAxisIsStopNative(
                    id,
                    axis);
            }
        }

        internal static int FMC4030_Jog_Single_Axis(
            int id,
            int axis,
            float position,
            float speed,
            float acceleration,
            float deceleration,
            int mode)
        {
            lock (CallLock)
            {
                return JogSingleAxisNative(
                    id,
                    axis,
                    position,
                    speed,
                    acceleration,
                    deceleration,
                    mode);
            }
        }

        internal static int FMC4030_Stop_Single_Axis(
            int id,
            int axis,
            int mode)
        {
            lock (CallLock)
            {
                return StopSingleAxisNative(
                    id,
                    axis,
                    mode);
            }
        }
    }
}
