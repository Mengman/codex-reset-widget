param([Parameter(Mandatory)][string]$ArchivePath, [switch]$RunDesktopChecks, [switch]$RunLiveChecks,
    [string]$UpgradeDataDirectory)
$ErrorActionPreference = 'Stop'
if ($UpgradeDataDirectory -and -not $RunLiveChecks) { throw 'Upgrade checks require -RunLiveChecks.' }
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$verificationRoot = Join-Path ([IO.Path]::GetDirectoryName($archive)) ('Package check ' + [char]0x6D4B + [char]0x8BD5 + ' ' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $verificationRoot 'app'
New-Item -ItemType Directory -Path $verificationRoot | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Assert-Check([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }; $checks.Add($Message); Write-Output "PASS $Message"
}
function Run-CheckProcess([string]$ReportName, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $packageRoot 'CodexResetWidget.exe'))
    $start.UseShellExecute = $false; $start.WindowStyle = 'Hidden'; $start.WorkingDirectory = $verificationRoot
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Extracted application checks timed out.' }
        if ($process.ExitCode -ne 0) { throw "Extracted application failed ($($process.ExitCode)): $ReportName" }
        if (-not (Test-Path -LiteralPath $ReportName)) { throw "Report missing: $ReportName" }
    } finally { $process.Dispose() }
}
function Check-PackageFiles {
    foreach ($file in $manifest.Files) {
        if ([IO.Path]::IsPathRooted($file.Path) -or $file.Path.Split('/') -contains '..') { throw 'Unsafe manifest path' }
        $path = Join-Path $packageRoot $file.Path
        if (-not ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -eq $file.Length -and
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $file.Sha256)) { throw "Package file differs from manifest: $($file.Path)" }
    }
    $allFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File)
    Assert-Check ($allFiles.Count -eq $manifest.Files.Count + 1) 'No unexpected files in extracted application'
    Assert-Check $true "Verified SHA256 and size for $($manifest.Files.Count) package files"
}
$failure = $null; $desktopReport = $null; $liveReport = $null; $upgradeReport = $null
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $packageRoot
    $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'RELEASE.json') -Raw | ConvertFrom-Json
    Assert-Check ($manifest.SelfContained -and $manifest.RuntimeIdentifier -eq 'win-x64') 'Release manifest targets self-contained Windows x64'
    $config = Get-Content -LiteralPath (Join-Path $packageRoot 'CodexResetWidget.runtimeconfig.json') -Raw | ConvertFrom-Json
    Assert-Check (-not $config.runtimeOptions.framework -and -not $config.runtimeOptions.frameworks -and
        @($config.runtimeOptions.includedFrameworks).Count -eq 2) 'Runtime configuration includes both runtime frameworks'
    foreach ($file in @('CodexResetWidget.exe', 'CodexResetWidget.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
        'PresentationFramework.dll', 'README.txt', 'README.zh-CN.txt', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
        Assert-Check (Test-Path -LiteralPath (Join-Path $packageRoot $file)) "Required release file: $file"
    }
    Assert-Check (((Get-Item -LiteralPath (Join-Path $packageRoot 'CodexResetWidget.exe')).VersionInfo.ProductVersion.Split('+')[0]) -eq $manifest.Version) 'EXE version matches release manifest'
    foreach ($framework in $config.runtimeOptions.includedFrameworks) {
        $licenseFiles = @(Get-ChildItem -LiteralPath (Join-Path $packageRoot ('ThirdParty/' + $framework.name)) -File)
        Assert-Check (@($licenseFiles | Where-Object { $_.Name -match '^LICENSE' }).Count -gt 0) "Runtime license included: $($framework.name)"
        if ($framework.name -eq 'Microsoft.NETCore.App') {
            Assert-Check (@($licenseFiles | Where-Object { $_.Name -match '^THIRD-PARTY-NOTICES' }).Count -gt 0) 'NETCore third-party notices included'
        }
    }
    Assert-Check (-not (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Name -match '\.pdb$|^(settings|snapshot)\.json$|\.log$' })) 'Package contains no symbols or user data'
    Check-PackageFiles
    if ($RunDesktopChecks) {
        $directory = Join-Path $verificationRoot 'desktop-checks'
        $desktopReport = Join-Path $directory 'desktop-checks.json'
        Run-CheckProcess $desktopReport @('--desktop-check-dir', $directory)
        $desktop = Get-Content -LiteralPath $desktopReport -Raw | ConvertFrom-Json
        Assert-Check $desktop.Passed 'Extracted EXE passes desktop checks from a directory with spaces and Chinese characters'
        Assert-Check ([IO.Path]::GetFullPath($desktop.RuntimeDirectory).TrimEnd('\') -eq $packageRoot.TrimEnd('\')) 'Extracted EXE loads its bundled runtime'
    }
    if ($RunLiveChecks) {
        $directory = Join-Path $verificationRoot 'live-checks'
        $dataDirectory = Join-Path $verificationRoot 'fresh-user-data'
        $liveReport = Join-Path $directory 'live-checks.json'
        Run-CheckProcess $liveReport @('--live-capture-dir', $directory, '--settings-dir', $dataDirectory, '--cache-dir', (Join-Path $dataDirectory 'cache'))
        $live = Get-Content -LiteralPath $liveReport -Raw | ConvertFrom-Json
        Assert-Check (-not $live.IsDemo -and $live.StatusLoaded -and $live.HistoryCount -gt 0 -and $live.ReadingPreservedAfterRefresh) 'Fresh extracted application uses real data and preserves reading after refresh'
        Assert-Check ($live.Startup.Compact -and -not $live.Startup.Pinned -and $live.Startup.Theme -eq 'System' -and $live.Startup.Language -eq 'System') 'Fresh startup uses approved defaults'
        Assert-Check ([IO.Path]::GetFullPath($live.RuntimeDirectory).TrimEnd('\') -eq $packageRoot.TrimEnd('\')) 'Live startup uses the bundled runtime'
        Assert-Check (Test-Path -LiteralPath (Join-Path $dataDirectory 'cache/snapshot.json')) 'Live startup writes cache outside application directory'
        if ($UpgradeDataDirectory) {
            $source = (Resolve-Path -LiteralPath $UpgradeDataDirectory).Path
            $upgradeData = Join-Path $verificationRoot 'upgraded-user-data'
            New-Item -ItemType Directory -Path (Join-Path $upgradeData 'cache') -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $source 'settings.json') -Destination $upgradeData
            Copy-Item -LiteralPath (Join-Path $source 'cache/snapshot.json') -Destination (Join-Path $upgradeData 'cache')
            $expected = Get-Content -LiteralPath (Join-Path $upgradeData 'settings.json') -Raw | ConvertFrom-Json
            $directory = Join-Path $verificationRoot 'upgrade-checks'; $upgradeReport = Join-Path $directory 'live-checks.json'
            Run-CheckProcess $upgradeReport @('--live-capture-dir', $directory, '--settings-dir', $upgradeData, '--cache-dir', (Join-Path $upgradeData 'cache'))
            $upgrade = Get-Content -LiteralPath $upgradeReport -Raw | ConvertFrom-Json
            Assert-Check ($upgrade.Startup.Compact -eq $expected.compact -and $upgrade.Startup.Pinned -eq $expected.pinned -and
                $upgrade.Startup.Theme -eq $expected.theme -and [Math]::Abs($upgrade.Startup.Width - $expected.width) -lt 1) 'Schema-1 settings preserve saved mode pin theme and width on upgrade'
            Assert-Check ($null -eq $upgrade.CacheWarning -and $upgrade.StatusLoaded -and $upgrade.HistoryCount -gt 0) 'Schema-1 cache remains compatible during upgrade'
            Assert-Check ($upgrade.Startup.Language -eq 'System') 'Older settings without language default to the OS display language'
        }
    }
    # Compare hashes again to catch writes into the install directory during application checks.
    Check-PackageFiles
} catch { $failure = $_.Exception.Message }
$reportPath = Join-Path $verificationRoot 'package-checks.json'
[ordered]@{ Passed = $null -eq $failure; Error = $failure; Checks = @($checks.ToArray());
    Archive = $archive; Sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;
    ExtractedDirectory = $packageRoot; DesktopReport = $desktopReport; LiveReport = $liveReport; UpgradeReport = $upgradeReport;
    CleanWindowsWithoutDotNet = 'Not verified; current machine is a development environment.' } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "Package report: $reportPath"
if ($failure) { throw $failure }
