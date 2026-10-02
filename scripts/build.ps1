param([switch]$Publish, [switch]$RunUiChecks, [switch]$RunDesktopChecks)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $projectRoot
$dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) { throw 'Install the .NET SDK version specified in global.json, or install it under .tools/dotnet.' }
    $dotnetPath = $dotnetCommand.Source
}
# Keep SDK and NuGet state inside the workspace; these variables affect this process only.
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:APPDATA = Join-Path $projectRoot '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $projectRoot '.tools\localappdata'
New-Item -ItemType Directory -Force $env:APPDATA,$env:LOCALAPPDATA | Out-Null
$restoreArguments = @('--configfile', (Join-Path $projectRoot 'NuGet.Config'))
$localFeed = Join-Path $projectRoot '.tools\nuget-feed'
if (Test-Path -LiteralPath $localFeed) {
    # Optional offline feed used by this workspace; normal SDK installs can use nuget.org.
    $restoreArguments += @('--source', $localFeed, '-p:NuGetAudit=false')
}
& $dotnetPath restore CodexResetWidget.sln @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
& $dotnetPath build CodexResetWidget.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$milestoneRoot = Join-Path $projectRoot 'artifacts\milestones\m3'
New-Item -ItemType Directory -Force $milestoneRoot | Out-Null
& $dotnetPath tests\CodexResetWidget.Tests\bin\Release\net10.0\CodexResetWidget.Tests.dll |
    Tee-Object -FilePath (Join-Path $milestoneRoot 'domain-tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Domain tests failed.' }
if ($Publish -or $RunUiChecks -or $RunDesktopChecks) {
    $portableDirectory = Join-Path $milestoneRoot 'portable-final'
    & $dotnetPath restore src\CodexResetWidget\CodexResetWidget.csproj -r win-x64 -p:SelfContained=true @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'Runtime restore failed.' }
    & $dotnetPath publish src\CodexResetWidget\CodexResetWidget.csproj -c Release -r win-x64 --self-contained true -o $portableDirectory --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $portableDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\milestones\m3-usage.txt') -Destination (Join-Path $portableDirectory 'README.txt') -Force
    $runtimeManifest = Get-Content -LiteralPath (Join-Path $portableDirectory 'CodexResetWidget.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeManifest.runtimeOptions.includedFrameworks) {
        $runtimePackageId = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $runtimePackageDirectory = Join-Path $env:NUGET_PACKAGES ($runtimePackageId + '\' + $framework.version)
        $licenseDirectory = Join-Path $portableDirectory ('ThirdParty\' + $framework.name)
        New-Item -ItemType Directory -Force $licenseDirectory | Out-Null
        $licenseFiles = @(Get-ChildItem -LiteralPath $runtimePackageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)' })
        if ($licenseFiles.Count -eq 0) { throw "Runtime license missing: $runtimePackageId" }
        foreach ($licenseFile in $licenseFiles) { Copy-Item -LiteralPath $licenseFile.FullName -Destination $licenseDirectory -Force }
    }
    $archivePath = Join-Path $milestoneRoot 'CodexResetWidget-0.3.0-m3-win-x64.zip'
    Compress-Archive -Path (Join-Path $portableDirectory '*') -DestinationPath $archivePath -Force
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256 | Format-List
}
if ($RunUiChecks) {
    $captureDirectory = Join-Path $milestoneRoot 'captures'
    $checkProcess = Start-Process -FilePath (Join-Path $portableDirectory 'CodexResetWidget.exe') -WindowStyle Hidden -PassThru -ArgumentList @('--demo', '--capture-dir', ('"' + $captureDirectory + '"'))
    if (-not $checkProcess.WaitForExit(45000)) { throw 'WPF checks timed out; inspect the check window and logs.' }
    $reportPath = Join-Path $captureDirectory 'ui-checks.json'
    if (-not (Test-Path -LiteralPath $reportPath)) { throw 'WPF check report was not produced.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.Passed -or $checkProcess.ExitCode -ne 0) { throw "WPF checks failed. See $reportPath" }
    Write-Output "WPF checks passed: $($report.Checks.Count). Captures: $captureDirectory"
}
if ($RunDesktopChecks) {
    $desktopDirectory = Join-Path $milestoneRoot 'desktop-captures'
    $checkProcess = Start-Process -FilePath (Join-Path $portableDirectory 'CodexResetWidget.exe') -WindowStyle Hidden -PassThru -ArgumentList @('--desktop-check-dir', ('"' + $desktopDirectory + '"'))
    if (-not $checkProcess.WaitForExit(45000)) { throw 'Desktop checks timed out.' }
    $reportPath = Join-Path $desktopDirectory 'desktop-checks.json'
    if (-not (Test-Path -LiteralPath $reportPath)) { throw 'Desktop check report was not produced.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.Passed -or $checkProcess.ExitCode -ne 0) { throw "Desktop checks failed. See $reportPath" }
    Write-Output "Desktop checks passed: $($report.Checks.Count). Captures: $desktopDirectory"
}
