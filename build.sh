#!/bin/sh
ROOT=$(cd "$(dirname "$0")" && pwd)
cd "$ROOT" || exit 1
dotnet publish apic/apic.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true || exit 1
cp apic/bin/Release/net10.0-windows/win-x64/publish/apic.exe apic.exe || exit 1
printf "...Copied to %s\n" "$ROOT/apic.exe"
