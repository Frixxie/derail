#!/usr/bin/env bash
# Exercise the analyzer as a NuGet consumer, including automatic discovery.
set -eu
cd "$(dirname "$0")/../.."
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
dotnet pack src/Derail -c Release -p:PackageVersion=0.0.0-analyzer-test -o "$work/feed"
cp tests/analyzer-consumer/{Consumer.csproj,Options.cs} "$work/"
cat > "$work/NuGet.Config" <<'XML'
<configuration><packageSources><add key="local" value="feed" /></packageSources></configuration>
XML
dotnet restore "$work/Consumer.csproj" --configfile "$work/NuGet.Config" --packages "$work/packages"
dotnet build "$work/Consumer.csproj" --no-restore
if dotnet build "$work/Consumer.csproj" --no-restore -p:DefineConstants=INVALID > "$work/errors" 2>&1; then
    echo 'Expected explicitly uninitialized options to fail the build.' >&2
    exit 1
fi
# Check each marked expression, not just the overall failure of the build.
for line in $(awk '/\/\/ DRL001/ {print NR}' "$work/Options.cs"); do
    if ! grep -Eq "Options.cs\($line,[0-9]+\): error DRL001:" "$work/errors"; then
        cat "$work/errors" >&2
        exit 1
    fi
done
echo 'Package consumer: valid cases compiled; all explicit uninitialized cases reported DRL001.'
