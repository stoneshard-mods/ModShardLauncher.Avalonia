param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "publish"
}

$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null

Write-Host "Publishing GUI and CLI into $Output"

dotnet publish (Join-Path $root "src\ModShardLauncher.GUI\ModShardLauncher.GUI.csproj") `
    -c $Configuration -r $Runtime --self-contained true -o $Output

dotnet publish (Join-Path $root "src\ModShardLauncher.CLI\ModShardLauncher.CLI.csproj") `
    -c $Configuration -r $Runtime --self-contained true -o $Output

Write-Host "Done: ModShardLauncher.GUI.exe and ModShardLauncher.CLI.exe"
