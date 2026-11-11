param([string]$SourceRoot, [string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent ([IO.Path]::GetFullPath($SourceRoot))
$privateVerifier = Join-Path $root 'ci-private-verify.ps1'
if (-not (Test-Path -LiteralPath $privateVerifier)) { throw 'Authenticated native verification entrypoint absent' }
& $privateVerifier -SourceRoot $SourceRoot -OutDir $OutDir
