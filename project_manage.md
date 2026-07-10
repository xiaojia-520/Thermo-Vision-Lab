# FMC4030 控制器 CH3 联调记录

## 1. 当前目标

本次联调目标是确认电脑通过以太网直连 FMC4030 控制器后，能否连接控制器并控制 CH3 轴运动。

结论：已确认可以连接控制器，并成功控制 CH3 相对运动。

## 2. 网络连接与配置

### 2.1 初始情况

电脑以太网最初配置为：

```text
以太网 IP: 192.168.1.1
子网掩码: 255.255.255.0
```

FMC4030 示例资料中的默认控制器地址为：

```text
控制器 IP: 192.168.0.30
控制器端口: 8088
```

由于电脑和控制器不在同一网段，最初无法通过以太网确认控制器通信。

### 2.2 修改电脑以太网 IP

将电脑以太网改为：

```text
以太网 IP: 192.168.0.1
子网掩码: 255.255.255.0
默认网关: 留空
```

修改后控制器连通性正常。

### 2.3 连通性验证结果

控制器 TCP 端口测试结果：

```text
ComputerName     : 192.168.0.30
RemoteAddress    : 192.168.0.30
RemotePort       : 8088
InterfaceAlias   : 以太网
SourceAddress    : 192.168.0.1
TcpTestSucceeded : True
```

Ping 测试结果：

```text
Pinging 192.168.0.30 from 192.168.0.1:
Reply from 192.168.0.30: bytes=32 time<1ms TTL=255
Reply from 192.168.0.30: bytes=32 time<1ms TTL=255
```

ARP 表中看到控制器：

```text
192.168.0.30    E0-9A-8E-13-1B-33   Reachable
```

说明控制器确实通过电脑的 `以太网` 网卡连通，而不是走虚拟网卡。

## 3. SDK 与调用方式

### 3.1 使用的 DLL

本次调用使用的是仓库内 C# 示例目录中的 32 位 DLL：

```text
资料\FMC4030二次开发示例\FMC4030_Demo_CSharp(c#)\FMC4030_Demo_CSharp\FMC4030_Demo_CSharp\FMC4030-Dll.dll
```

注意：仓库内 Windows 版 `FMC4030-Dll.dll` 是 32 位 DLL。64 位 Python 直接加载会失败，报错类似：

```text
%1 不是有效的 Win32 应用程序
```

因此本次使用 32 位 PowerShell 通过 C# P/Invoke 调用 DLL。

32 位 PowerShell 路径：

```text
C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe
```

### 3.2 关键 SDK 接口

打开控制器：

```c
FMC4030_Open_Device(int id, char* ip, int port)
```

关闭控制器：

```c
FMC4030_Close_Device(int id)
```

读取机器状态：

```c
FMC4030_Get_Machine_Status(int id, unsigned char* machineData)
```

单轴运动：

```c
FMC4030_Jog_Single_Axis(
    int id,
    int axis,
    float pos,
    float speed,
    float acc,
    float dec,
    int mode
)
```

检查轴是否停止：

```c
FMC4030_Check_Axis_Is_Stop(int id, int axis)
```

停止单轴：

```c
FMC4030_Stop_Single_Axis(int id, int axis, int mode)
```

### 3.3 轴号对应关系

根据 Qt 示例和 SDK 示例，轴号从 0 开始：

```text
axis = 0  -> CH1 / X
axis = 1  -> CH2 / Y
axis = 2  -> CH3 / Z
```

本次用户连接的是 CH3，因此操作轴号为：

```text
axis = 2
```

### 3.4 运动模式

根据 Java 示例注释：

```text
mode = 1  -> 相对运动
mode = 2  -> 绝对运动
```

本次全部使用相对运动：

```text
mode = 1
```

## 4. CH3 运动测试流程

每次运动测试的基本流程：

