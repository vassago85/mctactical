#!/usr/bin/env pwsh
# Runs the HuntexPos.Api.Tests xUnit suite in a disposable .NET 8 SDK container.
# Fails the script if any test fails so CI / pre-commit hooks can gate on this.
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Write-Host "Running dotnet tests from $root" -ForegroundColor Cyan

docker run --rm `
    -v "${root}:/src" `
    -w /src `
    mcr.microsoft.com/dotnet/sdk:8.0 `
    dotnet test tests/HuntexPos.Api.Tests/HuntexPos.Api.Tests.csproj --nologo --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet test failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "All backend tests passed." -ForegroundColor Green
