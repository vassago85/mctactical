using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;

namespace HuntexPos.Api.Services;

/// <summary>
/// Wrapper over the Shopify <b>GraphQL</b> Admin API. The REST product/variant endpoints are a
/// legacy API (deprecated Oct 2024) and are unavailable to custom apps on organizations created
/// after April 2025, so all calls here use GraphQL. The POS is the source of truth: this client
/// only pushes products/prices/inventory up and reads back the ids Shopify assigns, plus reads
/// paid orders down for visibility. All calls fail fast with a descriptive
/// <see cref="ShopifyNotConfiguredException"/> when disabled or missing credentials.
/// </summary>
public class ShopifyClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ShopifyOptions _opt;
    private readonly ShopifyTokenProvider _tokens;

    public ShopifyClient(HttpClient http, IOptions<ShopifyOptions> opt, ShopifyTokenProvider tokens)
    {
        _http = http;
        _opt = opt.Value;
        _tokens = tokens;
    }

    public bool IsConfigured =>
        _opt.Enabled
        && !string.IsNullOrWhiteSpace(_opt.ShopDomain)
        && _tokens.HasCredentials;

    private string GraphQlUrl =>
        $"https://{_opt.ShopDomain.Trim().TrimEnd('/')}/admin/api/{_opt.ApiVersion}/graphql.json";

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new ShopifyNotConfiguredException(
                "Shopify integration is not configured. Set Shopify:Enabled, Shopify:ShopDomain and Shopify:AdminAccessToken.");
    }

    // --- GraphQL plumbing -------------------------------------------------

    /// <summary>Execute a GraphQL operation and return a detached clone of the "data" element.</summary>
    private async Task<JsonElement> GraphQlAsync(string query, object? variables, CancellationToken ct)
    {
        EnsureConfigured();

        var token = await _tokens.GetTokenAsync(ct);
        var req = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);
        req.Headers.Add("X-Shopify-Access-Token", token);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { query, variables }, Json), Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new ShopifyApiException((int)res.StatusCode, text);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
            throw new ShopifyApiException(200, errors.ToString());

        return root.GetProperty("data").Clone();
    }

    /// <summary>Throw if a mutation returned a non-empty userErrors array.</summary>
    private static void ThrowOnUserErrors(JsonElement mutationResult)
    {
        if (mutationResult.TryGetProperty("userErrors", out var ue)
            && ue.ValueKind == JsonValueKind.Array
            && ue.GetArrayLength() > 0)
            throw new ShopifyApiException(200, ue.ToString());
    }

    private static long ParseGidNumber(string? gid)
    {
        if (string.IsNullOrWhiteSpace(gid)) return 0;
        var slash = gid.LastIndexOf('/');
        var tail = slash >= 0 ? gid[(slash + 1)..] : gid;
        var q = tail.IndexOf('?');
        if (q >= 0) tail = tail[..q];
        return long.TryParse(tail, out var n) ? n : 0;
    }

    private static string ProductGid(long id) => $"gid://shopify/Product/{id}";
    private static string VariantGid(long id) => $"gid://shopify/ProductVariant/{id}";
    private static string InventoryItemGid(long id) => $"gid://shopify/InventoryItem/{id}";
    private static string LocationGid(long id) => $"gid://shopify/Location/{id}";

    /// <summary>Parse a plain Shopify money string (e.g. variant price or unit cost amount).</summary>
    private static decimal ParseMoney(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static decimal ParseMoney(JsonElement parent, string setProp)
    {
        if (!parent.TryGetProperty(setProp, out var set)) return 0m;
        if (!set.TryGetProperty("shopMoney", out var money)) return 0m;
        if (!money.TryGetProperty("amount", out var amt)) return 0m;
        return amt.ValueKind == JsonValueKind.String
               && decimal.TryParse(amt.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : 0m;
    }

    // --- Ping -------------------------------------------------------------

    /// <summary>Verify credentials and return the shop name plus available inventory locations.</summary>
    public async Task<ShopifyPingResult> PingAsync(CancellationToken ct)
    {
        const string query = """
            {
              shop { name }
              locations(first: 100) { edges { node { id name isActive } } }
            }
            """;
        var data = await GraphQlAsync(query, null, ct);

        var shopName = data.TryGetProperty("shop", out var shop) && shop.TryGetProperty("name", out var n)
            ? n.GetString() ?? _opt.ShopDomain
            : _opt.ShopDomain;

        var locations = new List<ShopifyLocation>();
        if (data.TryGetProperty("locations", out var locs) && locs.TryGetProperty("edges", out var edges))
        {
            foreach (var edge in edges.EnumerateArray())
            {
                var node = edge.GetProperty("node");
                locations.Add(new ShopifyLocation(
                    ParseGidNumber(node.GetProperty("id").GetString()),
                    node.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "",
                    node.TryGetProperty("isActive", out var la) && la.GetBoolean()));
            }
        }

        return new ShopifyPingResult(shopName, _opt.ApiVersion, _opt.LocationId, locations);
    }

    // --- Catalog reads ----------------------------------------------------

    /// <summary>Return the set of non-empty variant SKUs Shopify holds.</summary>
    public async Task<HashSet<string>> GetAllVariantSkusAsync(CancellationToken ct)
    {
        var variants = await GetAllVariantsAsync(ct);
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in variants)
            if (!string.IsNullOrWhiteSpace(v.Sku)) skus.Add(v.Sku);
        return skus;
    }

    /// <summary>
    /// Page through every product variant and return its SKU together with the Shopify ids the POS
    /// needs to link against (product, variant, inventory item). Blank-SKU variants are skipped.
    /// </summary>
    public async Task<List<ShopifyVariantRef>> GetAllVariantsAsync(CancellationToken ct)
    {
        const string query = """
            query($cursor: String) {
              productVariants(first: 250, after: $cursor) {
                edges {
                  node {
                    id
                    sku
                    inventoryItem { id }
                    product { id }
                  }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
            """;

        var result = new List<ShopifyVariantRef>();
        string? cursor = null;
        do
        {
            var data = await GraphQlAsync(query, new { cursor }, ct);
            var conn = data.GetProperty("productVariants");
            foreach (var edge in conn.GetProperty("edges").EnumerateArray())
            {
                var node = edge.GetProperty("node");
                var sku = node.TryGetProperty("sku", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(sku)) continue;

                var variantId = ParseGidNumber(node.GetProperty("id").GetString());
                var productId = node.TryGetProperty("product", out var prod)
                    ? ParseGidNumber(prod.GetProperty("id").GetString())
                    : 0;
                var inventoryItemId = node.TryGetProperty("inventoryItem", out var inv)
                    ? ParseGidNumber(inv.GetProperty("id").GetString())
                    : 0;

                result.Add(new ShopifyVariantRef(sku.Trim(), productId, variantId, inventoryItemId));
            }

            var pageInfo = conn.GetProperty("pageInfo");
            cursor = pageInfo.GetProperty("hasNextPage").GetBoolean()
                ? pageInfo.GetProperty("endCursor").GetString()
                : null;
        }
        while (cursor != null);

        return result;
    }

    /// <summary>
    /// Fetch every Shopify variant with barcode and product title for match analysis. Blank-SKU
    /// variants are kept (they may still carry a barcode). Read-only; used by diagnostics only.
    /// </summary>
    public async Task<List<ShopifyVariantDetail>> GetAllVariantDetailsAsync(CancellationToken ct)
    {
        const string query = """
            query($cursor: String) {
              productVariants(first: 250, after: $cursor) {
                edges {
                  node {
                    id
                    sku
                    barcode
                    title
                    price
                    compareAtPrice
                    inventoryItem { id unitCost { amount } }
                    product { id title vendor productType }
                  }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
            """;

        var result = new List<ShopifyVariantDetail>();
        string? cursor = null;
        do
        {
            var data = await GraphQlAsync(query, new { cursor }, ct);
            var conn = data.GetProperty("productVariants");
            foreach (var edge in conn.GetProperty("edges").EnumerateArray())
            {
                var node = edge.GetProperty("node");
                var sku = node.TryGetProperty("sku", out var s) ? s.GetString() : null;
                var barcode = node.TryGetProperty("barcode", out var b) ? b.GetString() : null;
                var variantTitle = node.TryGetProperty("title", out var vt) ? vt.GetString() : null;
                var price = node.TryGetProperty("price", out var pr) ? ParseMoney(pr.GetString()) : 0m;
                decimal? compareAt = null;
                if (node.TryGetProperty("compareAtPrice", out var cap) && cap.ValueKind == JsonValueKind.String)
                {
                    var c = ParseMoney(cap.GetString());
                    if (c > 0) compareAt = c;
                }

                var variantId = ParseGidNumber(node.GetProperty("id").GetString());
                long productId = 0;
                var productTitle = "";
                string? vendor = null;
                string? productType = null;
                if (node.TryGetProperty("product", out var prod))
                {
                    productId = ParseGidNumber(prod.GetProperty("id").GetString());
                    productTitle = prod.TryGetProperty("title", out var pt) ? pt.GetString() ?? "" : "";
                    vendor = prod.TryGetProperty("vendor", out var vn) ? vn.GetString() : null;
                    productType = prod.TryGetProperty("productType", out var ptype) ? ptype.GetString() : null;
                }

                long inventoryItemId = 0;
                decimal cost = 0m;
                if (node.TryGetProperty("inventoryItem", out var inv))
                {
                    inventoryItemId = ParseGidNumber(inv.GetProperty("id").GetString());
                    if (inv.TryGetProperty("unitCost", out var uc) && uc.ValueKind == JsonValueKind.Object)
                        cost = ParseMoney(uc.TryGetProperty("amount", out var amt) ? amt.GetString() : null);
                }

                result.Add(new ShopifyVariantDetail(
                    string.IsNullOrWhiteSpace(sku) ? null : sku.Trim(),
                    string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
                    productTitle,
                    productId,
                    variantId,
                    inventoryItemId,
                    price,
                    cost,
                    string.IsNullOrWhiteSpace(vendor) ? null : vendor!.Trim(),
                    string.IsNullOrWhiteSpace(productType) ? null : productType!.Trim(),
                    string.IsNullOrWhiteSpace(variantTitle) ? null : variantTitle!.Trim(),
                    compareAt));
            }

            var pageInfo = conn.GetProperty("pageInfo");
            cursor = pageInfo.GetProperty("hasNextPage").GetBoolean()
                ? pageInfo.GetProperty("endCursor").GetString()
                : null;
        }
        while (cursor != null);

        return result;
    }

    // --- Product push -----------------------------------------------------

    /// <summary>
    /// Push a single POS product up to Shopify. Creates a new product when unlinked, or updates the
    /// existing variant (price/sku/barcode) when already mapped. Inventory is set separately.
    /// </summary>
    public async Task<ShopifyPushResult> PushProductAsync(Product p, CancellationToken ct)
    {
        if (p.ShopifyProductId.HasValue && p.ShopifyVariantId.HasValue)
            return await UpdateExistingAsync(p, ct);
        return await CreateNewAsync(p, ct);
    }

    private async Task<ShopifyPushResult> CreateNewAsync(Product p, CancellationToken ct)
    {
        const string createMutation = """
            mutation productCreate($input: ProductInput!) {
              productCreate(input: $input) {
                product {
                  id
                  variants(first: 1) { nodes { id inventoryItem { id } } }
                }
                userErrors { field message }
              }
            }
            """;

        var input = new
        {
            title = p.Name,
            descriptionHtml = p.Description,
            vendor = string.IsNullOrWhiteSpace(p.Manufacturer) ? null : p.Manufacturer,
            productType = string.IsNullOrWhiteSpace(p.ItemType) ? null : p.ItemType,
            tags = BuildTags(p),
            status = p.Active ? "ACTIVE" : "DRAFT"
        };

        var data = await GraphQlAsync(createMutation, new { input }, ct);
        var productCreate = data.GetProperty("productCreate");
        ThrowOnUserErrors(productCreate);

        var product = productCreate.GetProperty("product");
        var productId = ParseGidNumber(product.GetProperty("id").GetString());
        var defaultVariant = product.GetProperty("variants").GetProperty("nodes").EnumerateArray().First();
        var variantId = ParseGidNumber(defaultVariant.GetProperty("id").GetString());
        var inventoryItemId = ParseGidNumber(
            defaultVariant.GetProperty("inventoryItem").GetProperty("id").GetString());

        // Set SKU/price/barcode on the default variant and enable inventory tracking.
        var updated = await BulkUpdateVariantAsync(productId, variantId, p, ct);
        return new ShopifyPushResult(productId, variantId,
            updated.InventoryItemId != 0 ? updated.InventoryItemId : inventoryItemId);
    }

    private async Task<ShopifyPushResult> UpdateExistingAsync(Product p, CancellationToken ct)
    {
        await UpdateProductCategoryAsync(p.ShopifyProductId!.Value, p, ct);
        var result = await BulkUpdateVariantAsync(p.ShopifyProductId!.Value, p.ShopifyVariantId!.Value, p, ct);
        return new ShopifyPushResult(
            p.ShopifyProductId!.Value,
            p.ShopifyVariantId!.Value,
            result.InventoryItemId != 0 ? result.InventoryItemId : (p.ShopifyInventoryItemId ?? 0));
    }

    /// <summary>
    /// Sync a linked product's categorization to Shopify (product-level fields only): tags built from
    /// the POS Category/Manufacturer/ItemType, plus productType and vendor. Keeps Shopify grouping and
    /// filtering aligned with the POS. Used both when pushing a product and by the bulk tag sync.
    /// </summary>
    public async Task UpdateProductCategoryAsync(long shopifyProductId, Product p, CancellationToken ct)
    {
        const string mutation = """
            mutation productUpdate($input: ProductInput!) {
              productUpdate(input: $input) {
                product { id }
                userErrors { field message }
              }
            }
            """;

        var input = new
        {
            id = ProductGid(shopifyProductId),
            tags = BuildTags(p),
            productType = string.IsNullOrWhiteSpace(p.ItemType) ? null : p.ItemType,
            vendor = string.IsNullOrWhiteSpace(p.Manufacturer) ? null : p.Manufacturer
        };

        var data = await GraphQlAsync(mutation, new { input }, ct);
        ThrowOnUserErrors(data.GetProperty("productUpdate"));
    }

    /// <summary>Distinct, non-empty tags for a product: its Category, Manufacturer and ItemType.</summary>
    private static string[] BuildTags(Product p) =>
        new[] { p.Category, p.Manufacturer, p.ItemType }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<ShopifyPushResult> BulkUpdateVariantAsync(long productId, long variantId, Product p, CancellationToken ct)
    {
        const string mutation = """
            mutation bulkUpdate($productId: ID!, $variants: [ProductVariantsBulkInput!]!) {
              productVariantsBulkUpdate(productId: $productId, variants: $variants) {
                productVariants { id inventoryItem { id } }
                userErrors { field message }
              }
            }
            """;

        var variables = new
        {
            productId = ProductGid(productId),
            variants = new[]
            {
                new
                {
                    id = VariantGid(variantId),
                    price = p.SellPrice.ToString(CultureInfo.InvariantCulture),
                    barcode = string.IsNullOrWhiteSpace(p.Barcode) ? null : p.Barcode,
                    inventoryItem = new { sku = p.Sku, tracked = true }
                }
            }
        };

        var data = await GraphQlAsync(mutation, variables, ct);
        var bulk = data.GetProperty("productVariantsBulkUpdate");
        ThrowOnUserErrors(bulk);

        var variant = bulk.GetProperty("productVariants").EnumerateArray().First();
        var inventoryItemId = variant.TryGetProperty("inventoryItem", out var inv)
            ? ParseGidNumber(inv.GetProperty("id").GetString())
            : 0;
        return new ShopifyPushResult(productId, variantId, inventoryItemId);
    }

    /// <summary>
    /// Update only the sell price of an already-linked Shopify variant. Deliberately touches nothing
    /// else — no SKU, barcode, inventory tracking or quantity — so pushing POS prices can never alter
    /// online stock or availability. Used by the bulk "push prices" action.
    /// </summary>
    public async Task UpdateVariantPriceAsync(long productId, long variantId, decimal price, CancellationToken ct)
    {
        const string mutation = """
            mutation priceUpdate($productId: ID!, $variants: [ProductVariantsBulkInput!]!) {
              productVariantsBulkUpdate(productId: $productId, variants: $variants) {
                userErrors { field message }
              }
            }
            """;

        var variables = new
        {
            productId = ProductGid(productId),
            variants = new[]
            {
                new { id = VariantGid(variantId), price = price.ToString(CultureInfo.InvariantCulture) }
            }
        };

        var data = await GraphQlAsync(mutation, variables, ct);
        ThrowOnUserErrors(data.GetProperty("productVariantsBulkUpdate"));
    }

    /// <summary>Set the absolute available quantity for an inventory item at the configured location.</summary>
    public async Task SetInventoryAsync(long inventoryItemId, int available, CancellationToken ct)
    {
        EnsureConfigured();
        if (_opt.LocationId is not { } locationId)
            throw new ShopifyNotConfiguredException(
                "Shopify:LocationId is not set. Inventory cannot be pushed until a location is chosen.");

        const string mutation = """
            mutation inventorySetQuantities($input: InventorySetQuantitiesInput!) {
              inventorySetQuantities(input: $input) {
                userErrors { field message }
              }
            }
            """;

        var variables = new
        {
            input = new
            {
                name = "available",
                reason = "correction",
                ignoreCompareQuantity = true,
                quantities = new[]
                {
                    new
                    {
                        inventoryItemId = InventoryItemGid(inventoryItemId),
                        locationId = LocationGid(locationId),
                        quantity = available
                    }
                }
            }
        };

        var data = await GraphQlAsync(mutation, variables, ct);
        ThrowOnUserErrors(data.GetProperty("inventorySetQuantities"));
    }

    // --- Orders read ------------------------------------------------------

    /// <summary>
    /// Fetch recent paid orders (newest first) with the fields needed to represent them as POS
    /// invoices. Pages until <paramref name="maxOrders"/> is reached or Shopify runs out.
    /// </summary>
    public async Task<List<ShopifyOrder>> GetPaidOrdersAsync(int maxOrders, CancellationToken ct)
    {
        const string query = """
            query($cursor: String, $q: String) {
              orders(first: 100, after: $cursor, query: $q, sortKey: CREATED_AT, reverse: true) {
                edges {
                  node {
                    id
                    name
                    createdAt
                    email
                    customer { firstName lastName }
                    subtotalPriceSet { shopMoney { amount } }
                    totalTaxSet { shopMoney { amount } }
                    totalDiscountsSet { shopMoney { amount } }
                    totalPriceSet { shopMoney { amount } }
                    totalShippingPriceSet { shopMoney { amount } }
                    shippingLines(first: 1) { edges { node { title } } }
                    lineItems(first: 100) {
                      edges {
                        node {
                          title
                          variantTitle
                          quantity
                          sku
                          variant { id }
                          originalUnitPriceSet { shopMoney { amount } }
                        }
                      }
                    }
                  }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
            """;

        var orders = new List<ShopifyOrder>();
        string? cursor = null;
        do
        {
            var data = await GraphQlAsync(query, new { cursor, q = "financial_status:paid" }, ct);
            var conn = data.GetProperty("orders");
            foreach (var edge in conn.GetProperty("edges").EnumerateArray())
            {
                orders.Add(ParseOrder(edge.GetProperty("node")));
                if (orders.Count >= maxOrders) return orders;
            }

            var pageInfo = conn.GetProperty("pageInfo");
            cursor = pageInfo.GetProperty("hasNextPage").GetBoolean()
                ? pageInfo.GetProperty("endCursor").GetString()
                : null;
        }
        while (cursor != null);

        return orders;
    }

    /// <summary>
    /// Build the display title for a line item: product title plus the variant title when it is a real
    /// variant (Shopify uses "Default Title" for products that have none, which we drop). Matches what
    /// the Shopify order screen shows, e.g. "ULTRA MICROMETER SEATER DIE – 6 DASHER".
    /// </summary>
    private static string ComposeLineTitle(string title, string? variantTitle)
    {
        var variant = variantTitle?.Trim();
        if (string.IsNullOrWhiteSpace(variant) || variant.Equals("Default Title", StringComparison.OrdinalIgnoreCase))
            return title;
        return string.IsNullOrWhiteSpace(title) ? variant : $"{title} \u2013 {variant}";
    }

    private static ShopifyOrder ParseOrder(JsonElement o)
    {
        var lineItems = new List<ShopifyOrderLine>();
        if (o.TryGetProperty("lineItems", out var items) && items.TryGetProperty("edges", out var itemEdges))
        {
            foreach (var edge in itemEdges.EnumerateArray())
            {
                var li = edge.GetProperty("node");
                var variantId = li.TryGetProperty("variant", out var variant) && variant.ValueKind == JsonValueKind.Object
                    ? ParseGidNumber(variant.GetProperty("id").GetString())
                    : (long?)null;
                var title = li.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var variantTitle = li.TryGetProperty("variantTitle", out var vt) ? vt.GetString() : null;
                lineItems.Add(new ShopifyOrderLine(
                    Sku: li.TryGetProperty("sku", out var sku) ? sku.GetString() : null,
                    Title: ComposeLineTitle(title, variantTitle),
                    VariantId: variantId is > 0 ? variantId : null,
                    Quantity: li.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0,
                    Price: ParseMoney(li, "originalUnitPriceSet")));
            }
        }

        string? customerName = null;
        if (o.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            var first = c.TryGetProperty("firstName", out var fn) ? fn.GetString() : null;
            var last = c.TryGetProperty("lastName", out var ln) ? ln.GetString() : null;
            customerName = string.Join(" ", new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(customerName)) customerName = null;
        }

        string? shippingTitle = null;
        if (o.TryGetProperty("shippingLines", out var sl) && sl.TryGetProperty("edges", out var slEdges))
        {
            foreach (var edge in slEdges.EnumerateArray())
            {
                var node = edge.GetProperty("node");
                shippingTitle = node.TryGetProperty("title", out var st) ? st.GetString() : null;
                break;
            }
        }

        return new ShopifyOrder(
            Id: ParseGidNumber(o.GetProperty("id").GetString()),
            Name: o.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
            CreatedAt: o.TryGetProperty("createdAt", out var ca) && ca.TryGetDateTimeOffset(out var dto)
                ? dto
                : DateTimeOffset.UtcNow,
            CustomerName: customerName,
            Email: o.TryGetProperty("email", out var em) ? em.GetString() : null,
            SubtotalPrice: ParseMoney(o, "subtotalPriceSet"),
            TotalTax: ParseMoney(o, "totalTaxSet"),
            TotalDiscounts: ParseMoney(o, "totalDiscountsSet"),
            TotalPrice: ParseMoney(o, "totalPriceSet"),
            TotalShipping: ParseMoney(o, "totalShippingPriceSet"),
            ShippingTitle: string.IsNullOrWhiteSpace(shippingTitle) ? null : shippingTitle.Trim(),
            Lines: lineItems);
    }
}

