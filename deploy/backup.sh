#!/usr/bin/env bash
# Hot SQLite backup + PDF/branding archive for a POS deployment.
#
# Usage:
#   ./backup.sh /opt/mctactical
#
# Optional env:
#   BACKUP_REMOTE   rsync/scp/aws-s3 destination, e.g.:
#                     user@offsite-host:/srv/backups
#                     s3://my-bucket/pos-backups
#   RETENTION_DAYS  number of days to keep local backups (default 7)
#
# Requires: sqlite3 on the host (apt install sqlite3) — used directly against
# the bind-mounted DB file. SQLite's online .backup is safe while the API
# container is reading/writing the database.

set -euo pipefail

ROOT="${1:?client root required, e.g. /opt/mctactical}"
NAME="$(basename "$ROOT")"
TS="$(date -u +%Y%m%d-%H%M%S)"
DEST="$ROOT/backups"
DB_FILE="$ROOT/data/huntex.db"
RETENTION="${RETENTION_DAYS:-7}"

if [ ! -f "$DB_FILE" ]; then
  echo "FATAL: database not found at $DB_FILE" >&2
  exit 1
fi

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "FATAL: sqlite3 not installed on host. Run: sudo apt install sqlite3" >&2
  exit 1
fi

mkdir -p "$DEST"
DB_OUT="$DEST/db-${TS}.db"
FILES_OUT="$DEST/files-${TS}.tar.gz"

echo "[$(date -u +%FT%TZ)] backing up $NAME"

# Online backup — safe to run while the API container is up.
sqlite3 "$DB_FILE" ".backup '$DB_OUT'"
echo "  db   -> $DB_OUT ($(du -h "$DB_OUT" | cut -f1))"

# PDFs and branding assets (non-fatal if dirs are missing on a fresh install).
if [ -d "$ROOT/data/pdfs" ] || [ -d "$ROOT/data/branding" ]; then
  tar -czf "$FILES_OUT" -C "$ROOT/data" \
    $([ -d "$ROOT/data/pdfs" ] && echo pdfs) \
    $([ -d "$ROOT/data/branding" ] && echo branding) 2>/dev/null || true
  echo "  files-> $FILES_OUT ($(du -h "$FILES_OUT" | cut -f1))"
fi

# Local retention.
find "$DEST" -maxdepth 1 -type f -name "db-*.db" -mtime "+${RETENTION}" -delete
find "$DEST" -maxdepth 1 -type f -name "files-*.tar.gz" -mtime "+${RETENTION}" -delete

# Offsite copy if configured.
if [ -n "${BACKUP_REMOTE:-}" ]; then
  case "$BACKUP_REMOTE" in
    s3://*)
      if ! command -v aws >/dev/null 2>&1; then
        echo "WARN: BACKUP_REMOTE set to S3 but aws CLI missing — skipping offsite" >&2
      else
        aws s3 cp "$DB_OUT" "$BACKUP_REMOTE/$NAME/" --only-show-errors
        [ -f "$FILES_OUT" ] && aws s3 cp "$FILES_OUT" "$BACKUP_REMOTE/$NAME/" --only-show-errors
        echo "  offsite -> $BACKUP_REMOTE/$NAME/"
      fi
      ;;
    *)
      rsync -az "$DB_OUT" "$BACKUP_REMOTE/$NAME/" 2>&1 || \
        echo "WARN: rsync to $BACKUP_REMOTE failed — backup remains local" >&2
      [ -f "$FILES_OUT" ] && rsync -az "$FILES_OUT" "$BACKUP_REMOTE/$NAME/" 2>&1 || true
      echo "  offsite -> $BACKUP_REMOTE/$NAME/"
      ;;
  esac
fi

echo "[$(date -u +%FT%TZ)] done"
