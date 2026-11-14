param(
    [string]$ToolsDirectory = (Join-Path $PSScriptRoot 'build\tools'),
    [Parameter(Mandatory)][string]$TunerPayloadZip,
    [string]$Version = '2026.11.15'
)
$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release\AeroficsToolbox.exe') -Destination (Join-Path $dist "AeroficsToolbox-v$Version-win-x64.exe")
$paths = @(git -c core.quotepath=false -C $PSScriptRoot ls-files)
if ($LASTEXITCODE -ne 0 -or !$paths.Count) { throw 'Stage the source files in Git before packaging source.' }
$sourceZip = Join-Path $dist "AeroficsToolbox-v$Version-source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
$archive = [IO.Compression.ZipFile]::Open($sourceZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($path in $paths) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $PSScriptRoot $path),('aerofics-tool/' + $path),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$inputZip = Join-Path $dist "AeroficsToolbox-v$Version-build-inputs.zip"
if (Test-Path -LiteralPath $inputZip) { Remove-Item -LiteralPath $inputZip }
$archive = [IO.Compression.ZipFile]::Open($inputZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $ToolsDirectory -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($ToolsDirectory,$file.FullName).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,('tools/' + $relative),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$TunerPayloadZip,'zero-tuner-payload.zip',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $archive.Dispose() }
Get-ChildItem -LiteralPath $dist -File | Select-Object Name,Length
