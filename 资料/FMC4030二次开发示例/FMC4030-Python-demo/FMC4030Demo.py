# -*- coding: UTF-8 -*-
from ctypes import *
import time

fmc4030 = windll.LoadLibrary('E:\资料\售后常用资料\控制器\FMC4030\FMC4030-2024-03-08\FMC4030Lib-x64-20240229/FMC4030-Dll.dll')


# 定义设备状态类，用于获取设备状态数据
# struct machine_status{
# 	float realPos[3];
# 	float realSpeed[3];
# 	unsigned int inputStatus;
# 	unsigned int outputStatus;
# 	unsigned int limitNStatus;
# 	unsigned int limitPStatus;
# 	unsigned int machineRunStatus;
# 	unsigned int axisStatus[MAX_AXIS];
# 	unsigned int homeStatus;
# 	char file[20][30];
# };
class machine_status(Structure):
    _fields_ = [
        ("realPos", c_float * 3),
        ("realSpeed", c_float * 3),
        ("inputStatus", c_int32 * 1),
        ("outputStatus", c_int32 * 1),
        ("limitNStatus", c_int32 * 1),
        ("limitPStatus", c_int32 * 1),
        ("machineRunStatus", c_int32 * 1),
        ("axisStatus", c_int32 * 3),
        ("homeStatus", c_int32 * 1),
        ("file", c_ubyte * 600)
    ]


ms = machine_status()

# 给控制器编号，此ID号唯一
iD = 1
axis = 1
ip = '192.168.0.30'
port = 8088

# 连接控制器
print(fmc4030.FMC4030_Open_Device(1, "192.168.0.30", 8088))

# 控制器单轴运动
print(fmc4030.FMC4030_Jog_Single_Axis(1, 1, c_float(1000), c_float(100), c_float(100), c_float(100), 1))

# 延时等待，等待控制卡实际启动
time.sleep(0.1)

# 等待轴运行完成，过程中不断获取轴实际位置并输出
while fmc4030.FMC4030_Check_Axis_Is_Stop(iD, axis) == 0:
    fmc4030.FMC4030_Get_Machine_Status(iD, pointer(ms))
    # print(ms.realPos[axis])

# 关闭控制器连接，使用完成一定调用此函数释放资源
# print(fmc4030.FMC4030_Close_Device(iD))
