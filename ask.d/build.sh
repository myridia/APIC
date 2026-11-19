#!/bin/sh
# id: 1
# group: APIC
# desc: Build - publish APIC (win-x64, single file)

ROOT=$(cd "$(dirname "$0")/.." && pwd)

printf "...Publishing APIC (win-x64)\n"
dotnet publish "$ROOT/apic/apic.csproj" -c Release -r win-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true || exit 1
