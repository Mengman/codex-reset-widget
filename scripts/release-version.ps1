param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Stop'
# Accept version tags, not arbitrary refs or MSBuild arguments. Build metadata is not used in asset names.
$number = '(0|[1-9][0-9]*)'
$identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
if ($Tag -cnotmatch "^v$number\.$number\.$number(?:-$identifier(?:\.$identifier)*)?\z") {
    throw 'Release tags must be vMAJOR.MINOR.PATCH, optionally with a SemVer prerelease such as -rc.1.'
}
$version = $Tag.Substring(1)
foreach ($component in $version.Split('-')[0].Split('.')) {
    $value = 0
    if (-not [int]::TryParse($component, [ref]$value) -or $value -gt 65534) {
        throw 'Version components must fit the .NET assembly version range (0-65534).'
    }
}
Write-Output $version
