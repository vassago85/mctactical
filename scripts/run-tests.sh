#!/usr/bin/env bash
# Runs the HuntexPos.Api.Tests xUnit suite in a disposable .NET 8 SDK container.
set -euo pipefail

ROOT="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )/.." &> /dev/null && pwd )"
echo "Running dotnet tests from $ROOT"

docker run --rm \
    -v "$ROOT":/src \
    -w /src \
    mcr.microsoft.com/dotnet/sdk:8.0 \
    dotnet test tests/HuntexPos.Api.Tests/HuntexPos.Api.Tests.csproj --nologo --verbosity minimal

echo "All backend tests passed."
