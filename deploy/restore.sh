#!/usr/bin/env bash
# Restore a POS deployment from a backup.sh-produced .db file.
#
# Usage:
#   ./restore.sh /opt/mctactical /opt/mctactical/backups/db-20260428-023001.db
#
# This script:
#   1. Stops the API container so the DB is not in use.
#   2. Moves the current DB aside to .pre-restore-<timestamp>
#   3. Copies the backup into place.
#   4. Restarts the API container.
#
# It deliberately does NOT touch PDFs/branding — restore those manually from
# the matching files-*.tar.gz if needed (tar -xzf into <root>/data/).

set -euo pipefail

ROOT="${1:?client root required, e.g. /opt/mctactical}"
BACKUP="${2:?backup .db file required}"
NAME="$(basename "$ROOT")"
DB_FILE="$ROOT/data/huntex.db"
TS="$(date -u +%Y%m%d-%H%M%S)"

if [ ! -f "$BACKUP" ]; then
  echo "FATAL: backup file not found: $BACKUP" >&2
  exit 1
fi

echo "Will restore $BACKUP -> $DB_FILE"
echo "Current DB will be saved as $DB_FILE.pre-restore-$TS"
read -r -p "Type the client name ($NAME) to confirm: " CONFIRM
[ "$CONFIRM" = "$NAME" ] || { echo "Aborted."; exit 1; }

cd "$ROOT"
echo "Stopping containers..."
docker compose stop api

if [ -f "$DB_FILE" ]; then
  mv "$DB_FILE" "$DB_FILE.pre-restore-$TS"
  echo "  current DB saved -> $DB_FILE.pre-restore-$TS"
fi

cp "$BACKUP" "$DB_FILE"
chown 1654:1654 "$DB_FILE" 2>/dev/null || true
echo "  restored DB -> $DB_FILE"

echo "Starting containers..."
docker compose start api
echo "Done. Verify the app at the configured URL, then delete $DB_FILE.pre-restore-$TS once happy."
