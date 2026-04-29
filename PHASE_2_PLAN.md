# Phase 2 — Thermal receipt printing

**Status:** Plan only. Nothing implemented. Reviewer sign-off required before coding starts.

**Scope:** Add silent thermal-receipt printing + cash-drawer pulse to POS, with a per-station master toggle. Keep current invoice/PDF flow untouched as the fallback. Use a **free** browser-native architecture (Chrome `--kiosk-printing` + HTML receipt template) — no QZ Tray, no per-seat licences.

**Success criteria:**
- A till with the toggle **OFF** behaves exactly like today (PDF in new tab, no behaviour change)
- A till with the toggle **ON** prints an 80mm receipt silently after every sale, opens the cash drawer if configured, falls back to PDF if printing fails
- Manual reprint button works in both modes
- Admin can configure stations from a settings UI
- MC Tactical and Axionis can opt in independently and incrementally (per till)

**Out of scope for Phase 2** (parked for later phase):
- QZ Tray byte-level ESC/POS control
- Multi-printer routing on a single till (kitchen + receipt + label simultaneously)
- LAN-shared printer with raw socket — for multi-till networked setups, you point each till at the same Windows shared printer in driver land, no extra code
- Print-server PC topology (same — Windows shared printer)

---

## Architecture (chosen)

**Master toggle per station:** `StationProfile.ThermalPrinterEnabled : bool` (default `false`).

