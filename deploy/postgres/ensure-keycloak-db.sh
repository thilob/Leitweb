#!/bin/sh
set -eu

while :; do
  if psql -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='keycloak'" 2>/dev/null | grep -q 1; then
    exit 0
  fi

  if psql -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE keycloak OWNER leitweb"; then
    exit 0
  fi

  echo "PostgreSQL ist noch nicht dauerhaft bereit; neuer Versuch in 2 Sekunden ..." >&2
  sleep 2
done
