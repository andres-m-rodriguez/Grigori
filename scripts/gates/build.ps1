# Conductor gate: the build passes when no compiler or MSBuild errors exist.
# Agents run this instead of reading raw `dotnet build` output — it exits 1 on any real
# error and prints only the error lines, so a failing gate is legible in one screen.
#
# MSB3027/MSB3021 output-copy locks from a running Grigori.Server.exe are expected on this
# machine and do not fail the gate: the compile already succeeded, only the copy to bin/ lost
# a race with the process holding the old exe. Restart the server to clear it.

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Continue'

$solution = Join-Path $PSScriptRoot '..\..\Grigori.slnx' | Resolve-Path

$output = & dotnet build $solution --configuration $Configuration --nologo 2>&1 | Out-String

$errors = $output -split "`r?`n" |
    Where-Object { $_ -match ': error (CS|MSB|NETSDK|NU)\d+' } |
    Where-Object { $_ -notmatch ': error MSB(3027|3021)' } |
    Select-Object -Unique

$locked = $output -split "`r?`n" | Where-Object { $_ -match ': error MSB(3027|3021)' }
if ($locked) {
    Write-Host "note: output copy blocked by a running Grigori.Server.exe — compile succeeded, bin/ is stale" -ForegroundColor Yellow
}

if ($errors.Count -gt 0) {
    Write-Host "BUILD FAILED — $($errors.Count) error(s)" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_ }
    exit 1
}

Write-Host "BUILD PASSED ($Configuration)" -ForegroundColor Green
exit 0
