#!/bin/sh
# id: 2
# group: APIC
# desc: Run - publish and start apic.exe

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT" || exit 1

printf "...Publishing APIC (win-x64)\n"
dotnet publish apic/apic.csproj -c Release -r win-x64 /p:PublishSingleFile=true || exit 1
cp apic/bin/Release/net10.0-windows/win-x64/publish/apic.exe apic.exe || exit 1
printf "...Starting %s\n" "$ROOT/apic.exe"
./apic.exe
