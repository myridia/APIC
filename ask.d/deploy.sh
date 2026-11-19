#!/bin/sh
# id: 6
# group: APIC
# desc: Deploy - upload apic.exe to the host and sign it

ROOT=$(cd "$(dirname "$0")/.." && pwd)
. "$ROOT/ask.d/lib.sh"
ENV_FILE="$ROOT/.env"

[ -f "$ROOT/apic.exe" ] || {
  printf "  apic.exe missing - run task 1 (Build) first\n"
  exit 1
}
command -v iconv >/dev/null 2>&1 || {
  printf "  iconv is required to sign\n"
  exit 1
}

apic_load_conn "$ENV_FILE"

PFX=${SIGN_PFX:-"C:\Users\$APIC_USER\MySPC.pfx"}
EXE="C:\Users\$APIC_USER\Desktop\apic.exe"
TARGET="$APIC_USER@$APIC_HOST:~/Desktop/"

printf "...Uploading apic.exe to %s\n" "$TARGET"
apic_scp "$ROOT/apic.exe" "$TARGET" || exit 1

PFX_ESC=$(printf '%s' "$PFX" | sed 's/\\/\\\\/g')
EXE_ESC=$(printf '%s' "$EXE" | sed 's/\\/\\\\/g')
PS_SRC=$(sed -e "s|__PFX__|$PFX_ESC|" -e "s|__EXE__|$EXE_ESC|" "$ROOT/ask.d/sign.ps1") || exit 1
ENC=$(printf '%s' "$PS_SRC" | iconv -t UTF-16LE | base64 | tr -d '\n') || exit 1

printf "...Signing apic.exe on %s (PFX: %s)\n" "$APIC_HOST" "$PFX"
apic_ssh "$APIC_USER@$APIC_HOST" powershell.exe -NoProfile -EncodedCommand "$ENC" || exit 1
