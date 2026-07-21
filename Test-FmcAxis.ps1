#requires -Version 5.1

<##
    Safe single-axis test tool for FMC4030.

    Run this file with 32-bit Windows PowerShell:
    C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe `
      -ExecutionPolicy Bypass -File .\Test-FmcAxis.ps1 `
      -Ip 192.168.1.31 -Axis 0 -Distance 1 -Speed 2 -Acc 10 -Dec 10

    Distance is relative when Mode=1 and absolute when Mode=2.
    The unit depends on the controller's configured lead/division.
    Positive and negative distance represent opposite directions.

    The script does not move an axis unless -AllowNotHomed is supplied
    when the selected axis has not completed homing.
##>

[CmdletBinding()]
param(
    [string]$Ip = "192.168.1.31",
    [int]$Axis = 0,
    [double]$Distance,
    [double]$Speed = 2,
    [double]$Acc = 10,
    [double]$Dec = 10,
    [ValidateSet(1, 2)]
    [int]$Mode = 1,
    [int]$Port = 8088,
    [switch]$AllowNotHomed,
    [switch]$StopOnly,
    [string]$DllDirectory = "AUTO"
)

$ErrorActionPreference = "Stop"

if ([Environment]::Is64BitProcess) {
    throw "Use 32-bit PowerShell: C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
}

if ($Axis -lt 0 -or $Axis -gt 2) {
    throw "Axis must be 0, 1, or 2."
}

if (-not $StopOnly) {
    if ($PSBoundParameters.ContainsKey("Distance") -eq $false -or $Distance -eq 0) {
        throw "Distance is required and cannot be zero."
    }
    if ([Math]::Abs($Distance) -gt 50) {
        throw "For safety, one command is limited to 50 units."
    }
    if ($Speed -le 0 -or $Acc -le 0 -or $Dec -le 0) {
        throw "Speed, Acc, and Dec must be greater than zero."
    }
}

if ($DllDirectory -eq "AUTO") {
    $dllCandidates = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter "FMC4030-Dll.dll" -Recurse -File |
        Where-Object { $_.Length -eq 23552 })
    if ($dllCandidates.Count -eq 0) {
        throw "Could not find the 32-bit FMC4030-Dll.dll below: $PSScriptRoot"
    }
    $DllDirectory = $dllCandidates[0].DirectoryName
}

if (-not (Test-Path -LiteralPath (Join-Path $DllDirectory "FMC4030-Dll.dll"))) {
    throw "FMC4030-Dll.dll was not found in: $DllDirectory"
}

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class FmcAxisTestNative
{
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    public static extern bool SetDllDirectory(string path);

    [DllImport("FMC4030-Dll.dll", CallingConvention=CallingConvention.StdCall, CharSet=CharSet.Ansi)]
    public static extern int FMC4030_Open_Device(int id, string ip, int port);

    [DllImport("FMC4030-Dll.dll", CallingConvention=CallingConvention.StdCall)]
    public static extern int FMC4030_Close_Device(int id);

    [DllImport("FMC4030-Dll.dll", CallingConvention=CallingConvention.StdCall)]
    public static extern int FMC4030_Jog_Single_Axis(int id, int axis, float pos, float speed, float acc, float dec, int mode);

    [DllImport("FMC4030-Dll.dll", CallingConvention=CallingConvention.StdCall)]
    public static extern int FMC4030_Check_Axis_Is_Stop(int id, int axis);

    [DllImport("FMC4030-Dll.dll", CallingConvention=CallingConvention.StdCall)]
    public static extern int FMC4030_Stop_Single_Axis(int id, int axis, int mode);

    [DllImport("FMC4030-Dll.dll", CallingConvention=CallingConvention.StdCall)]
    public static extern int FMC4030_Get_Machine_Status(int id, [Out] byte[] data);
}
'@

[FmcAxisTestNative]::SetDllDirectory($DllDirectory) | Out-Null

function Get-Status {
    $buffer = New-Object byte[] 1024
    $result = [FmcAxisTestNative]::FMC4030_Get_Machine_Status(0, $buffer)
    if ($result -ne 0) {
        throw "Get_Machine_Status failed: $result"
    }

    $positions = @()
    $speeds = @()
    $axisStates = @()
    for ($i = 0; $i -lt 3; $i++) {
        $positions += [BitConverter]::ToSingle($buffer, $i * 4)
        $speeds += [BitConverter]::ToSingle($buffer, 12 + $i * 4)
        $axisStates += [BitConverter]::ToUInt32($buffer, 44 + $i * 4)
    }

    [PSCustomObject]@{
        Position = $positions
        Speed = $speeds
        AxisState = $axisStates
        MachineState = [BitConverter]::ToUInt32($buffer, 40)
    }
}

