#!/usr/bin/env bash
# Prints the inventory of hardcoded player-visible English in Scripts/.
# The scanner is Tests/Localization/HardcodedTextScanner.cs; this runs its Inventory test.
# Usage: Tests/Localization/loc-scan.sh [--write-baseline] [out-dir]
#   Writes inventory.md (every site) and rollup.md (per system) to out-dir (default: a new temp folder)
#   and prints the rollup. --write-baseline also rewrites Tests/Localization/hardcoded-baseline.json.
set -euo pipefail
write_baseline=0
if [ "${1:-}" = "--write-baseline" ]; then write_baseline=1; shift; fi
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:-$(mktemp -d)}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
dotnet build "$root/Tests/Tests.csproj" -c Release -v q -nologo
LOC_SCAN_WRITE_BASELINE="$write_baseline" LOC_SCAN_OUT="$out" dotnet test "$root/Tests/Tests.csproj" -c Release --no-build -nologo \
    --filter "FullyQualifiedName=UsurperReborn.Tests.Localization.HardcodedTextScannerTests.Inventory"
cat "$out/rollup.md"
echo "Every site: $out/inventory.md"
