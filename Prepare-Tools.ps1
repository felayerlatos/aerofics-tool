param(
    [Parameter(Mandatory)][string]$OriginalToolsDirectory,
    [Parameter(Mandatory)][string]$OptimizerExe,
    [Parameter(Mandatory)][string]$TunerExe,
    [string]$Destination = (Join-Path $PSScriptRoot 'build\tools')
)
$ErrorActionPreference = 'Stop'
$original = (Resolve-Path -LiteralPath $OriginalToolsDirectory).Path
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw 'Choose a new, empty destination.' }
$config = Get-Content -LiteralPath (Join-Path $original 'launcher.json') -Raw -Encoding utf8 | ConvertFrom-Json
$excluded = @('AIDA64','HWiNFO','Geek Uninstaller','Thaiphoon')
$programs = @($config.programs | Where-Object { $_.name -notin $excluded })
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach ($program in $programs) {
    $relativeDirectory = Split-Path -Path $program.exe -Parent
    $source = Join-Path $original $relativeDirectory
    $target = Join-Path $Destination $relativeDirectory
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        if ($relative -match '(^|[\\/])DDU Logs([\\/]|$)' -or $file.Name -match '^(pkey\.txt|Log\.txt|\.complete)$' -or $file.Extension -eq '.log') { continue }
        $out = Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $out
    }
}
$extras = Join-Path $Destination 'Extras'
New-Item -ItemType Directory -Path $extras -Force | Out-Null
Copy-Item -LiteralPath $OptimizerExe -Destination (Join-Path $extras 'AeroficsOptimizer-2.0.0.exe')
Copy-Item -LiteralPath $TunerExe -Destination (Join-Path $extras 'ZeroCpuTuner.exe')
$publicConfig = [ordered]@{
    programs = $programs
    optimizerExe = 'Extras/AeroficsOptimizer-2.0.0.exe'
    tunerExe = 'Extras/ZeroCpuTuner.exe'
    voltageProvider = 'HwInfoThenAmdSmu'
}
$publicConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'launcher.json') -Encoding utf8
Write-Output $Destination
