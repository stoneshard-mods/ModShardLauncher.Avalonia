param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishDir = "",
    [string]$Output = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
    $Version = [string]($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Cannot determine the product version from Directory.Build.props."
    }
    $Version = $Version.Trim()
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $root "publish"
}
$PublishDir = [IO.Path]::GetFullPath($PublishDir)

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "bundle\ModShardLauncher-Setup-$Version.exe"
}
$Output = [IO.Path]::GetFullPath($Output)

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "publish.ps1") -Configuration $Configuration -Runtime $Runtime -Output $PublishDir
}

$gui = Join-Path $PublishDir "ModShardLauncher.GUI.exe"
$cli = Join-Path $PublishDir "ModShardLauncher.CLI.exe"
if (-not (Test-Path $gui) -or -not (Test-Path $cli)) {
    throw "Publish output is missing the GUI/CLI executables: $PublishDir"
}

$makensisCandidates = @(
    (Get-Command makensis -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
    "$env:ProgramFiles\NSIS\makensis.exe"
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $makensisCandidates) {
    throw "makensis.exe was not found. Install NSIS first (winget install NSIS.NSIS)."
}
$makensis = @($makensisCandidates)[0]

$version4 = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }

New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null

$arguments = @(
    "/DAPP_VERSION=$Version",
    "/DAPP_VERSION4=$version4",
    "/DPUBLISH_DIR=$PublishDir",
    "/DLICENSE_FILE=$(Join-Path $root 'LICENSE')",
    "/DICON_FILE=$(Join-Path $root 'src\ModShardLauncher.GUI\Assets\app.ico')",
    "/DOUTFILE=$Output",
    (Join-Path $PSScriptRoot "ModShardLauncher.nsi")
)

Write-Host "Building installer $Version with $makensis"

# NSIS writes diagnostics to stderr; keep the script from turning them into exceptions
# before we can report the real exit code.
$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $makensis @arguments 2>&1 | ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorAction
}

if ($exitCode -ne 0) {
    throw "makensis failed with exit code $exitCode"
}

if (-not (Test-Path $Output)) {
    throw "Installer was not produced: $Output"
}

Write-Host "Installer: $Output"
