# `deploy/` — Operator scripts

Scripts and templates for running this POS as a hosted service. Phase 1A introduces only the backup/restore pair; provisioning and per-client compose templates land in Phase 1B.

## Backup / restore

### What it does

`backup.sh` produces two artefacts per run:

- `db-<UTC-timestamp>.db` — a hot copy of the SQLite database via `sqlite3 .backup` (online-safe, no downtime).
- `files-<UTC-timestamp>.tar.gz` — `pdfs/` + `branding/` from the data directory.

Local retention defaults to 7 days. Offsite copy is opt-in via the `BACKUP_REMOTE` env var (rsync target or `s3://` URL).

### Host prerequisites

- `sqlite3` CLI (`sudo apt install sqlite3`)
- For S3 offsite: `awscli` (`sudo apt install awscli`)

The API container does **not** need to be modified — backups read directly from the bind-mounted DB file on the host.

### Manual one-off backup

```bash
sudo /opt/mctactical/deploy/backup.sh /opt/mctactical
```

Output goes to `/opt/mctactical/backups/`.

### Daily cron (recommended on every production host)

Edit `/etc/cron.d/mctactical-backup` (root-owned, mode 0644):

```
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# Daily at 02:30 UTC (~04:30 SAST). Adjust path per client root.
30 2 * * * root /opt/mctactical/deploy/backup.sh /opt/mctactical >> /var/log/mctactical-backup.log 2>&1
```

To enable offsite for that cron job:

```
30 2 * * * root BACKUP_REMOTE=s3://my-bucket/pos /opt/mctactical/deploy/backup.sh /opt/mctactical >> /var/log/mctactical-backup.log 2>&1
```

### Restore

```bash
sudo /opt/mctactical/deploy/restore.sh /opt/mctactical /opt/mctactical/backups/db-20260428-023001.db
```

The script asks for confirmation, stops the API container, swaps the DB file (keeping the previous one as `huntex.db.pre-restore-<ts>` so the restore is itself reversible), then restarts. PDFs and branding aren't restored automatically — if needed:

```bash
sudo tar -xzf /opt/mctactical/backups/files-20260428-023001.tar.gz -C /opt/mctactical/data/
```

### Verifying backups regularly

A backup you've never tested is not a backup. Once a quarter:

1. Provision a throwaway test directory (e.g. `/tmp/pos-restore-test/`).
2. Copy a recent `db-*.db` and `files-*.tar.gz` into it.
3. Run the API container against the copy and confirm sales/products/users open correctly.
