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
