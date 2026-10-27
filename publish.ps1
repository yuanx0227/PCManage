<#
.SYNOPSIS
    发布 PC控制（PCDeviceManage）为单文件 exe。

.DESCRIPTION
    默认发布为自包含（self-contained）的 win-x64 单文件（约 80 MB），目标机器不需要安装 .NET 运行时；
    加 -FrameworkDependent 则只有约 1.5 MB，但目标机器需要安装 .NET 8 桌面运行时和 ASP.NET Core 运行时。
    log4net.config 和 frp 目录会放在 exe 旁边。

.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -FrameworkDependent -Output D:\Release\PCManage
#>
param(
    [switch]$FrameworkDependent,
    [string]$Output = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PCDeviceManage\PCDeviceManage\PCDeviceManage.csproj'

$arguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--output', $Output,
    '--self-contained', $(if ($FrameworkDependent) { 'false' } else { 'true' }),
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none'
)
if (-not $FrameworkDependent) {
    # 自包含时运行时也打进 exe，压缩后体积大约减半（启动时解压到内存，略慢一点）。
    $arguments += '-p:EnableCompressionInSingleFile=true'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "已发布到 $Output"