**Free print transport:** Chrome (or Edge) launched with `--kiosk-printing` flag → `window.print()` from a hidden iframe → silent print to the system default printer (the till's installed thermal driver). No daemon, no certificate, no agent.

**Receipt rendering:** HTML/CSS template sized for 80mm rolls (`@page { size: 80mm auto }`), data-driven from existing invoice DTOs.

**Cash drawer:** Triggered by the printer driver's "open drawer after print" setting (Epson + Star expose this in their advanced driver options). For unsupported drivers, the toggle still works for printing; drawer opens manually.

**Fallback:** When toggle is OFF, *or* when `window.print()` fails, *or* when no printer driver is the system default → existing PDF-in-new-tab flow runs unchanged.

**Per-station identity:** Browser stores `pos:station-id` in localStorage. First time a till opens the app, an admin chooses (or creates) its station profile in `/settings/this-station`. Until set, the toggle is force-OFF and PDF flow is used.

---

## Live-safety strategy (MC Tactical specifically)

| Day | Activity | MC Tactical visible impact |
|-----|----------|---------------------------|
| 1 | Land Tasks 1-4 (backend + frontend skeletons, no UI integration) | Zero — no new UI shown anywhere |
| 2 | Land Tasks 5-7 (admin + station-settings UI, POS hook behind toggle) | Zero — toggle defaults OFF, sale flow unchanged |
| 3 | Land Task 8 (test print page) | Zero — admin-only screen, no till impact |
| 4 | Operator-only: install Epson driver on MC Tactical till PC, set as default | Zero (no app deploy yet) |
| 5 | Operator-only: enable toggle on MC Tactical's till, launch Chrome with kiosk flag | Day-of: silent thermal printing live |

Every step before Day 5 is reversible by setting `StationProfile.ThermalPrinterEnabled = false`. Day 5 itself is reversible the same way.

---

## Task breakdown

### Task 1 — Backend: `StationProfile` entity + migration

**Files**
- `src/HuntexPos.Api/Domain/StationProfile.cs` (new)
- `src/HuntexPos.Api/Data/HuntexDbContext.cs` (add DbSet)
- `src/HuntexPos.Api/Data/DbSeeder.cs` (add `EnsureStationProfilesTableAsync` — `CREATE TABLE IF NOT EXISTS StationProfiles (...)`)

**Schema (lean, 11 columns)**

```c#
public class StationProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";              // "Till 1", "Front counter"
    public bool ThermalPrinterEnabled { get; set; }     // master toggle (default false)
    public bool AutoPrintOnSale { get; set; } = true;
    public bool ReprintCustomerCopy { get; set; }       // print 2 copies if true
    public int ReceiptWidthMm { get; set; } = 80;       // 80 or 58
    public string? ReceiptFooterOverride { get; set; }  // null = use BusinessSettings.ReceiptFooter
    public bool OpenDrawerOnCash { get; set; } = true;  // informational only — driver handles actual pulse
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

**Risk:** Low. New table, additive only. Same `EnsureXxxTableAsync` pattern used by `BusinessSettings` and `MailSettings` — won't break existing DBs.

**Testing**
- Build API → `dotnet test` passes
- Boot API against MC Tactical's backup DB locally → table is created, no row inserted (until admin creates one), invoice flow unchanged

**Rollback** — `DROP TABLE StationProfiles;` on the host DB. No FKs into other tables.

**Production touched?** Schema-only on next deploy. Zero data risk.

---

### Task 2 — Backend: stations CRUD endpoints

**Files**
- `src/HuntexPos.Api/Controllers/StationsController.cs` (new)
- `src/HuntexPos.Api/DTOs/StationDtos.cs` (new)

**Endpoints (all `[Authorize(Roles="Owner,Manager")]`)**

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/api/stations` | list active stations |
| GET | `/api/stations/{id}` | single station |
| POST | `/api/stations` | create |
| PUT | `/api/stations/{id}` | update (toggle + all settings) |
| POST | `/api/stations/{id}/test-print` | returns receipt-data JSON for a canned test slip |
| DELETE | `/api/stations/{id}` | soft-delete (sets `IsActive = false`) |

**Risk:** Low. New routes only. No existing endpoint modified.

**Testing**
- Postman / curl each endpoint round-trip
- 401 for unauthorised callers, 403 for `Sales` role

**Rollback** — Remove the controller file. No data lost (rows survive in DB).

**Production touched?** New endpoints only. No regression surface.

---

### Task 3 — Backend: receipt-data endpoint

**Files**
- `src/HuntexPos.Api/Controllers/InvoicesController.cs` (one new action)
- `src/HuntexPos.Api/DTOs/ReceiptDataDto.cs` (new)

**Endpoint**

```
GET /api/invoices/{id}/receipt-data
```

Returns the **JSON** an HTML receipt template needs to render itself client-side: invoice number, date, lines (sku/name/qty/unit/total), discounts, tax, grand total, payment method, customer name + email, footer text from `BusinessSettings.ReceiptFooter`, business name + phone + address + VAT no.

This endpoint is **distinct from** `/api/invoices/{id}/pdf` — it returns raw data, not a PDF. The frontend uses it both for auto-print and manual reprint.

**Why a new endpoint** — keeps the receipt template a pure rendering concern in the SPA. Server doesn't care whether the client is going to print, email, screenshot, or display it.

**Risk:** Low. Read-only endpoint. Reuses existing invoice query logic.

**Testing**
- Hit endpoint with a real `invoiceId` from MC Tactical's backup → JSON contains all required fields
- Confirm `BusinessSettings.ReceiptFooter` populates the `footer` field

**Rollback** — Remove the action method. The PDF endpoint (`/pdf`) is untouched.

**Production touched?** New endpoint only.

---

### Task 4 — Frontend: `useReceiptPrinter` composable

**Files**
- `src/huntex-pos-web/src/composables/useReceiptPrinter.ts` (new)
- `src/huntex-pos-web/src/utils/storageMigrate.ts` (already exists)

**Public API**

```ts
export function useReceiptPrinter() {
  return {
    isThermalEnabled: ComputedRef<boolean>,            // toggle ON for current station?
    currentStation: ComputedRef<StationProfile | null>,
    setStationId: (id: string | null) => void,         // persist to localStorage
    printReceipt: (invoiceId: string) => Promise<{ ok: boolean; usedFallback: boolean }>,
    testPrint: () => Promise<...>,
    openDrawer: () => Promise<...>,                    // future: not in MVP
    refresh: () => Promise<void>                       // re-fetch station from API
  }
}
```

**Logic flow on `printReceipt(invoiceId)`**

```
1. Read currentStation.
   ├─ null OR ThermalPrinterEnabled=false  → call openPdfFallback(invoiceId), return {ok:true, usedFallback:true}
   └─ enabled
      │
      ▼
   GET /api/invoices/{invoiceId}/receipt-data
   ├─ network error → openPdfFallback, toast "Receipt data unavailable, opened PDF"
   └─ ok
      │
      ▼
   Mount <ReceiptTemplate> in a hidden, position:absolute iframe
   Call iframe.contentWindow.print()
   ├─ Chrome with --kiosk-printing  → silent print, return {ok:true, usedFallback:false}
   └─ Chrome without flag           → user sees standard print dialog (graceful)
```

**No QZ Tray, no certificate, no agent. Pure browser API.**

**Risk:** Low. New code path, only invoked when toggle is on. POS sale flow gates on it via `.catch(() => ...)`.

**Testing**
- Unit test the dispatch logic with a mock station profile (toggle off → fallback; toggle on → print)
- Manual test in Chrome with and without `--kiosk-printing`

**Rollback** — Frontend rebuild without the composable; or set toggle to off everywhere.

**Production touched?** Frontend image only.

---

### Task 5 — Frontend: `ReceiptTemplate.vue` (80mm HTML/CSS)

**Files**
- `src/huntex-pos-web/src/components/print/ReceiptTemplate.vue` (new)
- `src/huntex-pos-web/src/components/print/receipt-print.css` (new — print-only stylesheet)

**Layout sections (top → bottom)**

1. Logo (centered, max 240px wide, monochrome PNG from BusinessSettings)
2. Business name + tagline + address + phone + VAT no.
3. Horizontal rule
4. Invoice number + date/time + cashier name
5. Customer block (if not anonymous)
6. Line items: `<sku>  <name>  <qty>  <unit>  <total>` — auto-shrinking font
7. Subtotal / discount / VAT / **GRAND TOTAL** (large, bold)
8. Payment method + change due
9. Footer: `BusinessSettings.ReceiptFooter` (return policy, thanks-for-shopping, etc.)
10. Optional barcode of invoice number (Code128, rendered as inline SVG)

**Print CSS**

```css
@page { size: 80mm auto; margin: 2mm 3mm; }
@media print {
  body { font-family: 'Courier New', monospace; font-size: 11px; color: #000; }
  .receipt { width: 74mm; }
  .receipt__total { font-size: 16px; font-weight: 700; }
  .no-print { display: none !important; }
}
```

**58mm support** — same component, `@page { size: 58mm auto }` and narrower table columns when `station.ReceiptWidthMm === 58`.

**Risk:** Low. Pure rendering component, never visible to non-print users.

**Testing**
- Render in Chrome DevTools → "Emulate CSS media: print" → eyeball
- Print to PDF (any printer) → verify 80mm pagination

**Rollback** — Delete the component, no other code references it directly (only `useReceiptPrinter` does).

**Production touched?** Frontend image only.

---

### Task 6 — Frontend: admin stations UI

**Files**
- `src/huntex-pos-web/src/views/admin/StationsView.vue` (new)
- Router entry: `/admin/stations`
- AppShell sidebar: new "Stations" link in admin section, gated to `Owner`/`Manager`

**Screen layout**

```
┌──────────────────────────────────────────────┐
│  Stations                          [+ Add ]  │
├──────────────────────────────────────────────┤
│  ┌────────────────────────────────────────┐  │
│  │ Till 1                          [edit] │  │
│  │ Thermal printer:        ON  •  80mm    │  │
│  │ Auto-print:             ON             │  │
│  │ Drawer on cash:         ON             │  │
│  └────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────┐  │
│  │ Counter B                       [edit] │  │
│  │ Thermal printer:        OFF            │  │
│  │ — uses PDF fallback —                  │  │
│  └────────────────────────────────────────┘  │
└──────────────────────────────────────────────┘
```

Edit modal: name, **Thermal printer connected** master toggle, auto-print toggle, receipt width radio (80/58mm), drawer-on-cash toggle, footer override textarea, notes.

**Risk:** Low. Admin-only screen.

**Production touched?** Frontend image only.

---

### Task 7 — Frontend: `/settings/this-station` UI + station identity

**Files**
- `src/huntex-pos-web/src/views/SettingsThisStationView.vue` (new)
- Router: `/settings/this-station`
- Sidebar: "This till" link in Settings section

**Screen contents**

1. **Which station is this PC?** — dropdown of existing `StationProfile`s + "Create new"
2. Save button → writes selected station ID to `localStorage['pos:station-id']` AND echoes the chosen station's settings as a read-only summary
3. **"Test print"** button → calls `useReceiptPrinter().testPrint()` → silent slip prints if toggle on, or shows PDF if not
4. **Diagnostics:**
   - Browser: Chrome 128 / Firefox / Edge / Safari (auto-detected)
   - `--kiosk-printing` flag: detected? (heuristic: try `window.print()` to a probe; if a dialog appears, flag is OFF)
   - Default system printer: name (via WebUSB enumeration if granted, else "unknown")

**Risk:** Low. New screen, no impact on other flows.

**Production touched?** Frontend image only.

---

### Task 8 — POS integration (one-line hook in `PosView.vue`)

**File** — `src/huntex-pos-web/src/views/PosView.vue`

**Exact change**

After the existing successful sale block:

```ts
// existing
const { data } = await http.post('/api/invoices', payload)
saleSummary.value = data
showSaleSummary.value = true

// NEW — fire-and-forget, never blocks the sale
useReceiptPrinter()
  .printReceipt(data.id)
  .catch((err) => {
    console.warn('Receipt print failed:', err)
    toast.warning('Print failed — receipt PDF available in invoice history')
  })
```

The `.catch` ensures **a failed print never blocks the sale**. The sale is already in the database before this line runs.

When the station's `ThermalPrinterEnabled = false`, the composable's first branch returns `{ok:true, usedFallback:true}` immediately — i.e. it opens the PDF tab as it does today. **No behaviour change for tills with toggle off.**

**Risk:** Very low. One line, with guaranteed-non-throwing fallback.

**Testing**
- Toggle off + complete sale → identical to today (PDF tab opens)
- Toggle on + Chrome kiosk-printing flag set + sale → silent thermal receipt
- Toggle on + Chrome WITHOUT flag → standard print dialog appears (graceful)
- Toggle on + no printer driver installed → print dialog shows "no printer", user clicks Cancel, fallback PDF opens

**Rollback** — Comment out the four added lines. POS reverts to today's flow byte-for-byte.

**Production touched?** Frontend image only. Sales flow is gated on toggle off.

---

### Task 9 — Manual reprint button

**File** — wherever the existing "View PDF" button lives on invoice detail (likely `InvoiceDetailView.vue` or `ReportsView.vue` — confirm in implementation phase)

**Change** — Add second button "Reprint receipt" next to "View PDF". Behaviour:
- Calls `useReceiptPrinter().printReceipt(invoiceId)` — same composable, same dispatch logic
- If station's toggle is off → opens PDF (matching the existing button)
- If on → silent reprint

**Risk:** Very low. Additive UI button.

**Production touched?** Frontend image only.

---

### Task 10 — Operator playbook (no code changes)

**Files** — `deploy/THERMAL_PRINTER.md` (new ops doc)

**Contents**
1. Hardware shopping list (Epson TM-T20III USB ~R3500, drawer ~R1500)
2. Driver install steps (Windows 10/11)
3. Printer driver settings — checking the "Open drawer after print" box
4. Chrome shortcut creation: `chrome.exe --kiosk-printing --app=https://pos.mctactical.co.za`
5. First-time station setup walk-through (login as admin → /admin/stations → create → /settings/this-station → select)
6. Troubleshooting flowchart (no print → check default printer → check kiosk flag → check toggle)

**Risk:** Documentation only.

---

## Rollout phases

| Phase | What ships | Toggle default | MC Tactical / Axionis impact |
|-------|------------|----------------|------------------------------|
| **2A** | Tasks 1-4 (backend + composable, no UI) | n/a | None — code paths exist but unused |
| **2B** | Tasks 5-9 (UI screens + POS hook) | OFF for all stations | None — new admin UI visible, but auto-print never triggers because no station has toggle ON |
| **2C** | Operator: install printer + driver, enable toggle on one MC Tactical till | ON for that one station | Silent thermal printing live for that till |
| **2D** | Same playbook for Axionis | ON for their till | Silent thermal printing live |

**Each phase is independently deployable and reversible.** Toggle is the single point of control.

---

## Risk register (cross-cutting)

| Risk | Mitigation |
|------|-----------|
| Sale completes but receipt fails to print | `.catch()` on the print call. Sale is persisted in DB before printing is attempted. Reprint button always available. |
| Browser blocks `window.print()` due to popup blocker | Print is triggered from a user gesture (sale completion), so this doesn't apply. Fallback PDF tab triggered the same way. |
| Cashier launches Chrome without `--kiosk-printing` flag | App still works — they just see the print dialog and click OK. Operator doc tells them to use the desktop shortcut. |
| Wrong printer set as default → receipt prints to A4 office printer | Diagnostic on `/settings/this-station` shows current default printer name. Operator must verify before going live. |
| Cash drawer doesn't open | Driver-level config, not application-level. Document the driver checkbox in `THERMAL_PRINTER.md`. Test print confirms. |
| Browser UPGRADES drop the kiosk-printing flag | Chrome has supported `--kiosk-printing` since v17 (2012). Stable for the foreseeable future. If ever removed, fall back to print dialog (one extra click per sale). |
| New whitelabel customer doesn't have a thermal printer | Toggle stays off → identical behaviour to today (PDF in tab). Zero forced upgrade. |

---

## Open questions for sign-off

1. **80mm vs 58mm support in MVP?** — Recommendation: 80mm only for Phase 2, add 58mm in Phase 3 if any client needs it. (Both supported by the same template, just need a second test pass.)
2. **Logo on receipt — required or optional?** — Recommendation: optional with a default of off. Cheap thermals print logos slowly and grainy; many merchants prefer text-only.
3. **Per-station `ReceiptFooterOverride` — needed?** — Probably yes: branch shops with different return policies. Easy to expose, cheap to implement.
4. **Reprint on an old (pre-Phase-2) invoice?** — Yes; the `/api/invoices/{id}/receipt-data` endpoint reads from `Invoice` table which is untouched, so any historical invoice can be reprinted.
5. **Cash drawer test on its own?** — Recommendation: not in MVP. Driver-level "test page" already opens the drawer. Add a UI button in Phase 3 if requested.

---

## Effort estimate

| Tasks | Days |
|-------|------|
| 1-3 (backend) | 1.5 |
| 4-5 (composable + template) | 1.5 |
| 6-7 (admin + station UI) | 1.5 |
| 8-9 (POS hook + reprint) | 0.5 |
| 10 (ops doc) | 0.5 |
| Testing + edge cases + first-customer install | 1.5 |
| **Total** | **~7 days** |

Cost: **0 ZAR in licences**, ~R5000 hardware per till one-off (Epson TM-T20III + RJ-11 drawer).

---

## Decision points

Before I implement Phase 2A, please confirm:

- [ ] Master toggle approach (`StationProfile.ThermalPrinterEnabled`) — agreed
- [ ] Free Chrome `--kiosk-printing` path (no QZ Tray) — agreed
- [ ] Both toggle OFF and toggle ON are first-class supported topologies — agreed
- [ ] Order of phases (2A → 2B → 2C → 2D) — agreed
- [ ] Any answer different from my recommendation on the 5 open questions above

When you sign off on the above, I'll start with Task 1 (StationProfile entity + migration), in a fresh branch off `main` (so it stacks cleanly on top of Phase 1A whenever you merge that).
