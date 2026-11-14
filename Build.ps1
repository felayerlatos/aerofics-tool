param(
    [string]$ToolsDirectory = '',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'release')
)
$ErrorActionPreference = 'Stop'
if ($ToolsDirectory) { & (Join-Path $PSScriptRoot 'Pack-Payload.ps1') -ToolsDirectory $ToolsDirectory }
dotnet publish (Join-Path $PSScriptRoot 'src\Aerofics.Toolbox.csproj') -c Release -r win-x64 --self-contained true -o $OutputDirectory --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-Item -LiteralPath (Join-Path $OutputDirectory 'AeroficsToolbox.exe')
