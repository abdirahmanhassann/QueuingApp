#!/bin/bash
# Deploys Line (QueueingApp + QueuingAppFE) next to AccessControl. Runs on the server as root (via SSM).
# Usage: setup.sh <bucket> <app_host> <api_host>
# Safe to re-run for every new version. AccessControl is not rebuilt; only its Caddy gets the Line sites.
set -euo pipefail
BUCKET="$1"; APP_HOST="$2"; API_HOST="$3"
ROOT=/opt/line; AC=/opt/ac
log() { echo "== $*"; }

[ -f "$AC/docker-compose.yml" ] || { echo "AccessControl not found in $AC"; exit 1; }
mkdir -p "$ROOT" && cd "$ROOT"

log "Fetching code"
aws s3 cp "s3://$BUCKET/line-src.tgz" /tmp/line-src.tgz
rm -rf QueueingApp QueuingAppFE && tar -xzf /tmp/line-src.tgz -C "$ROOT"
cp QueueingApp/deploy/docker-compose.yml "$ROOT/docker-compose.yml"
cp QueueingApp/deploy/api.Dockerfile QueueingApp/Dockerfile
cp QueueingApp/deploy/web.Dockerfile QueuingAppFE/Dockerfile

# Settings kept between deploys
AC_NETWORK=$(docker inspect "$(cd $AC && docker compose ps -q caddy)" \
  --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}' | awk '{print $1}')
[ -n "$AC_NETWORK" ] || { echo "Couldn't find AccessControl's Docker network"; exit 1; }
[ -f .env ] || echo "LINE_DB_PASSWORD=Ln$(openssl rand -hex 16)A1" > .env
grep -v '^\(APP_HOST\|API_HOST\|AC_NETWORK\)=' .env > .env.tmp || true
{ cat .env.tmp; echo "APP_HOST=$APP_HOST"; echo "API_HOST=$API_HOST"; echo "AC_NETWORK=$AC_NETWORK"; } > .env
rm -f .env.tmp; chmod 600 .env
set -a; . ./.env; set +a

log "Twilio settings from launchSettings.json (https profile)"
python3 - <<'PY'
import json,re
t=open('/opt/line/QueueingApp/QueueingApp/Properties/launchSettings.json',encoding='utf-8-sig').read()
t=re.sub(r'^\s*//.*$','',t,flags=re.M)
env=json.loads(t)['profiles']['https']['environmentVariables']
with open('/opt/line/api.env','w') as f:
    for k,v in env.items():
        if k.startswith('TWILIO_'): f.write(f'{k}={v}\n')
PY
chmod 600 api.env

log "Database QUEUINGAPP on AccessControl's SQL Server (own login)"
SA=$(grep '^SA_PASSWORD=' "$AC/.env" | cut -d= -f2-)
DB=$(cd $AC && docker compose ps -q db)
sql() { docker exec -i "$DB" /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SA" "$@"; }
sql -Q "IF DB_ID('QUEUINGAPP') IS NULL BEGIN CREATE DATABASE QUEUINGAPP; ALTER DATABASE QUEUINGAPP SET RECOVERY SIMPLE; END;
IF SUSER_ID('line_app') IS NULL CREATE LOGIN line_app WITH PASSWORD = '$LINE_DB_PASSWORD', DEFAULT_DATABASE = QUEUINGAPP;
ELSE ALTER LOGIN line_app WITH PASSWORD = '$LINE_DB_PASSWORD';"
sql -d QUEUINGAPP -Q "IF USER_ID('line_app') IS NULL CREATE USER line_app FOR LOGIN line_app; ALTER ROLE db_owner ADD MEMBER line_app;"
C=QueueingApp/QueueingApp/Connectivity
HAS=$(sql -d QUEUINGAPP -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN OBJECT_ID('dbo.Users') IS NULL THEN 0 ELSE 1 END" | tr -d '[:space:]')
if [ "$HAS" = "0" ]; then
  log "Base schema (first deploy only)"
  docker cp "$C/DBSCRIPT.txt" "$DB:/tmp/line-base.sql"; sql -d QUEUINGAPP -i /tmp/line-base.sql
fi
log "Migrations"
docker cp "$C/MIGRATIONS.sql" "$DB:/tmp/line-migrations.sql"; sql -d QUEUINGAPP -i /tmp/line-migrations.sql
if [ -f "$C/STRIPE_KEYS.local.sql" ]; then
  docker cp "$C/STRIPE_KEYS.local.sql" "$DB:/tmp/line-stripe.sql"; sql -d QUEUINGAPP -i /tmp/line-stripe.sql >/dev/null
  docker exec -u 0 "$DB" rm -f /tmp/line-stripe.sql
fi

log "Building images"
docker build -t line-api QueueingApp
docker build --build-arg VITE_API_BASE_URL="https://$API_HOST" -t line-web QueuingAppFE
docker compose up -d --remove-orphans

log "Caddy: add Line's sites to AccessControl's Caddyfile"
# Line's sites go inline between markers. (An "import sites/*.caddy" glob made Caddy load them twice and stop.)
cp -n "$AC/Caddyfile" "$AC/Caddyfile.before-line" 2>/dev/null || true
cp "$AC/Caddyfile" /tmp/ac-Caddyfile.prev
{ sed '/^import sites\/\*\.caddy$/d; /^# >>> line$/,/^# <<< line$/d' /tmp/ac-Caddyfile.prev
  printf '# >>> line\n%s {\n\tencode gzip\n\treverse_proxy line-web:3000\n}\n%s {\n\treverse_proxy line-api:8080\n}\n# <<< line\n' "$APP_HOST" "$API_HOST"
} > /tmp/ac-Caddyfile.new
AC_APP=$(grep '^APP_HOST=' "$AC/.env" | cut -d= -f2-); AC_API=$(grep '^API_HOST=' "$AC/.env" | cut -d= -f2-)
docker run --rm -e APP_HOST="$AC_APP" -e API_HOST="$AC_API" -v /tmp/ac-Caddyfile.new:/etc/caddy/Caddyfile:ro caddy:2 \
  caddy validate --adapter caddyfile --config /etc/caddy/Caddyfile >/dev/null 2>&1 \
  || { echo "New Caddyfile is invalid; AccessControl's Caddyfile left unchanged"; exit 1; }
cat /tmp/ac-Caddyfile.new > "$AC/Caddyfile"   # same file, so Caddy's mount sees it
rm -f "$AC/sites/line.caddy"
cd "$AC"
docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile || docker compose up -d --no-deps --force-recreate caddy
cd "$ROOT"

log "Done"
docker compose ps
echo "Site: https://$APP_HOST   API: https://$API_HOST"