1. 打开控制器连接。
2. 读取 CH3 当前状态和当前位置。
3. 发送 CH3 相对运动命令。
4. 循环读取 CH3 位置、速度和轴状态。
5. 调用 `FMC4030_Check_Axis_Is_Stop` 判断是否停止。
6. 如果超时仍未停止，则调用 `FMC4030_Stop_Single_Axis` 停止轴。
7. 读取最终位置。
8. 关闭控制器连接。

本次使用的主要参数：

```text
id    = 0
ip    = 192.168.0.30
port  = 8088
axis  = 2
mode  = 1
acc   = 100
dec   = 100
```

## 5. 测试结果记录

### 5.1 初始只读检查

打开控制器成功：

```text
OPEN=0
```

CH3 初始读取：

```text
AXIS=2
POS=0
SPEED=0
IS_STOP=1
```

后续发现单独读取位置接口返回值不如完整状态结构可靠，因此改用 `FMC4030_Get_Machine_Status` 读取 `realPos[2]`。

### 5.2 完整状态读取

完整状态读取到三轴位置：

```text
AXIS=0 POS=-72.208 SPEED=0 STATUS=0x0E08
AXIS=1 POS=280.634 SPEED=0 STATUS=0x0E08
AXIS=2 POS=691.068 SPEED=0 STATUS=0x1608
```

CH3 状态 `0x1608` 表示轴已停止，并且正负限位未触发；同时带有 `0x1000` 回零超时标志。

### 5.3 CH3 相对 +10 测试

命令参数：

```text
axis  = 2
pos   = +10
speed = 20
acc   = 100
dec   = 100
mode  = 1
```

结果：

```text
BEFORE POS=691.068
JOG_RET=0
AFTER  POS=701.068
DELTA=10
FINAL_STOP=1
```

说明 CH3 成功相对运动 `+10`。

### 5.4 CH3 相对 +50 测试一

命令参数：

```text
axis  = 2
pos   = +50
speed = 30
acc   = 100
dec   = 100
mode  = 1
```

结果：

```text
BEFORE POS=701.068
AFTER  POS=751.068
DELTA=50
FINAL_STOP=1
```

### 5.5 CH3 相对 +50 测试二

结果：

```text
BEFORE POS=751.068
AFTER  POS=801.068
DELTA=50
FINAL_STOP=1
```

### 5.6 CH3 相对 +50 测试三

结果：

```text
BEFORE POS=801.068
AFTER  POS=851.068
DELTA=50
FINAL_STOP=1
```

## 6. 当前最终状态

当前已知 CH3 位置：

```text
CH3 POS = 851.068
```

当前已知控制器连接参数：

```text
控制器 IP: 192.168.0.30
控制器端口: 8088
电脑以太网 IP: 192.168.0.1
```

CH3 已确认可以正常运动，已完成：

```text
+10
+50
+50
+50
```

累计相对运动：

```text
+160
```

## 7. 注意事项

1. CH3 可以运动，但状态中出现过 `0x1000` 回零超时标志。正式实验前建议确认原点开关、限位开关和回零流程。
2. 后续不要盲目连续向同一方向大距离运动，避免接近机械行程末端。
3. 当前只验证了 CH3，CH1 和 CH2 尚未做运动测试。
4. 由于 DLL 是 32 位，后续如果使用 Python，建议安装 32 位 Python，或者改用 C# / C++ / 32 位 PowerShell 调用。
5. 如果要开发正式上位机，建议封装一层控制器接口，统一处理连接、状态读取、运动、停止、异常和日志。

## 8. 后续建议

短期建议：

1. 做一个最小控制脚本，支持读取三轴位置、CH3 相对运动、停止。
2. 明确 CH3 正方向和负方向对应实际机械运动方向。
3. 检查 CH3 机械行程范围和限位开关是否有效。
4. 在确认安全后，再测试 CH3 回零。

中期建议：

1. 建立 `src` 目录，放正式控制代码，不和供应商资料混在一起。
2. 做控制器配置文件，例如记录控制器 IP、端口、轴号、速度、加速度、行程限制。
3. 后续和红外相机联动时，将每次采集的红外数据与三轴坐标绑定保存。
