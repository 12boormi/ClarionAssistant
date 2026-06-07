<#
.SYNOPSIS
  Regenerate the bundled "Clarion IDE API" DocGraph library (ticket 8f1dda1c).
  Run this after a Clarion upgrade. Writes ONLY the bundled docgraph.db; never touches the personal DB.

.PARAMETER Bin
  Clarion install bin folder. Default: C:\Clarion12\bin

.PARAMETER Db
  Target bundled DocGraph DB. Default: %APPDATA%\ClarionAssistant\docgraph.db

.PARAMETER BuildStamp
  Clarion build version to stamp (e.g. 12.0.0.14000). If supplied, Targets.cs BuildStamp is updated first.

.PARAMETER NoBackup
  Skip the pre-run DB backup (not recommended).

.EXAMPLE
  ./regenerate.ps1 -BuildStamp 12.0.0.14100
#>
param(
  [string]$Bin = "C:\Clarion12\bin",
  [string]$Db,
  [string]$BuildStamp,
  [switch]$NoBackup
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1. Guard: Clarion must be closed (bundled DB could be locked, and reflection reads bin\ DLLs).
$clarion = Get-Process -ErrorAction SilentlyContinue |
  Where-Object { $_.ProcessName -match 'Clarion|cwide|C12' }
if ($clarion) {
  Write-Error "Clarion appears to be running ($($clarion.ProcessName -join ', ')). Close it and re-run."
}

# 2. Optional: re-stamp the build version in Targets.cs before building.
if ($BuildStamp) {
  $targets = Join-Path $here 'Targets.cs'
  $content = Get-Content $targets -Raw
  $content = [System.Text.RegularExpressions.Regex]::Replace(
    $content, 'public const string BuildStamp = "[^"]*";', "public const string BuildStamp = `"$BuildStamp`";")
  Set-Content $targets $content -NoNewline
  Write-Host "Re-stamped BuildStamp = $BuildStamp"
}

# 3. Resolve the bundled DB path (default = %APPDATA%\ClarionAssistant\docgraph.db).
if (-not $Db) { $Db = Join-Path $env:APPDATA 'ClarionAssistant\docgraph.db' }
Write-Host "Bundled DB: $Db"
Write-Host "Personal DB (NEVER touched): $(Join-Path $env:APPDATA 'ClarionAssistant\personal-docgraph.db')"

# 4. Backup the bundled DB.
if (-not $NoBackup -and (Test-Path $Db)) {
  $bak = "$Db.pre-ideapi-bak"
  Copy-Item $Db $bak -Force
  Write-Host "Backup: $bak"
}

# 5. Build + run the extractor against the bundled DB.
Push-Location $here
try {
  dotnet build -c Release -v q | Out-Null
  dotnet run -c Release -- --bin "$Bin" --db "$Db" --verify
} finally {
  Pop-Location
}

Write-Host "`nDone. The 'Clarion IDE API' library is regenerated in the bundled DB."
Write-Host "Other libraries (CapeSoft, SoftVelocity docs, personal DB) are untouched."