$id = 0
$opened = $false

try {
    $openResult = [FmcAxisTestNative]::FMC4030_Open_Device($id, $Ip, $Port)
    if ($openResult -ne 0) {
        throw "Open_Device failed for $Ip`:$Port, result=$openResult"
    }
    $opened = $true

    $before = Get-Status
    $state = $before.AxisState[$Axis]
    Write-Host ("Before: axis={0}, position={1}, speed={2}, state=0x{3:X4}" -f $Axis, $before.Position[$Axis], $before.Speed[$Axis], $state) -ForegroundColor Cyan

    if ($StopOnly) {
        $stopResult = [FmcAxisTestNative]::FMC4030_Stop_Single_Axis($id, $Axis, 1)
        Write-Host "Stop result: $stopResult" -ForegroundColor Yellow
        exit 0
    }

    if (($state -band 0x0001) -ne 0) {
        throw "The selected axis is already running."
    }
    if ($Distance -gt 0 -and ($state -band 0x0020) -ne 0) {
        throw "Positive limit is active; positive movement is blocked."
    }
    if ($Distance -lt 0 -and ($state -band 0x0010) -ne 0) {
        throw "Negative limit is active; negative movement is blocked."
    }
    if (($state -band 0x0040) -eq 0 -and -not $AllowNotHomed) {
        throw "The axis is not homed. Home it first, or explicitly add -AllowNotHomed."
    }

    Write-Host ("Moving: ip={0}, axis={1}, distance={2}, speed={3}, acc={4}, dec={5}, mode={6}" -f $Ip, $Axis, $Distance, $Speed, $Acc, $Dec, $Mode) -ForegroundColor Yellow
    $confirm = Read-Host "Type RUN to send the motion command"
    if ($confirm -cne "RUN") {
        Write-Host "Cancelled. No motion command was sent." -ForegroundColor Green
        exit 0
    }

    $moveResult = [FmcAxisTestNative]::FMC4030_Jog_Single_Axis(
        $id, $Axis, [float]$Distance, [float]$Speed, [float]$Acc, [float]$Dec, $Mode)
    Write-Host "Motion command result: $moveResult" -ForegroundColor Yellow
    if ($moveResult -ne 0) {
        throw "Motion command failed: $moveResult"
    }

    $deadline = (Get-Date).AddSeconds(60)
    $lastPosition = $nowPosition = (Get-Status).Position[$Axis]
    $stalledSince = $null
    do {
        Start-Sleep -Milliseconds 250
        $now = Get-Status
        Write-Host ("position={0}, speed={1}, state=0x{2:X4}" -f $now.Position[$Axis], $now.Speed[$Axis], $now.AxisState[$Axis])
        if (($now.AxisState[$Axis] -band 0x0030) -ne 0) {
            [void][FmcAxisTestNative]::FMC4030_Stop_Single_Axis($id, $Axis, 2)
            throw "A hardware limit became active. An immediate stop command was sent."
        }
        if ([Math]::Abs($now.Position[$Axis] - $lastPosition) -lt 0.0001 -and
            [Math]::Abs($now.Speed[$Axis]) -gt 0.001) {
            if ($null -eq $stalledSince) {
                $stalledSince = Get-Date
            }
            elseif (((Get-Date) - $stalledSince).TotalSeconds -ge 2) {
                [void][FmcAxisTestNative]::FMC4030_Stop_Single_Axis($id, $Axis, 2)
                throw "The axis reported motion but its position did not change for 2 seconds. An immediate stop command was sent."
            }
        }
        else {
            $stalledSince = $null
        }
        $lastPosition = $now.Position[$Axis]
        if ((Get-Date) -gt $deadline) {
            [void][FmcAxisTestNative]::FMC4030_Stop_Single_Axis($id, $Axis, 2)
            throw "Motion timeout. An immediate stop command was sent."
        }
    } while (([FmcAxisTestNative]::FMC4030_Check_Axis_Is_Stop($id, $Axis)) -eq 0)

    Write-Host "Axis stopped." -ForegroundColor Green
}
finally {
    if ($opened) {
        $closeResult = [FmcAxisTestNative]::FMC4030_Close_Device($id)
        Write-Host "Close result: $closeResult"
    }
}
