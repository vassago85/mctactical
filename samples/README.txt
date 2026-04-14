Huntex 2026 sample data
-----------------------
- huntex2026.xlsx — copied from the author’s OneDrive workbook (source of truth).
- huntex2026.csv — exported from that workbook (same columns; UTF-8 with BOM).

Regenerate the CSV after Excel changes:
  docker run --rm -v "<repo>:/work" -w /work mcr.microsoft.com/dotnet/sdk:8.0 \
    dotnet run --project tools/ExportHuntexCsv/ExportHuntexCsv.csproj -- \
    samples/huntex2026.xlsx samples/huntex2026.csv "huntex 2026"

Or from repo root with local dotnet:
  dotnet run --project tools/ExportHuntexCsv -- samples/huntex2026.xlsx samples/huntex2026.csv "huntex 2026"

Import via web UI (Import page) or scripts/import-huntex.ps1 -FilePath ...
