#!/bin/sh
# id: 2
# group: APIC
# desc: Run - publish and start apic.exe

ROOT=$(cd "$(dirname "$0")/.." && pwd)

printf "...Publishing APIC (win-x64)\n"
dotnet publish "$ROOT/apic/apic.csproj" -c Release -r win-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true || exit 1
printf "...Starting apic.exe\n"
"$ROOT/apic/bin/Release/net10.0/win-x64/apic.exe"
