# Thermo Vision Lab

宽温域三维红外成像测试装置的 Windows 上位机。项目将三组 FMC4030 运动平台、TOMILO 温湿度实验舱和红外相机工具集中到一个 WPF 控制台中，用于设备状态监视、运动控制、环境控制和红外采集。

> 当前项目仍处于设备联调阶段。涉及回零、行程标定、平台运动或实验舱启停前，请先确认物理急停、硬件限位和现场人员安全；软件联锁不能替代硬件保护。

## 当前能力

### 运动平台

- 同时监视 3 台 FMC4030 控制器，状态刷新周期为 200 ms。
- 1、2 号控制器管理 X/Y 轴，3 号控制器管理 X/Y/Z 轴。
- 显示原始位置、软件位置、速度、运行状态、正负限位、报警和回零状态。
- 支持逐台回零、负限位/行程标定、相对步进、绝对定位、软件限位和全部停止。
- 未连接、未回零、未配置软件限位、轴未停止或目标越界时，会禁止相应运动操作。

### 温湿度实验舱

- 通过 Modbus TCP 每秒读取运行状态、当前温湿度、设定值、工作阶段和报警输入。
- 支持设置目标温度与湿度，以及远程启动、停止实验舱。
- 写操作需要用户二次确认，并在写入后回读设备状态；存在活动报警时禁止远程启动。
- 当前固件未必提供可靠的压缩机、温控、湿控、排水和照明状态，无法确认时界面会显示“无数据”。

### 红外相机

- 主程序通过 Ping 检测 `192.168.1.201` 是否在线。
- 点击“打开红外工具”会启动或激活仓库中的官方 `IRToolPro`。
- `ThermoVision.Camera` 是独立的 SDK 接入原型，目前使用演示相机服务，只验证 MVVM 和 JSON 元数据保存流程，尚未接入真实 Yoseen SDK、实时温度流或热图渲染。

## 系统结构

```text
ThermoVision.exe（x64 WPF 主程序）
├─ BoxHost.dll
│  └─ Modbus TCP ── TOMILO 实验舱 192.168.1.30:8000
├─ 命名管道 ── MotionHost.exe（x86）
│               └─ FMC4030-Dll.dll
│                  ├─ 控制器 1：192.168.1.31:8088（X/Y）
│                  ├─ 控制器 2：192.168.1.32:8088（X/Y）
│                  └─ 控制器 3：192.168.1.33:8088（X/Y/Z）
└─ 启动外部程序 ── IRToolPro.exe ── 红外相机 192.168.1.201
```

FMC4030 厂商 DLL 为 32 位，不能直接加载到 x64 主程序中，因此运动控制由独立的 x86 `MotionHost` 执行。主程序负责界面和安全检查，两者通过本机命名管道交换命令、进度和实时状态。

## 默认设备地址

| 设备 | 地址 | 协议/说明 |
| --- | --- | --- |
| TOMILO 实验舱 | `192.168.1.30:8000` | Modbus TCP，Unit ID `1` |
| FMC4030 控制器 1 | `192.168.1.31:8088` | X/Y 轴 |
| FMC4030 控制器 2 | `192.168.1.32:8088` | X/Y 轴 |
| FMC4030 控制器 3 | `192.168.1.33:8088` | X/Y/Z 轴 |
| 红外相机 | `192.168.1.201` | 主界面用 Ping 判断在线状态 |

运行电脑的有线网卡需要配置为同一网段，例如选择一个未占用的 `192.168.1.x/24` 地址。当前设备地址直接定义在代码中，如需更换，应同步修改主界面、`MotionServer` 和对应设备服务中的常量。

## 开发环境

- Windows 10/11 x64
- 支持 `.slnx` 的 Visual Studio，并安装“.NET 桌面开发”工作负载
- .NET Framework 4.8 Developer Pack / Targeting Pack
- FMC4030 随项目提供的 x86 原生 DLL
- 真实设备联调所需的独立有线网卡或交换机

本项目的主工程是传统 .NET Framework WPF 项目。请使用 Visual Studio 自带的完整 MSBuild 构建；仅安装跨平台 .NET SDK 后执行 `dotnet build` 或 `dotnet msbuild`，可能不会生成 WPF 的 XAML 中间代码。

## 构建与运行

1. 使用 Visual Studio 打开 `src/src.slnx`。
2. 选择 `Debug | x64` 或 `Release | x64`。
3. 生成整个解决方案。解决方案会以 x86 生成 `MotionHost`，并将它和 `FMC4030-Dll.dll` 复制到主程序输出目录。
4. 将 `ThermoVision` 设为启动项目并运行。

Debug 模式的主要输出如下：

