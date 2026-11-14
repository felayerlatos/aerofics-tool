param([Parameter(Mandatory)][string]$OutputRoot, [string]$SevenZipRoot = 'C:\Program Files\7-Zip')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($name in @('exe','dll')) {
    $path = Join-Path $SevenZipRoot "7z.$name"
    $bytes = [IO.File]::ReadAllBytes($path)
    $pe = [BitConverter]::ToInt32($bytes,60)
    if ([BitConverter]::ToUInt16($bytes,$pe+4) -ne 0x8664) { throw "64-bit 7-Zip required: $path" }
    $compressed = [IO.File]::Create((Join-Path $OutputRoot "extractor.$name.deflate"))
    try {
        $stream = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionLevel]::Optimal, $true)
        try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
    } finally { $compressed.Dispose() }
}
$exe = Join-Path $OutputRoot 'AeroficsOptimizer-2.0.0.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32manifest:$(Join-Path $PSScriptRoot 'Launcher\app.manifest')" "/win32icon:$(Join-Path $PSScriptRoot 'App\app.ico')" "/resource:$(Join-Path $OutputRoot 'runtime.7z'),Runtime.7z" "/resource:$(Join-Path $OutputRoot 'extractor.exe.deflate'),Extractor.exe.deflate" "/resource:$(Join-Path $OutputRoot 'extractor.dll.deflate'),Extractor.dll.deflate" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'Launcher\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Get-Item -LiteralPath $exe | Select-Object FullName,Length
