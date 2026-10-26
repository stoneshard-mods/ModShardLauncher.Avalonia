<#
.SYNOPSIS
    Runs the CLI patch N times on the same input and reports the median stage timings.

.DESCRIPTION
    The patch pipeline is dominated by one-off costs (the first cold decompilation of a big script,
    saved 160 MB data files), so a single run is not a measurement: this script builds once, patches
    N times, checks that every run produced the same bytes, and prints the median per stage.

.PARAMETER Data
    Vanilla data.win (usually `original.win`).

.PARAMETER Runs
    Number of patch runs (default 3). Odd numbers give a true median.

.PARAMETER Enable
    Mod selection: `-Enable all` (default) or a comma separated list of mod names.

.PARAMETER Mods
    Optional mods directory; defaults to <data-dir>/Mods.

.PARAMETER OutputDirectory
    Where the patched files and timing JSON files go (default: %TEMP%\msl-measure-<timestamp>).

.EXAMPLE
    .\scripts\measure-patch.ps1 -Data "C:\...\Stoneshard\original.win" -Runs 3
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Data,

    [ValidateRange(1, 20)]
    [int] $Runs = 3,

    [string] $Enable = 'all',

    [string] $Mods,

    [string] $OutputDirectory,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/ModShardLauncher.CLI/ModShardLauncher.CLI.csproj'

if (-not (Test-Path -LiteralPath $Data)) {
    throw "Vanilla data file not found: $Data"
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("msl-measure-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host "Building $Configuration ..." -ForegroundColor Cyan
& dotnet build $project -c $Configuration --nologo -v quiet | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$cli = Join-Path $root "src/ModShardLauncher.CLI/bin/$Configuration/net10.0/ModShardLauncher.CLI.dll"
if (-not (Test-Path -LiteralPath $cli)) {
    throw "CLI assembly not found: $cli"
}

$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $output = Join-Path $OutputDirectory "patched-$i.win"
    $timings = Join-Path $OutputDirectory "timings-$i.json"

    $arguments = @($cli, 'patch', '--data', $Data, '--output', $output, '--timings', $timings)
    if ($Enable -eq 'all') {
        $arguments += '--all'
    }
    else {
        $arguments += @('--enable', $Enable)
    }

    if ($Mods) {
        $arguments += @('--mods', $Mods)
    }

    Write-Host "[$i/$Runs] patching $([System.IO.Path]::GetFileName($Data)) ..." -ForegroundColor Cyan
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet @arguments | Out-Host
    $stopwatch.Stop()

    if ($LASTEXITCODE -ne 0) {
        throw "Patch run $i failed with exit code $LASTEXITCODE"
    }

    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    $json = Get-Content -LiteralPath $timings -Raw | ConvertFrom-Json

    $stages = @{}
    foreach ($stage in $json.stages) {
        $stages[$stage.stage] = [double]$stage.totalMs
    }

    $results += [pscustomobject]@{
        Run         = $i
        WallSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 1)
        TotalMs     = [double]$json.totalMs
        Sha256      = $hash
        Stages      = $stages
    }
}

# --- report ------------------------------------------------------------------------------------
$distinctHashes = @($results | Select-Object -ExpandProperty Sha256 | Sort-Object -Unique)
Write-Host ''
Write-Host "Output SHA-256: $($distinctHashes -join ', ')"
if ($distinctHashes.Count -ne 1) {
    Write-Host 'FAIL: the runs did not produce the same bytes; the timing comparison is meaningless.' -ForegroundColor Red
    exit 1
}
Write-Host "Bytes identical across $Runs run(s)." -ForegroundColor Green

function Get-Median {
    param([double[]] $Values)
    if ($Values.Count -eq 0) { return 0 }
    $sorted = $Values | Sort-Object
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) {
        return [double]$sorted[$middle]
    }

    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}

$stageNames = @($results | ForEach-Object { $_.Stages.Keys } | Sort-Object -Unique)
$rows = foreach ($name in $stageNames) {
    $values = @($results | ForEach-Object { if ($_.Stages.ContainsKey($name)) { [double]$_.Stages[$name] } else { 0 } })
    [pscustomobject]@{
        Stage     = $name
        MedianMs  = [math]::Round((Get-Median -Values $values), 1)
        MinMs     = [math]::Round(($values | Measure-Object -Minimum).Minimum, 1)
        MaxMs     = [math]::Round(($values | Measure-Object -Maximum).Maximum, 1)
    }
}

Write-Host ''
Write-Host 'Median stage timings (load + patch + save = totalMs)' -ForegroundColor Cyan
$rows | Sort-Object -Property MedianMs -Descending |
    Format-Table -AutoSize @{ L = 'Stage'; E = { $_.Stage } },
                        @{ L = 'Median'; E = { '{0,8:N1} ms  ({1:N1} s)' -f $_.MedianMs, ($_.MedianMs / 1000) } },
                        @{ L = 'Min'; E = { '{0:N1} ms' -f $_.MinMs } },
                        @{ L = 'Max'; E = { '{0:N1} ms' -f $_.MaxMs } } |
    Out-Host

Write-Host ''
Write-Host ("Wall clock per run: " + (($results | ForEach-Object { "$($_.WallSeconds)s" }) -join ' / '))
Write-Host ("Timing files: $OutputDirectory")
