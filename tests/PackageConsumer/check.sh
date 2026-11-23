#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 1 || ! -f $1 || ${1##*/} != Derail.*.nupkg ]]; then
    echo 'Usage: bash tests/PackageConsumer/check.sh path/to/Derail.<version>.nupkg' >&2
    exit 2
fi

package=${1##*/}
version=${package#Derail.}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir "$work/feed"
cp "$1" "$work/feed/"

fixture=$(dirname "$0")
cp "$fixture/PackageConsumer.csproj" "$fixture/Program.cs" "$work/"
project="$work/PackageConsumer.csproj"
cat > "$work/NuGet.Config" <<'XML'
<configuration>
  <packageSources><add key="candidate" value="feed" /></packageSources>
</configuration>
XML
export NUGET_PACKAGES="$work/cache"
dotnet restore "$project" --configfile "$work/NuGet.Config" -p:DerailVersion="${version%.nupkg}"
dotnet run --project "$project" -c Release --no-restore -p:DerailVersion="${version%.nupkg}"
