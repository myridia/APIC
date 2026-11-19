#!/bin/sh
# id: 3
# group: APIC
# desc: Restore dependencies

ROOT=$(cd "$(dirname "$0")/.." && pwd)

printf "...Restoring dependencies\n"
dotnet restore "$ROOT/apic/apic.csproj" || exit 1