public record ShopifyLocation(long Id, string Name, bool Active);

public record ShopifyVariantRef(string Sku, long ProductId, long VariantId, long InventoryItemId);

/// <summary>
/// A Shopify variant with the extra fields needed to reason about matching quality: barcode and the
/// parent product title. Unlike <see cref="ShopifyVariantRef"/>, blank-SKU variants are included so
/// barcode-only matches can be discovered. Used by the read-only match-analysis diagnostic.
/// </summary>
public record ShopifyVariantDetail(
    string? Sku,
    string? Barcode,
    string Title,
    long ProductId,
    long VariantId,
    long InventoryItemId,
    decimal Price = 0m,
    decimal Cost = 0m,
    string? Vendor = null,
    string? ProductType = null,
    string? VariantTitle = null,
    decimal? CompareAtPrice = null);

public record ShopifyPingResult(
    string ShopName,
    string ApiVersion,
    long? ConfiguredLocationId,
    IReadOnlyList<ShopifyLocation> Locations);

public record ShopifyPushResult(long ProductId, long VariantId, long InventoryItemId);

public record ShopifyOrderLine(string? Sku, string Title, long? VariantId, int Quantity, decimal Price);

public record ShopifyOrder(
    long Id,
    string Name,
    DateTimeOffset CreatedAt,
    string? CustomerName,
    string? Email,
    decimal SubtotalPrice,
    decimal TotalTax,
    decimal TotalDiscounts,
    decimal TotalPrice,
    decimal TotalShipping,
    string? ShippingTitle,
    IReadOnlyList<ShopifyOrderLine> Lines);

public class ShopifyNotConfiguredException(string message) : Exception(message);

public class ShopifyApiException(int statusCode, string body)
    : Exception($"Shopify API returned {statusCode}: {body}")
{
    public int StatusCode { get; } = statusCode;
}
