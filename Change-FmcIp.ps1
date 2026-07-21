#requires -Version 5.1

<#
    FMC4030 修改 IP 地址脚本

    示例：
    .\Change-FmcIp.ps1 -OldIp 192.168.0.30 -NewIp 192.168.0.33

    注意：
    1. 必须使用 32 位 PowerShell。
    2. 一次只连接一台待修改的控制器。
    3. 控制器必须处于停止状态。
    4. 脚本只修改 IP，不修改运动参数。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OldIp,

    [Parameter(Mandatory = $true)]
    [string]$NewIp,

    [int]$Port = 8088,
    [int]$DeviceId = 0,
    [string]$SourceIp = "192.168.0.1",
    [string]$DllPath = "D:\Thermo-Vision-Lab\资料\FMC4030二次开发示例\FMC4030_Demo_CSharp(c#)\FMC4030_Demo_CSharp\FMC4030_Demo_CSharp\FMC4030-Dll.dll"
)

$ErrorActionPreference = "Stop"

if ([Environment]::Is64BitProcess) {
    throw @"
当前 PowerShell 是 64 位。
请使用：
C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe
"@
}

if (-not (Test-Path -LiteralPath $DllPath)) {
    throw "找不到 FMC4030 DLL：$DllPath"
}

try {
    $oldAddress = [System.Net.IPAddress]::Parse($OldIp)
    $newAddress = [System.Net.IPAddress]::Parse($NewIp)
}
catch {
    throw "OldIp 或 NewIp 不是合法 IP 地址"
}

if ($oldAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
    $newAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "OldIp 和 NewIp 必须是 IPv4 地址"
}

if ($OldIp -eq $NewIp) {
    throw "新旧 IP 不能相同"
}

function Test-TcpPort {
    param(
        [string]$Ip,
        [int]$Port
    )

    $localEndPoint = New-Object System.Net.IPEndPoint(
        ([System.Net.IPAddress]::Parse($SourceIp)),
        0
    )

    $client = New-Object System.Net.Sockets.TcpClient($localEndPoint)

    try {
        $task = $client.ConnectAsync($Ip, $Port)
        return ($task.Wait(1500) -and $client.Connected)
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

Write-Host "检查目标 IP $NewIp 是否空闲..." -ForegroundColor Yellow
$ping = New-Object System.Net.NetworkInformation.Ping
$pingReply = $ping.Send($NewIp, 1000)

if ($pingReply.Status -eq [System.Net.NetworkInformation.IPStatus]::Success) {
    throw "目标 IP $NewIp 已经有设备响应，停止修改，避免 IP 冲突"
}

if (Test-TcpPort -Ip $NewIp -Port $Port) {
    throw "目标 IP $NewIp 的 TCP $Port 端口已经开放，停止修改"
}

$nativeCode = @'
using System;
using System.Runtime.InteropServices;

public static class FmcNativeIpTool
{
    [DllImport(@"__DLL_PATH__",
        CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi)]
    public static extern int FMC4030_Open_Device(
        int id, string ip, int port);

    [DllImport(@"__DLL_PATH__",
        CallingConvention = CallingConvention.StdCall)]
    public static extern int FMC4030_Get_Device_Para(
        int id, [Out] byte[] devicePara);

    [DllImport(@"__DLL_PATH__",
        CallingConvention = CallingConvention.StdCall)]
    public static extern int FMC4030_Set_Device_Para(
        int id, byte[] devicePara);

    [DllImport(@"__DLL_PATH__",
        CallingConvention = CallingConvention.StdCall)]
    public static extern int FMC4030_Close_Device(int id);
}
'@

$nativeCode = $nativeCode.Replace("__DLL_PATH__", $DllPath)
Add-Type -TypeDefinition $nativeCode

Write-Host "连接控制器 $OldIp`:$Port ..." -ForegroundColor Yellow
$openResult = [FmcNativeIpTool]::FMC4030_Open_Device($DeviceId, $OldIp, $Port)
Write-Host "Open_Device 返回值：$openResult"

if ($openResult -ne 0) {
    throw "连接控制器失败，返回值：$openResult"
}

try {
    $devicePara = New-Object byte[] 1024
    $getResult = [FmcNativeIpTool]::FMC4030_Get_Device_Para($DeviceId, $devicePara)
    Write-Host "Get_Device_Para 返回值：$getResult"

    if ($getResult -ne 0) {
        throw "读取控制器参数失败，返回值：$getResult"
    }

    # machine_device_para 中 ip[15] 位于 offset 12。
    # port 位于 offset 28。其余字段保持不变。
    $storedIp = [Text.Encoding]::ASCII.GetString($devicePara, 12, 15).Trim([char]0)
    $storedPort = [BitConverter]::ToInt32($devicePara, 28)

    Write-Host "控制器当前保存的 IP：$storedIp" -ForegroundColor Cyan
    Write-Host "控制器当前保存的端口：$storedPort" -ForegroundColor Cyan

    if ($storedIp -ne $OldIp) {
        throw "控制器实际保存的 IP 是 $storedIp，不是指定的旧 IP $OldIp，停止修改"
    }

    Write-Host "准备修改：$OldIp -> $NewIp" -ForegroundColor Yellow

    [Array]::Clear($devicePara, 12, 15)
    $newIpBytes = [Text.Encoding]::ASCII.GetBytes($NewIp)
    [Array]::Copy($newIpBytes, 0, $devicePara, 12, $newIpBytes.Length)

    $setResult = [FmcNativeIpTool]::FMC4030_Set_Device_Para($DeviceId, $devicePara)
    Write-Host "Set_Device_Para 返回值：$setResult"

    if ($setResult -ne 0) {
        throw "写入控制器参数失败，返回值：$setResult"
    }

    $verifyIp = [Text.Encoding]::ASCII.GetString($devicePara, 12, 15).Trim([char]0)
    Write-Host "内存中的新 IP：$verifyIp" -ForegroundColor Green
}
finally {
    try {
        $closeResult = [FmcNativeIpTool]::FMC4030_Close_Device($DeviceId)
        Write-Host "Close_Device 返回值：$closeResult"
    }
    catch {
        Write-Warning "关闭连接时出现异常：$($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "IP 参数写入成功。" -ForegroundColor Green
Write-Host "请给控制器断电重启，使新 IP 生效。" -ForegroundColor Yellow
Write-Host "重启后应访问：$NewIp`:$Port" -ForegroundColor Cyan
