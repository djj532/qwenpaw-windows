param([string]$SourceRoot, [string]$OutDir)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Native verification requires actual Windows, not a cross-build' }
$env:PYTHONDONTWRITEBYTECODE='1'
$env:PYTHONPYCACHEPREFIX=Join-Path $env:RUNNER_TEMP 'qa-pycache'
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$out = [IO.Path]::GetFullPath($OutDir)
New-Item -Path $out -ItemType Directory -Force | Out-Null
$os = Get-CimInstance Win32_OperatingSystem
@{os=$os.Caption;version=$os.Version;architecture=$os.OSArchitecture;native_windows=$true} |
  ConvertTo-Json | Set-Content (Join-Path $out 'native-environment.json')
Push-Location $source
try {
  if (-not (Test-Path webui/index.html)) { throw 'Real bundled frontend absent' }
  if (-not (Test-Path e2e/win-native-e2e.ps1)) { throw 'Native UI verification script absent' }
  if (Test-Path webui.zip) { Move-Item webui.zip (Join-Path $env:RUNNER_TEMP 'unused-original-webui.zip') }
  Compress-Archive -Path webui/* -DestinationPath webui.zip
  $unitPath = Join-Path $out 'unit'
  $testProjects = @(Get-ChildItem -Path tests -Filter '*Tests.csproj' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName)
  if ($testProjects.Count -lt 1) { throw 'Real unit test project absent' }
  $unitFailed = $false
  foreach ($testProject in $testProjects) {
    & dotnet test $testProject.FullName -c Release --logger trx --results-directory $unitPath 2>&1 |
      Tee-Object -FilePath (Join-Path $out 'unit-console.log') -Append
    if ($LASTEXITCODE -ne 0) { $unitFailed = $true }
  }
  $unitTotal=0; $unitPassed=0
  foreach($trx in @(Get-ChildItem $unitPath -Filter '*.trx' -Recurse)) {
    [xml]$doc=Get-Content $trx.FullName -Raw
    $unitTotal += [int]$doc.TestRun.ResultSummary.Counters.total
    $unitPassed += [int]$doc.TestRun.ResultSummary.Counters.passed
  }
  if($unitTotal -lt 56 -or $unitPassed -lt 56){$unitFailed=$true}
  @{minimum_preserved_main_tests=56;actual_total=$unitTotal;actual_passed=$unitPassed;unit_tests= $(if ($unitFailed) {'FAIL'} else {'PASS'});continue_independent_native_checks=$true;publication_allowed=$false} |
    ConvertTo-Json | Set-Content (Join-Path $out 'unit-gate-status.json')
  $release = Join-Path $env:RUNNER_TEMP 'qp-release'
  & dotnet publish QwenPaw.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $release 2>&1 |
    Tee-Object -FilePath (Join-Path $out 'publish-console.log')
  if ($LASTEXITCODE -ne 0) { throw 'Native publish failed' }
  $releaseDll = Join-Path $source 'bin/Release/net8.0-windows/win-x64/QwenPaw.dll'
  if (-not (Test-Path $releaseDll)) { throw 'Real published Release assembly absent' }
  $releaseDllSha = (Get-FileHash $releaseDll -Algorithm SHA256).Hash.ToLowerInvariant()
  $exe = Join-Path $release 'QwenPaw.exe'
  if (-not (Test-Path $exe)) { throw 'Published executable absent' }
  $version = (Get-Item $exe).VersionInfo
  $signature = Get-AuthenticodeSignature -LiteralPath $exe
  @{file_version=$version.FileVersion;product_version=$version.ProductVersion;sha256=(Get-FileHash $exe).Hash.ToLowerInvariant();authenticode_status=[string]$signature.Status;self_contained=$true} |
    ConvertTo-Json | Set-Content (Join-Path $out 'executable-metadata.json')
  if ($version.FileVersion -ne '2.0.8.0') { throw 'Wrong main file version' }
  $rollbackSource = Join-Path (Split-Path -Parent $source) 'rollback-source'
  if (-not (Test-Path (Join-Path $rollbackSource 'QwenPaw.csproj'))) { throw 'Functional rollback source absent' }
  $rollbackRelease = Join-Path $env:RUNNER_TEMP 'qp-rollback-release'
  Push-Location $rollbackSource
  try {
    if (Test-Path webui.zip) { Move-Item webui.zip (Join-Path $env:RUNNER_TEMP 'unused-rollback-webui.zip') }
    Compress-Archive -Path webui/* -DestinationPath webui.zip
    & dotnet publish QwenPaw.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $rollbackRelease 2>&1 |
      Tee-Object -FilePath (Join-Path $out 'rollback-publish-console.log')
    if ($LASTEXITCODE -ne 0) { throw 'Functional rollback native publish failed' }
  } finally { Pop-Location }
  $rollbackTests = @(Get-ChildItem -Path (Join-Path $rollbackSource 'tests') -Filter '*Tests.csproj' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName)
  if ($rollbackTests.Count -lt 1) { throw 'Real rollback compatibility test project absent' }
  foreach ($testProject in $rollbackTests) {
    & dotnet test $testProject.FullName -c Release --logger trx --results-directory (Join-Path $out 'rollback-unit') 2>&1 |
      Tee-Object -FilePath (Join-Path $out 'rollback-unit-console.log') -Append
    if ($LASTEXITCODE -ne 0) { $unitFailed = $true }
  }
  $rollbackExe = Join-Path $rollbackRelease 'QwenPaw.exe'
  if ((Get-Item $rollbackExe).VersionInfo.FileVersion -ne '2.0.7.68') { throw 'Wrong rollback file version' }
  $native = Join-Path $out 'native-ui'
  & pwsh -NoProfile -File e2e/win-native-e2e.ps1 -ExePath $exe -OutDir $native -RollbackExePath $rollbackExe -ReleaseDllPath $releaseDll -ReleaseDllSha256 $releaseDllSha 2>&1 |
    Tee-Object -FilePath (Join-Path $out 'native-ui-console.log')
  if ($LASTEXITCODE -ne 0) { throw 'Native WPF/WebView2 validation failed' }
  & pwsh -NoProfile -File e2e/win-stress-e2e.ps1 -SourceRoot $source -OutDir (Join-Path $out 'native-stress') -ReleaseDllSha256 $releaseDllSha 2>&1 |
    Tee-Object -FilePath (Join-Path $out 'native-stress-console.log')
  if ($LASTEXITCODE -ne 0) { throw 'Native pressure gates NOT accepted; baseline results retained' }
  Copy-Item -LiteralPath $exe -Destination (Join-Path $out 'QwenPaw-Windows-x64-v2.0.8-auth.exe')
  Copy-Item -LiteralPath $rollbackExe -Destination (Join-Path $out 'QwenPaw-Windows-x64-v2.0.7-rollback.exe')
  Copy-Item -LiteralPath version.json -Destination (Join-Path $out 'version.json')
  @{unit_tests= $(if ($unitFailed) {'FAIL'} else {'PASS'});native_publish='PASS';native_ui_exit_code=0;completion_utc=[DateTime]::UtcNow.ToString('o');publication_allowed=$false} |
    ConvertTo-Json | Set-Content (Join-Path $out 'ci-stage-result.json')
  if ($unitFailed) { throw 'Unit gate failed; independent native results retained but candidate is NOT accepted' }
} finally {
  Pop-Location
}
