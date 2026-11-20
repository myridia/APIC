#!/bin/sh

APIC_ASKPASS=$(cd "$(dirname "$0")" && pwd)/askpass

apic_read_env() {
  sed -n "s/^$2=//p" "$1" 2>/dev/null | head -n1 | tr -d '\r'
}

apic_load_conn() {
  env_file=$1
  APIC_HOST=$(apic_read_env "$env_file" APIC_HOST)
  APIC_USER=$(apic_read_env "$env_file" APIC_USER)
  SSHPASS=$(apic_read_env "$env_file" SSHPASS)

  changed=0
  if [ -z "$APIC_HOST" ]; then
    printf "  Host/IP for the QuickBooks machine (example 192.168.0.10): "
    IFS= read -r APIC_HOST || exit 1
    changed=1
  fi
  if [ -z "$APIC_USER" ]; then
    printf "  SSH user on %s: " "$APIC_HOST"
    IFS= read -r APIC_USER || exit 1
    changed=1
  fi
  if [ -z "$SSHPASS" ]; then
    printf "  SSH password for %s@%s: " "$APIC_USER" "$APIC_HOST"
    if [ -t 0 ]; then
      stty -echo 2>/dev/null
      IFS= read -r SSHPASS || exit 1
      stty echo 2>/dev/null
    else
      IFS= read -r SSHPASS || exit 1
    fi
    printf "\n"
    changed=1
  fi
  if [ "$changed" = "1" ]; then
    {
      printf 'APIC_HOST=%s\n' "$APIC_HOST"
      printf 'APIC_USER=%s\n' "$APIC_USER"
      printf 'SSHPASS=%s\n' "$SSHPASS"
    } > "$env_file" || exit 1
    chmod 600 "$env_file"
    printf "  Saved to %s (gitignored)\n" "$env_file"
  fi
  export APIC_HOST APIC_USER SSHPASS
}

apic_ssh() {
  if command -v sshpass >/dev/null 2>&1; then
    sshpass -e ssh "$@"
  else
    SSH_ASKPASS="$APIC_ASKPASS" SSH_ASKPASS_REQUIRE=force ssh "$@"
  fi
}

apic_scp() {
  if command -v sshpass >/dev/null 2>&1; then
    sshpass -e scp "$@"
  else
    SSH_ASKPASS="$APIC_ASKPASS" SSH_ASKPASS_REQUIRE=force scp "$@"
  fi
}
