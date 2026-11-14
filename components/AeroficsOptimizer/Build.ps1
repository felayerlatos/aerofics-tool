param(
    [string]$DotnetPath = 'dotnet',
    [string]$SevenZipRoot = 'C:\Program Files\7-Zip',
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts')
)
$ErrorActionPreference = 'Stop'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$payload = Join-Path $OutputRoot 'payload'
$archive = Join-Path $OutputRoot 'runtime.7z'
$exe = Join-Path $OutputRoot 'AeroficsOptimizer-2.0.0.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
if (!(Test-Path -LiteralPath (Join-Path $SevenZipRoot '7z.dll'))) { throw 'Install 64-bit 7-Zip before building.' }
# Use a new output directory for each release; never mix files from earlier publishes.
if (Test-Path -LiteralPath $payload) { throw "Output already exists: $payload. Choose a new OutputRoot." }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
& $DotnetPath publish (Join-Path $PSScriptRoot 'App\SystemOptimizer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false --nologo -clp:ErrorsOnly -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Licenses') -Destination $payload -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.txt') -Destination (Join-Path $payload '使用说明.txt')
Push-Location $payload
try {
    & (Join-Path $SevenZipRoot '7z.exe') a -t7z $archive '.\*' -mx=9 -m0=lzma2 -md=256m -mfb=273 -ms=on -mmt=4 -bb0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw 'Compression failed.' }
} finally { Pop-Location }
& (Join-Path $PSScriptRoot 'BuildLauncher.ps1') -OutputRoot $OutputRoot -SevenZipRoot $SevenZipRoot
