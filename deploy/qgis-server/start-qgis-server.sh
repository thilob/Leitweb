#!/bin/sh
set -eu

umask 077
service_file="${HOME}/.pg_service.conf"
cat > "${service_file}" <<EOF
[leitweb]
host=${PGHOST:-database}
port=${PGPORT:-5432}
dbname=${PGDATABASE:-leitweb}
user=${PGUSER:-leitweb}
password=${PGPASSWORD:?PGPASSWORD muss gesetzt werden}
sslmode=${PGSSLMODE:-prefer}
EOF

export PGSERVICEFILE="${service_file}"
export QGIS_SERVER_LOG_STDERR=1
export QGIS_SERVER_LOG_LEVEL="${QGIS_SERVER_LOG_LEVEL:-1}"
export QGIS_SERVER_FORCE_READONLY_LAYERS=1
export QT_QPA_PLATFORM=offscreen
exec /usr/bin/spawn-fcgi -p 5555 -n -- /usr/lib/cgi-bin/qgis_mapserv.fcgi
