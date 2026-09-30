#!/usr/bin/env bash
# Prints the inventory of hardcoded player-visible English in Scripts/.
# The scanners are Tests/Localization/HardcodedTextScanner.cs (output calls) and
# Tests/Localization/DataTextScanner.cs (data tables, Electron payloads, shown throws);
# this runs their Inventory tests.
# Usage: Tests/Localization/loc-scan.sh [--write-baseline] [--write-data-baseline] [out-dir]
#   Writes inventory.md and rollup.md (output calls) and inventory-data.md and rollup-data.md
#   (the rest) to out-dir (default: a new temp folder) and prints both rollups.
#   --write-baseline rewrites Tests/Localization/hardcoded-baseline.json;
#   --write-data-baseline rewrites Tests/Localization/hardcoded-data-baseline.json.
set -euo pipefail
write_baseline=0
write_data_baseline=0
while [ $# -gt 0 ]; do
    case "$1" in
        --write-baseline) write_baseline=1; shift ;;
        --write-data-baseline) write_data_baseline=1; shift ;;
        *) break ;;
    esac
done
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:-$(mktemp -d)}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
dotnet build "$root/Tests/Tests.csproj" -c Release -v q -nologo
LOC_SCAN_WRITE_BASELINE="$write_baseline" LOC_SCAN_WRITE_DATA_BASELINE="$write_data_baseline" LOC_SCAN_OUT="$out" \
    dotnet test "$root/Tests/Tests.csproj" -c Release --no-build -nologo \
    --filter "FullyQualifiedName=UsurperReborn.Tests.Localization.HardcodedTextScannerTests.Inventory|FullyQualifiedName=UsurperReborn.Tests.Localization.DataTextScannerTests.Inventory"
cat "$out/rollup.md"
cat "$out/rollup-data.md"
echo "Every site: $out/inventory.md and $out/inventory-data.md"
