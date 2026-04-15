# MC Tactical POS — Project Context

## Deployment

- **Server**: `cha021-truserv1230-jhb1-001` (SSH user: `paul`)
- **Repo path on server**: `/opt/mctactical`
- **Domain**: `mctactical.charsleydigital.co.za`
- **Web port**: `8095` (set via `WEB_PORT=8095` in `.env`)
- **API internal port**: `8080` (container-only, proxied by nginx in the web container)
- **Database**: SQLite at `/opt/mctactical/data/huntex.db`
- **PDFs**: `/opt/mctactical/data/pdfs/`
- **Git remote**: `https://github.com/vassago85/mctactical.git` (branch: `main`)

## Deploy command (on server)

```bash
cd /opt/mctactical && sudo git pull origin main && sudo docker compose up -d --build
```

## Tech stack

- **Backend**: .NET 8 (C#), EF Core, SQLite, QuestPDF, JWT auth
- **Frontend**: Vue 3, TypeScript, Vite, vite-plugin-pwa
- **Hosting**: Docker Compose (api + nginx containers)
- **TLS**: Terminated upstream (Nginx Proxy Manager or similar), not in these containers

## .env keys (on server, not committed)

```
OWNER_EMAIL=...
OWNER_PASSWORD=...
JWT_KEY=...
MCTACTICAL_DATA_DIR=/opt/mctactical/data
PUBLIC_BASE_URL=https://mctactical.charsleydigital.co.za
WEB_PORT=8095
```

## Key paths (local dev)

- API project: `src/HuntexPos.Api/`
- Web project: `src/huntex-pos-web/`
- Docker Compose: `docker-compose.yml` (root)
- Nginx config: `src/huntex-pos-web/nginx.conf`

## Conventions

- Hash router (`createWebHashHistory`) — no server-side URL rewriting needed for SPA routes
- All API calls go through `/api/` prefix, proxied by nginx to the api container
- Role hierarchy: Dev > Owner > Admin > Sales
- MC Tactical is VAT registered — invoices must show VAT (15%) breakdown
- Currency: ZAR (South African Rand)
