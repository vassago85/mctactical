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
    /// Initial two-way reconcile (link-only). Matches POS products to Shopify variants by SKU and
    /// stores the Shopify ids on the POS product WITHOUT overwriting any field values. Defaults to a
    /// dry-run preview; pass <c>apply=true</c> to persist the links. Shopify-only items are reported,
    /// not imported. SKUs that appear on more than one Shopify variant are skipped as ambiguous.
    /// </summary>
    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile([FromQuery] bool apply = false, CancellationToken ct = default)
    {
        List<ShopifyVariantRef> shopifyVariants;
        try
        {
            shopifyVariants = await _shopify.GetAllVariantsAsync(ct);
        }
        catch (ShopifyNotConfiguredException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ShopifyApiException ex)
        {
            return StatusCode(502, new { error = "Shopify rejected the request.", status = ex.StatusCode, detail = ex.Message });
        }

        // Group Shopify variants by SKU so we can detect (and skip) ambiguous duplicates.
        var shopifyBySku = shopifyVariants
            .GroupBy(v => v.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var ambiguousSkus = shopifyBySku.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToList();

        var products = await _db.Products
            .Where(p => p.Active && p.Sku != null && p.Sku != "")
            .ToListAsync(ct);

        var linked = new List<string>();
        var alreadyLinked = new List<string>();
        var ambiguousMatched = new List<string>();
        var posOnly = new List<string>();

        foreach (var p in products)
        {
            var sku = p.Sku.Trim();
            if (!shopifyBySku.TryGetValue(sku, out var variants))
            {
                posOnly.Add(sku);
                continue;
            }

            if (variants.Count > 1)
            {
                ambiguousMatched.Add(sku);
                continue;
            }

            if (p.ShopifyVariantId.HasValue)
            {
                alreadyLinked.Add(sku);
                continue;
            }

            var v = variants[0];
            if (apply)
            {
                p.ShopifyProductId = v.ProductId;
                p.ShopifyVariantId = v.VariantId;
                p.ShopifyInventoryItemId = v.InventoryItemId == 0 ? null : v.InventoryItemId;
                p.ShopifySyncedAt = DateTimeOffset.UtcNow;
            }
            linked.Add(sku);
        }

        if (apply && linked.Count > 0)
            await _db.SaveChangesAsync(ct);

        var posSkuSet = new HashSet<string>(products.Select(p => p.Sku.Trim()), StringComparer.OrdinalIgnoreCase);
        var shopifyOnly = shopifyBySku.Keys.Where(s => !posSkuSet.Contains(s)).ToList();

        return Ok(new
        {
            applied = apply,
            linkedCount = linked.Count,
            alreadyLinkedCount = alreadyLinked.Count,
            posOnlyCount = posOnly.Count,
            shopifyOnlyCount = shopifyOnly.Count,
            ambiguousSkuCount = ambiguousSkus.Count,
            ambiguousMatchedCount = ambiguousMatched.Count,
            sampleLinked = linked.Take(25).ToList(),
            samplePosOnly = posOnly.Take(25).ToList(),
            sampleShopifyOnly = shopifyOnly.Take(25).ToList(),
            sampleAmbiguous = ambiguousMatched.Take(25).ToList(),
            note = apply
                ? "Links saved. No product fields were overwritten. Shopify-only items were not imported."
                : "Preview only \u2014 nothing was saved. Re-run with ?apply=true to persist the links."
        });
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
                summary.SkippedExistingCount,
                summary.MatchedLineCount,
                summary.UnmatchedLineCount,
                summary.OrdersWithUnmatchedLines,
                summary.SampleImported,
                note = summary.Applied
                    ? "Imported as invoices tagged 'Shopify'. POS stock was not changed. Unmatched line items were skipped \u2014 run reconcile/link first to improve coverage."
                    : "Preview only \u2014 nothing was saved. Re-run with ?apply=true to import these orders."
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
}
