param([string]$ExistingExe = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskBuildRoot = Join-Path $PSScriptRoot ('.build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskBuildRoot | Out-Null
if (-not $ExistingExe) { $ExistingExe = Join-Path (Split-Path $PSScriptRoot -Parent) 'ZeroCpuTuner.exe' }
$ExistingExe = (Resolve-Path -LiteralPath $ExistingExe).Path
$report = Join-Path $taskBuildRoot 'check.json'
$data = Join-Path $taskBuildRoot 'data'
$taskArgs = '--check-install "' + $report + '" --data-dir "' + $data + '"'
$proc = Start-Process -FilePath $ExistingExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -Wait
if ($proc.ExitCode -ne 0) { throw '读取交付 EXE 内置组件失败，见构建目录 check.json。' }
$payloadRoot = Join-Path $data 'engine-1.0.0-r3'
if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'backend\CpuSensor.dll'))) { throw '请使用本次交付的修订版 EXE 构建。' }
$payload = Join-Path $PSScriptRoot 'payload.zip'
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload }
$archive = [IO.Compression.ZipFile]::Open($payload, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in Get-ChildItem -LiteralPath $payloadRoot -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($payloadRoot, $item.FullName).Replace('\','/')
        if ($relative -eq '.complete' -or $relative -eq 'backend/ryzen-smu-cli.sys') { continue }
        $level = if ($relative.StartsWith('backend/')) { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $item.FullName, $relative, $level) | Out-Null
    }
} finally { $archive.Dispose() }
& dotnet publish (Join-Path $PSScriptRoot 'ZeroCpuTuner.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $PSScriptRoot 'publish')
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output (Join-Path $PSScriptRoot 'publish\ZeroCpuTuner.exe')
