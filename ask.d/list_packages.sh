#!/bin/sh
# id: 4
# group: APIC
# desc: List packages with versions

ROOT=$(cd "$(dirname "$0")/.." && pwd)

printf "...Listing packages\n"
dotnet list "$ROOT/apic/apic.csproj" package || exit 1
