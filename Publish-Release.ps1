param(
    [string]$Repository = 'felayerlatos/aerofics-tool',
    [string]$Tag = 'v2026.11.15',
    [string]$AssetDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI from https://cli.github.com/ and sign in with gh auth login first.' }
gh auth status
if ($LASTEXITCODE -ne 0) { throw 'Sign in with gh auth login first.' }
$assets = @(
    (Join-Path $AssetDirectory "AeroficsToolbox-$Tag-win-x64.exe"),
    (Join-Path $AssetDirectory "AeroficsToolbox-$Tag-source.zip"),
    (Join-Path $AssetDirectory "AeroficsToolbox-$Tag-build-inputs.zip")
)
foreach ($asset in $assets) { if (!(Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Missing release artifact: $asset" } }
gh release create $Tag @assets --repo $Repository --target main --title "Aerofics 工具箱 $Tag" --notes-file (Join-Path $PSScriptRoot 'docs\release-notes.md')
if ($LASTEXITCODE -ne 0) { throw 'GitHub release creation/upload failed; inspect the release page before retrying.' }
