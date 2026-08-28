#!/bin/sh
set -eu

if ! psql -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='keycloak'" | grep -q 1; then
  psql -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE keycloak OWNER leitweb"
fi
