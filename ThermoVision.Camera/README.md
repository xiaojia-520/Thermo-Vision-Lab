# ThermoVision.Camera

红外相机独立采集模块的 WPF 架构骨架。

## 当前状态

- 技术栈：C#、WPF、.NET Framework 4.8、x64。
- 已有：MVVM 界面、相机服务接口、采集帧模型、JSON 元数据保存。
- 未接入：Yoseen SDK、实时温度流、热图渲染、设备配置写入。
- 当前 `DemoCameraService` 仅用于验证 UI 和保存流程，不会访问相机，也不会伪装成真实采集。

## 目录职责

- `ViewModels`：界面状态和命令；不直接调用 DLL。
- `Services/ICameraService.cs`：相机 SDK 的唯一入口抽象。
- `Services/DemoCameraService.cs`：临时演示实现；接 SDK 时替换成 `YoseenCameraService`。
- `Services/JsonFrameStorage.cs`：按日期保存采集元数据。
- `Models`：相机连接与温度帧的数据模型。

## 下一步

1. 从厂商获取或解析 Yoseen SDK 的 C# 调用说明。
2. 新增 `YoseenCameraService`，实现连接、设备信息读取和温度流回调。
3. 把温度矩阵和预览图加入 `ThermalFrame`，并将界面中央区域替换为热图渲染控件。
4. 在真实相机连通的前提下，只读验证设备信息和一帧温度数据。

## 运行

在 Visual Studio 中打开 `ThermoVision.Camera.csproj`，以 `x64` 生成并运行。
