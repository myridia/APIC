#!/bin/sh
# id: 5
# group: APIC
# desc: Clean - remove bin, obj and apic.exe

ROOT=$(cd "$(dirname "$0")/.." && pwd)

printf "...Removing build output\n"
rm -rf "$ROOT/apic/bin" "$ROOT/apic/obj" "$ROOT/apic.exe"
