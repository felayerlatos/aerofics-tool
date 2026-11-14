param([Parameter(Mandatory)][string]$ToolsDirectory)
$ErrorActionPreference = 'Stop'
$ToolsDirectory = (Resolve-Path -LiteralPath $ToolsDirectory).Path
$config = Get-Content -LiteralPath (Join-Path $ToolsDirectory 'launcher.json') -Raw -Encoding utf8 | ConvertFrom-Json
$excluded = @('AIDA64','HWiNFO','Geek Uninstaller','Thaiphoon')
if (@($config.programs | Where-Object { $_.name -in $excluded }).Count) { throw 'Public package contains an excluded commercial/restricted tool.' }
foreach ($file in Get-ChildItem -LiteralPath $ToolsDirectory -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($ToolsDirectory, $file.FullName)
    if ($relative -match '(^|[\\/])(AIDA64|hwinfo|Geek Uninstaller|Thaiphoon)([\\/]|$)' -or $file.Name -eq 'pkey.txt') { throw "Excluded file: $relative" }
}
foreach ($relative in @($config.programs.exe) + @($config.optimizerExe, $config.tunerExe)) {
    if (!(Test-Path -LiteralPath (Join-Path $ToolsDirectory $relative) -PathType Leaf)) { throw "Missing executable: $relative" }
}
$temporaryZip = Join-Path ([IO.Path]::GetTempPath()) ('aerofics-payload-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $ToolsDirectory -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($ToolsDirectory, $file.FullName).Replace('\','/')
            $level = if ($relative.StartsWith('Extras/')) { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, $level) | Out-Null
        }
    } finally { $archive.Dispose() }
    $plain = [IO.File]::ReadAllBytes($temporaryZip)
    $key = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    $tag = [byte[]]::new(16)
    $cipher = [byte[]]::new($plain.Length)
    $aes = [Security.Cryptography.AesGcm]::new($key, 16)
    try { $aes.Encrypt($nonce, $plain, $cipher, $tag) } finally { $aes.Dispose(); [Array]::Clear($plain) }
    $stream = [IO.File]::Create((Join-Path $PSScriptRoot 'src\payload.enc'))
    try { $stream.Write($nonce); $stream.Write($tag); $stream.Write($cipher) } finally { $stream.Dispose() }
    $keyText = 'namespace AeroficsLauncher; internal static class PayloadKey { internal static readonly byte[] Value = Convert.FromBase64String("' + [Convert]::ToBase64String($key) + '"); }'
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'src\PayloadKey.g.cs'), $keyText, [Text.UTF8Encoding]::new($false))
    Write-Output 'Payload prepared. Executable bytes were preserved.'
} finally { if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip } }
