using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

/// <summary>
/// Phase 1 Shopify bridge: verify the connection, check catalog overlap by SKU, and push a
/// single product up to Shopify (POS is the source of truth). Restricted to Owner/Dev since
/// it manages an external integration and store data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Owner},{Roles.Dev}")]
public class ShopifyController : ControllerBase
{
    private readonly HuntexDbContext _db;
    private readonly ShopifyClient _shopify;
    private readonly ShopifyOrderImportService _orderImport;

    public ShopifyController(HuntexDbContext db, ShopifyClient shopify, ShopifyOrderImportService orderImport)
    {
        _db = db;
        _shopify = shopify;
        _orderImport = orderImport;
    }

    /// <summary>Test credentials and list Shopify locations (to help pick Shopify:LocationId).</summary>
    [HttpGet("ping")]
    public async Task<IActionResult> Ping(CancellationToken ct)
    {
        try
        {
            var result = await _shopify.PingAsync(ct);
            return Ok(result);
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }
    }

    /// <summary>
    /// Compare POS SKUs against Shopify variant SKUs to reveal how much of the catalog already
    /// lines up. This is the "check in the beginning" step before committing to a match strategy.
    /// </summary>
    [HttpGet("match-check")]
    public async Task<IActionResult> MatchCheck(CancellationToken ct)
    {
        HashSet<string> shopifySkus;
        try
        {
            shopifySkus = await _shopify.GetAllVariantSkusAsync(ct);
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }

        var posSkus = await _db.Products
            .Where(p => p.Active)
            .Select(p => p.Sku)
            .ToListAsync(ct);

        var posSet = new HashSet<string>(
            posSkus.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var matched = posSet.Where(shopifySkus.Contains).ToList();
        var posOnly = posSet.Where(s => !shopifySkus.Contains(s)).ToList();
        var shopifyOnly = shopifySkus.Where(s => !posSet.Contains(s)).ToList();

        return Ok(new
        {
            posActiveSkuCount = posSet.Count,
            shopifySkuCount = shopifySkus.Count,
            matchedCount = matched.Count,
            posOnlyCount = posOnly.Count,
            shopifyOnlyCount = shopifyOnly.Count,
            sampleMatched = matched.Take(25).ToList(),
            samplePosOnly = posOnly.Take(25).ToList(),
            sampleShopifyOnly = shopifyOnly.Take(25).ToList()
        });
    }

    /// <summary>
    /// Initial two-way reconcile (link-only). Links POS products to Shopify variants and stores the
    /// Shopify ids on the POS product WITHOUT overwriting any field values. Matching is attempted in
    /// order of confidence: exact SKU, then real barcode, then normalized SKU (case/punctuation/
    /// leading-zero-insensitive). A POS product is only linked when exactly one *unclaimed* Shopify
    /// variant matches, and each variant is claimed once so nothing is double-linked. Defaults to a
    /// dry-run preview; pass <c>apply=true</c> to persist. Shopify-only items are reported, not imported.
    /// </summary>
    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile([FromQuery] bool apply = false, CancellationToken ct = default)
    {
        List<ShopifyVariantDetail> variants;
        try
        {
            variants = await _shopify.GetAllVariantDetailsAsync(ct);
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }

        var byExactSku = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Sku))
            .GroupBy(v => v.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var byBarcode = variants
            .Where(v => IsRealBarcode(v.Barcode))
            .GroupBy(v => v.Barcode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var byNormSku = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Sku) && NormalizeSku(v.Sku).Length > 0)
            .GroupBy(v => NormalizeSku(v.Sku!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var ambiguousSkus = byExactSku.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToList();

        var products = await _db.Products
            .Where(p => p.Active && p.Sku != null && p.Sku != "")
            .ToListAsync(ct);

        var linkedExact = new List<string>();
        var linkedBarcode = new List<string>();
        var linkedNorm = new List<string>();
        var alreadyLinked = new List<string>();
        var ambiguousMatched = new List<string>();

        // Variants already spoken for, so a later/looser rule never steals one.
        var claimedVariantIds = new HashSet<long>();
        var resolved = new HashSet<Guid>();

        foreach (var p in products.Where(p => p.ShopifyVariantId.HasValue))
        {
            alreadyLinked.Add(p.Sku.Trim());
            claimedVariantIds.Add(p.ShopifyVariantId!.Value);
            resolved.Add(p.Id);
        }

        void Link(Product p, ShopifyVariantDetail v, List<string> bucket)
        {
            if (apply)
            {
                p.ShopifyProductId = v.ProductId;
                p.ShopifyVariantId = v.VariantId;
                p.ShopifyInventoryItemId = v.InventoryItemId == 0 ? null : v.InventoryItemId;
                p.ShopifySyncedAt = DateTimeOffset.UtcNow;
            }
            claimedVariantIds.Add(v.VariantId);
            resolved.Add(p.Id);
            bucket.Add(p.Sku.Trim());
        }

        // Pass 1: exact SKU (strongest — claims variants first).
        foreach (var p in products.Where(p => !resolved.Contains(p.Id)))
        {
            if (!byExactSku.TryGetValue(p.Sku.Trim(), out var vs)) continue;
            if (vs.Count > 1) { ambiguousMatched.Add(p.Sku.Trim()); resolved.Add(p.Id); continue; }
            if (!claimedVariantIds.Contains(vs[0].VariantId)) Link(p, vs[0], linkedExact);
        }

        // Pass 2: real barcode.
        foreach (var p in products.Where(p => !resolved.Contains(p.Id)))
        {
            if (!IsRealBarcode(p.Barcode) || !byBarcode.TryGetValue(p.Barcode!.Trim(), out var vs)) continue;
            var avail = vs.Where(v => !claimedVariantIds.Contains(v.VariantId)).ToList();
            if (avail.Count == 1) Link(p, avail[0], linkedBarcode);
        }

        // Pass 3: normalized SKU.
        foreach (var p in products.Where(p => !resolved.Contains(p.Id)))
        {
            var norm = NormalizeSku(p.Sku);
            if (norm.Length == 0 || !byNormSku.TryGetValue(norm, out var vs)) continue;
            var avail = vs.Where(v => !claimedVariantIds.Contains(v.VariantId)).ToList();
            if (avail.Count == 1) Link(p, avail[0], linkedNorm);
        }

        var posOnly = products.Where(p => !resolved.Contains(p.Id)).Select(p => p.Sku.Trim()).ToList();
        var totalLinked = linkedExact.Count + linkedBarcode.Count + linkedNorm.Count;

        if (apply && totalLinked > 0)
            await _db.SaveChangesAsync(ct);

        var posSkuSet = new HashSet<string>(products.Select(p => p.Sku.Trim()), StringComparer.OrdinalIgnoreCase);
        var shopifyOnly = byExactSku.Keys.Where(s => !posSkuSet.Contains(s)).ToList();

        return Ok(new
        {
            applied = apply,
            linkedCount = totalLinked,
            linkedByExactSku = linkedExact.Count,
            linkedByBarcode = linkedBarcode.Count,
            linkedByNormalizedSku = linkedNorm.Count,
            alreadyLinkedCount = alreadyLinked.Count,
            posOnlyCount = posOnly.Count,
            shopifyOnlyCount = shopifyOnly.Count,
            ambiguousSkuCount = ambiguousSkus.Count,
            ambiguousMatchedCount = ambiguousMatched.Count,
            sampleLinkedByBarcode = linkedBarcode.Take(25).ToList(),
            sampleLinkedByNormalizedSku = linkedNorm.Take(25).ToList(),
            samplePosOnly = posOnly.Take(25).ToList(),
            sampleShopifyOnly = shopifyOnly.Take(25).ToList(),
            sampleAmbiguous = ambiguousMatched.Take(25).ToList(),
            note = apply
                ? "Links saved. No product fields were overwritten. Matched by exact SKU, then real barcode, then normalized SKU. Shopify-only items were not imported."
                : "Preview only \u2014 nothing was saved. Re-run with ?apply=true to persist the links."
        });
    }

    /// <summary>
    /// A barcode is trustworthy for matching only when it looks like a real EAN/UPC: 8+ characters,
    /// all digits, and not a placeholder of a single repeated digit (e.g. "0000000000277").
    /// </summary>
    private static bool IsRealBarcode(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return false;
        var b = barcode.Trim();
        if (b.Length < 8) return false;
        if (!b.All(char.IsDigit)) return false;
        return b.Distinct().Count() > 1;
    }

    /// <summary>
    /// Push a single POS product to Shopify and persist the returned Shopify ids on the product.
    /// If a location is configured, the current QtyOnHand is set as the online available quantity.
    /// </summary>
    [HttpPost("products/{id:guid}/push")]
    public async Task<IActionResult> PushProduct(Guid id, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product == null) return NotFound();

        try
        {
            var push = await _shopify.PushProductAsync(product, ct);

            product.ShopifyProductId = push.ProductId;
            product.ShopifyVariantId = push.VariantId;
            product.ShopifyInventoryItemId = push.InventoryItemId;
            product.ShopifySyncedAt = DateTimeOffset.UtcNow;

            var inventoryPushed = false;
            if (push.InventoryItemId != 0)
            {
                try
                {
                    await _shopify.SetInventoryAsync(push.InventoryItemId, product.QtyOnHand, ct);
                    inventoryPushed = true;
                }
                catch (ShopifyNotConfiguredException)
                {
                    // No location configured yet — product/price synced, inventory skipped.
                }
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                product.Id,
                product.Sku,
                product.ShopifyProductId,
                product.ShopifyVariantId,
                product.ShopifyInventoryItemId,
                inventoryPushed,
                availableQtyPushed = inventoryPushed ? product.QtyOnHand : (int?)null,
                product.ShopifySyncedAt
            });
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }
    }

    /// <summary>
    /// Push POS categorization (Category/Manufacturer/ItemType as Shopify tags, plus productType and
    /// vendor) to every already-linked Shopify product so both systems filter the same. One-way; no
    /// other product fields change. Defaults to a dry-run preview; pass <c>apply=true</c> to push.
    /// </summary>
    [HttpPost("sync-tags")]
    public async Task<IActionResult> SyncTags([FromQuery] bool apply = false, CancellationToken ct = default)
    {
        var linked = await _db.Products
            .Where(p => p.Active && p.ShopifyProductId != null)
            .ToListAsync(ct);

        var sample = linked.Take(25).Select(p => new
        {
            p.Sku,
            tags = new[] { p.Category, p.Manufacturer, p.ItemType }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        }).ToList();

        if (!apply)
        {
            return Ok(new
            {
                applied = false,
                linkedProductCount = linked.Count,
                sample,
                note = "Preview only \u2014 nothing was pushed. Re-run with ?apply=true to sync tags/type/vendor to Shopify."
            });
        }

        var updated = 0;
        var failures = new List<object>();
        foreach (var p in linked)
        {
            try
            {
                await _shopify.UpdateProductCategoryAsync(p.ShopifyProductId!.Value, p, ct);
                updated++;
            }
            catch (ShopifyNotConfiguredException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ShopifyApiException ex)
            {
                if (failures.Count < 25) failures.Add(new { p.Sku, status = ex.StatusCode, detail = ex.Message });
            }
        }

        return Ok(new
        {
            applied = true,
            linkedProductCount = linked.Count,
            updatedCount = updated,
            failedCount = failures.Count,
            failures,
            note = "Synced POS Category/Manufacturer/ItemType to Shopify as tags (plus productType and vendor) on linked products."
        });
    }

    /// <summary>
    /// Pull recent paid Shopify orders into the POS as invoices tagged "Shopify" so they show in
    /// Sales History. Visibility-only: POS stock is never changed. Already-imported orders are
    /// skipped. Defaults to a dry-run preview; pass <c>apply=true</c> to actually save the invoices.
    /// </summary>
    [HttpPost("orders/sync")]
    public async Task<IActionResult> SyncOrders(
        [FromQuery] bool apply = false,
        [FromQuery] int max = 250,
        CancellationToken ct = default)
    {
        var maxOrders = Math.Clamp(max, 1, 1000);
        try
        {
            var summary = await _orderImport.ImportPaidOrdersAsync(maxOrders, apply, ct);
            return Ok(new
            {
                summary.Applied,
                summary.FetchedCount,
                summary.ImportedCount,
                summary.RepairedCount,
                summary.SkippedExistingCount,
                summary.MatchedLineCount,
                summary.UnmatchedLineCount,
                summary.OrdersWithUnmatchedLines,
                summary.SampleImported,
                note = summary.Applied
                    ? "Imported as invoices tagged 'Shopify'. POS stock was not changed. Every line item is captured \u2014 items with no matching POS product are recorded against a hidden 'Shopify online item (not in POS)' placeholder so receipts stay complete. Orders imported earlier that were missing items were repaired."
                    : "Preview only \u2014 nothing was saved. Re-run with ?apply=true to import these orders (and repair any earlier imports missing items)."
            });
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }
    }

    /// <summary>
    /// Read-only diagnostic: measure how well POS products and Shopify variants can be matched under
    /// different strategies (exact SKU, normalized SKU, barcode) so we can choose a linking approach.
    /// Nothing is saved and no product fields change. Owner/Dev only.
    /// </summary>
    [HttpGet("match-analysis")]
    public async Task<IActionResult> MatchAnalysis(CancellationToken ct = default)
    {
        List<ShopifyVariantDetail> variants;
        try
        {
            variants = await _shopify.GetAllVariantDetailsAsync(ct);
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }

        var products = await _db.Products
            .Where(p => p.Active && p.Sku != null && p.Sku != "")
            .Select(p => new { p.Sku, p.Barcode, p.Name })
            .ToListAsync(ct);

        // Shopify-side lookups. A key mapping to more than one variant is "ambiguous" and cannot be
        // used for a confident automatic link.
        var byExactSku = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Sku))
            .GroupBy(v => v.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var byNormSku = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Sku) && NormalizeSku(v.Sku) is { Length: > 0 })
            .GroupBy(v => NormalizeSku(v.Sku!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var byBarcode = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Barcode))
            .GroupBy(v => v.Barcode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var barcodeRecovered = new List<object>();
        var normSkuRecovered = new List<object>();
        var barcodeRecoveredCount = 0;
        var barcodeAmbiguousCount = 0;
        var normSkuRecoveredCount = 0;
        var normSkuAmbiguousCount = 0;
        var exactMatchedCount = 0;
        var newlyMatchable = 0; // union across barcode + normSku for POS rows exact-SKU missed

        foreach (var p in products)
        {
            var sku = p.Sku!.Trim();
            var exact = byExactSku.ContainsKey(sku);
            if (exact) { exactMatchedCount++; continue; }

            var recovered = false;

            if (!string.IsNullOrWhiteSpace(p.Barcode) && byBarcode.TryGetValue(p.Barcode.Trim(), out var bc))
            {
                if (bc.Count == 1)
                {
                    barcodeRecoveredCount++;
                    recovered = true;
                    if (barcodeRecovered.Count < 25)
                        barcodeRecovered.Add(new { posSku = sku, posName = p.Name, posBarcode = p.Barcode, shopifySku = bc[0].Sku, shopifyTitle = bc[0].Title });
                }
                else barcodeAmbiguousCount++;
            }

            var norm = NormalizeSku(sku);
            if (norm.Length > 0 && byNormSku.TryGetValue(norm, out var ns))
            {
                if (ns.Count == 1)
                {
                    normSkuRecoveredCount++;
                    recovered = true;
                    if (normSkuRecovered.Count < 25)
                        normSkuRecovered.Add(new { posSku = sku, posName = p.Name, shopifySku = ns[0].Sku, shopifyTitle = ns[0].Title });
                }
                else normSkuAmbiguousCount++;
            }

            if (recovered) newlyMatchable++;
        }

        return Ok(new
        {
            dataQuality = new
            {
                posActiveWithSku = products.Count,
                posWithBarcode = products.Count(p => !string.IsNullOrWhiteSpace(p.Barcode)),
                shopifyVariants = variants.Count,
                shopifyWithSku = variants.Count(v => !string.IsNullOrWhiteSpace(v.Sku)),
                shopifyWithBarcode = variants.Count(v => !string.IsNullOrWhiteSpace(v.Barcode))
            },
            currentExactSkuMatched = exactMatchedCount,
            recoverableByBarcode = new { confident = barcodeRecoveredCount, ambiguous = barcodeAmbiguousCount },
            recoverableByNormalizedSku = new { confident = normSkuRecoveredCount, ambiguous = normSkuAmbiguousCount },
            newlyMatchableTotal = newlyMatchable,
            sampleBarcodeMatches = barcodeRecovered,
            sampleNormalizedSkuMatches = normSkuRecovered,
            note = "Read-only. No links were created and no products were changed. 'confident' = exactly one Shopify variant matched; 'ambiguous' = more than one, so it needs a human decision."
        });
    }

    /// <summary>
    /// List Shopify sale items that are not yet linked to a POS product, aggregated by Shopify variant
    /// (falling back to the sale SKU when the variant id is missing on older imports) and sorted by
    /// revenue so the highest-impact items to link surface first. Shipping lines are excluded. Read-only.
    /// </summary>
    [HttpGet("unlinked-sales")]
    public async Task<IActionResult> UnlinkedSales(CancellationToken ct = default)
    {
        var placeholder = await _db.Products
            .FirstOrDefaultAsync(p => p.Sku == ShopifyOrderImportService.UnlinkedPlaceholderSku, ct);
        if (placeholder == null)
            return Ok(Array.Empty<object>());

        // Placeholder-attached Shopify lines, minus shipping (which carries neither variant id nor SKU).
        var lines = await _db.InvoiceLines
            .Where(l => l.ProductId == placeholder.Id
                && l.Invoice!.Source == "Shopify"
                && !(l.ShopifyVariantId == null && l.SkuAtSale == null))
            .Select(l => new { l.ShopifyVariantId, l.SkuAtSale, l.Description, l.Quantity, l.LineTotal, l.InvoiceId })
            .ToListAsync(ct);

        var groups = lines
            .GroupBy(l => l.ShopifyVariantId.HasValue
                ? $"v:{l.ShopifyVariantId.Value}"
                : $"s:{(l.SkuAtSale ?? string.Empty).Trim().ToLowerInvariant()}")
            .Select(g => new
            {
                shopifyVariantId = g.Select(x => x.ShopifyVariantId).FirstOrDefault(v => v.HasValue),
                shopifySku = g.Select(x => x.SkuAtSale).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                title = g.GroupBy(x => x.Description)
                    .OrderByDescending(gg => gg.Count())
                    .Select(gg => gg.Key)
                    .FirstOrDefault() ?? "Shopify item",
                qtySold = g.Sum(x => x.Quantity),
                revenue = g.Sum(x => x.LineTotal),
                orderCount = g.Select(x => x.InvoiceId).Distinct().Count()
            })
            .OrderByDescending(x => x.revenue)
            .ToList();

        return Ok(groups);
    }

    /// <summary>
    /// Link a Shopify sale item to a POS product and reclassify its past Shopify sales off the
    /// placeholder onto that product (recomputing cost for GP). Idempotent: re-linking the same
    /// variant is a no-op; linking a product already tied to a different variant returns 409.
    /// </summary>
    [HttpPost("link-variant")]
    public async Task<IActionResult> LinkVariant([FromBody] LinkVariantRequest req, CancellationToken ct = default)
    {
        if (req.ShopifyVariantId is null or 0 && string.IsNullOrWhiteSpace(req.ShopifySku))
            return BadRequest(new { error = "Provide a Shopify variant id or SKU to link." });

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == req.PosProductId, ct);
        if (product == null)
            return NotFound(new { error = "POS product not found." });

        if (req.ShopifyVariantId is > 0
            && product.ShopifyVariantId.HasValue
            && product.ShopifyVariantId.Value != req.ShopifyVariantId.Value)
        {
            return Conflict(new
            {
                error = $"'{product.Sku}' is already linked to a different Shopify variant ({product.ShopifyVariantId}). Unlink it first."
            });
        }

        var placeholder = await _db.Products
            .FirstOrDefaultAsync(p => p.Sku == ShopifyOrderImportService.UnlinkedPlaceholderSku, ct);

        var sku = req.ShopifySku?.Trim();
        var candidates = placeholder == null
            ? new List<InvoiceLine>()
            : await _db.InvoiceLines
                .Where(l => l.ProductId == placeholder.Id
                    && l.Invoice!.Source == "Shopify"
                    && ((req.ShopifyVariantId > 0 && l.ShopifyVariantId == req.ShopifyVariantId)
                        || (l.ShopifyVariantId == null && sku != null && l.SkuAtSale == sku)))
                .ToListAsync(ct);

        var costAtSale = Math.Round(product.Cost * (1 - product.SupplierDiscountPercent / 100m), 2);
        foreach (var line in candidates)
        {
            line.ProductId = product.Id;
            line.CostAtSale = costAtSale;
            if (req.ShopifyVariantId > 0) line.ShopifyVariantId = req.ShopifyVariantId;
        }

        if (req.ShopifyVariantId > 0)
        {
            product.ShopifyVariantId = req.ShopifyVariantId;
            product.ShopifySyncedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            posProductId = product.Id,
            product.Sku,
            linkedShopifyVariantId = req.ShopifyVariantId,
            reclassifiedLineCount = candidates.Count,
            note = "Linked. Past Shopify sales of this item now attribute to the POS product; future imports match it automatically."
        });
    }

    /// <summary>
    /// Dashboard KPIs, computed from the DB only (no Shopify call). Period figures (revenue, orders,
    /// units, average order value) respect the optional date range; the linked/unlinked backlog figures
    /// are all-time. Shipping lines are excluded from unit and unlinked counts. Owner/Dev only.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var invoices = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.Source == "Shopify" && i.Status == InvoiceStatus.Final)
            .ToListAsync(ct);

        IEnumerable<Invoice> period = invoices;
        if (from.HasValue) period = period.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) period = period.Where(i => i.CreatedAt <= to.Value);
        var periodList = period.ToList();

        // A real product line (not a shipping/fee line, which carries neither variant id nor SKU).
        static bool IsProductLine(InvoiceLine l) => !(l.ShopifyVariantId == null && string.IsNullOrEmpty(l.SkuAtSale));

        var revenue = periodList.Sum(i => i.GrandTotal);
        var orders = periodList.Count;
        var units = periodList.Sum(i => i.Lines.Where(IsProductLine).Sum(l => l.Quantity));
        var aov = orders > 0 ? Math.Round(revenue / orders, 2) : 0m;

        var linkedProducts = await _db.Products.CountAsync(p => p.ShopifyVariantId != null, ct);

        var placeholder = await _db.Products
            .FirstOrDefaultAsync(p => p.Sku == ShopifyOrderImportService.UnlinkedPlaceholderSku, ct);

        var unlinkedItems = 0;
        decimal unlinkedRevenue = 0m;
        object? topUnlinked = null;
        if (placeholder != null)
        {
            var plines = await _db.InvoiceLines
                .Where(l => l.ProductId == placeholder.Id
                    && l.Invoice!.Source == "Shopify"
                    && !(l.ShopifyVariantId == null && l.SkuAtSale == null))
                .Select(l => new { l.ShopifyVariantId, l.SkuAtSale, l.Description, l.LineTotal })
                .ToListAsync(ct);

            var groups = plines
                .GroupBy(l => l.ShopifyVariantId.HasValue
                    ? $"v:{l.ShopifyVariantId.Value}"
                    : $"s:{(l.SkuAtSale ?? string.Empty).Trim().ToLowerInvariant()}")
                .Select(g => new
                {
                    title = g.GroupBy(x => x.Description).OrderByDescending(gg => gg.Count())
                        .Select(gg => gg.Key).FirstOrDefault() ?? "Shopify item",
                    revenue = g.Sum(x => x.LineTotal)
                })
                .ToList();

            unlinkedItems = groups.Count;
            unlinkedRevenue = groups.Sum(g => g.revenue);
            var top = groups.OrderByDescending(g => g.revenue).FirstOrDefault();
            if (top != null) topUnlinked = new { top.title, top.revenue };
        }

        return Ok(new
        {
            from,
            to,
            revenue,
            orders,
            units,
            avgOrderValue = aov,
            linkedProducts,
            unlinkedItems,
            unlinkedRevenue,
            topUnlinked
        });
    }

    /// <summary>
    /// Every Shopify variant with its link status against the POS (linked = a POS product carries this
    /// variant id). Includes SKU and title for browsing/searching and to drive the link action. Calls
    /// Shopify, so the UI loads it on demand. Owner/Dev only.
    /// </summary>
    [HttpGet("variants")]
    public async Task<IActionResult> Variants(CancellationToken ct = default)
    {
        List<ShopifyVariantDetail> variants;
        try
        {
            variants = await _shopify.GetAllVariantDetailsAsync(ct);
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }

        var linkedByVariant = (await _db.Products
                .Where(p => p.ShopifyVariantId != null)
                .Select(p => new { VariantId = p.ShopifyVariantId!.Value, p.Sku })
                .ToListAsync(ct))
            .GroupBy(x => x.VariantId)
            .ToDictionary(g => g.Key, g => g.First().Sku);

        var rows = variants
            .Select(v => new
            {
                shopifyVariantId = v.VariantId,
                sku = v.Sku,
                title = v.Title,
                linked = linkedByVariant.ContainsKey(v.VariantId),
                posSku = linkedByVariant.TryGetValue(v.VariantId, out var s) ? s : null
            })
            .OrderBy(r => r.linked)
            .ThenBy(r => r.title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new
        {
            total = rows.Count,
            linked = rows.Count(r => r.linked),
            unlinked = rows.Count(r => !r.linked),
            items = rows
        });
    }

    /// <summary>
    /// Normalize a SKU for loose matching: uppercase, strip everything except letters/digits, then
    /// drop leading zeros. Turns "for-004351", "FOR004351" and "FOR4351" into the same key.
    /// </summary>
    private static string NormalizeSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return string.Empty;
        var chars = sku.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
        var cleaned = new string(chars).TrimStart('0');
        return cleaned;
    }
}

/// <summary>Request body for linking a Shopify sale item to a POS product.</summary>
public record LinkVariantRequest(long? ShopifyVariantId, string? ShopifySku, Guid PosProductId);