```text
src/ThermoVision/bin/x64/Debug/ThermoVision.exe
src/ThermoVision/bin/x64/Debug/BoxHost.dll
src/ThermoVision/bin/x64/Debug/MotionHost/MotionHost.exe
src/ThermoVision/bin/x64/Debug/MotionHost/FMC4030-Dll.dll
```

如果当前 Visual Studio 无法打开 `.slnx`，请更新 IDE，或依次以对应平台生成以下项目：

1. `src/BoxHost/BoxHost.csproj`：Any CPU 或 x64；
2. `src/MotionHost/MotionHost.csproj`：x86；
3. `src/ThermoVision/ThermoVision.csproj`：x64。

### 运行红外工具

主程序会从自身目录向上查找：

```text
IRToolPro_v2.4.0.0626/IRToolPro.exe
```

开发目录中的实际位置为 `src/IRToolPro_v2.4.0.0626`。不要只复制 `IRToolPro.exe`，官方软件依赖同目录下的 DLL、配置和资源文件。

### 独立相机原型

如需查看尚未接入真实 SDK 的相机原型，可单独打开并以 x64 生成：

```text
ThermoVision.Camera/ThermoVision.Camera.csproj
```

演示采集产生的 JSON 元数据保存在：

```text
%USERPROFILE%/Documents/ThermoVision/Data/yyyy-MM-dd/
```

## 使用顺序建议

1. 配置电脑网卡，先用 `ping` 或厂商工具确认目标设备可达。
2. 启动主程序，观察各模块连接状态；不要在连接状态不稳定时执行写入或运动。
3. 首次使用运动轴时，低速、单轴确认物理方向和硬件限位，再执行回零与行程标定。
4. 回零和软件限位有效后，再测试小距离步进与目标定位。
5. 操作实验舱前核对当前温湿度、目标值和报警状态；所有写操作均等待回读成功后再进行下一步。

运动零点和软件限位保存在当前用户目录：

```text
%LOCALAPPDATA%/ThermoVision/motion-settings.xml
```

重新标定、换控制器或调整机械结构后，应检查并按需清理/重建这份配置。不要直接复制其他设备上的零点和行程参数。

## 目录说明

| 路径 | 内容 |
| --- | --- |
| `src/ThermoVision` | x64 WPF 主程序与设备控制页面 |
| `src/MotionHost` | x86 FMC4030 后台进程、原生 DLL 封装和运动安全逻辑 |
| `src/BoxHost` | TOMILO Modbus TCP 客户端与实验舱服务 |
| `src/IRToolPro_v2.4.0.0626` | 厂商红外相机工具及运行依赖 |
| `ThermoVision.Camera` | 尚未接入真实 SDK 的独立相机原型 |
| `红外摄像头对应驱动以及参数总结` | 相机驱动、手册、样张与使用记录 |
| `资料` | FMC4030 SDK、示例、接线图和项目资料 |
| `artifacts` | 阶段性联调/验证产物，不是日常开发输出目录 |
| `development_log_*.md` | 按日期记录的开发和现场联调过程 |

## 已知限制与现场注意事项

- 设备 IP、端口和轴映射目前是硬编码配置，没有设置界面。
- 1 号控制器的回零最大搜索距离已调整为 150 个控制器单位，但开发日志标记为仍需真机复测。
- 控制器单位与毫米的换算、各轴物理方向和完整行程需要逐台现场标定。
- 实验舱部件状态寄存器在部分固件上可能始终返回 0，界面会将不可信数据标为“无数据”。
- `ThermoVision.Camera` 不能代表真实相机已经完成 SDK 接入；当前生产入口仍是官方 IRToolPro。
- 仓库暂无自动化测试，硬件相关改动应先在断开负载或低速条件下验证。
- “全部停止”是软件命令，不是急停。出现人员、机械或电气风险时，应使用现场物理急停并切断相应动力。

## 相关资料

- `project_manage.md`：早期项目规划与需求拆解。
- `development_log_2026-07-24.md`：多设备平台整合、现场协议验证和遗留问题记录。
- `红外摄像头对应驱动以及参数总结/红外摄像头与IRToolPro软件总结.md`：红外相机与官方软件说明。
- `TOMILO-L控制器通讯协议(标准版) V2.1.pdf`：实验舱控制器协议。
- `资料/FMC4030二次开发库详解V1.0.pdf`：运动控制器 SDK 资料。

部分开发日志反映的是当日阶段状态，可能早于当前代码。例如 2026-07-24 的日志将实验舱描述为“只读”，而当前代码已经包含经过确认与回读的 FC05/FC06 控制流程；判断功能现状时请以代码和最新现场验证结果为准。
