using System.Text.Json;
using HuntexPos.Api.Data;
using HuntexPos.Api.Domain;
using HuntexPos.Api.DTOs;
using HuntexPos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HuntexPos.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Owner},{Roles.Dev}")]
public class ImportsController : ControllerBase
{
    private readonly ImportService _import;
    private readonly HuntexDbContext _db;

    public ImportsController(ImportService import, HuntexDbContext db)
    {
        _import = import;
        _db = db;
    }

    [HttpPost("huntex")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<object>> ImportHuntex(
        [FromForm] IFormFile file,
        [FromForm] string sheetName = "huntex 2026",
        [FromForm] Guid? supplierId = null,
        [FromForm] bool commit = false,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File required");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = file.OpenReadStream();
        var (rows, warnings) = ext == ".csv"
            ? await _import.PreviewHuntexCsvAsync(stream, supplierId, ct)
            : await _import.PreviewHuntexSheetAsync(stream, sheetName, supplierId, ct);
        if (!commit)
            return Ok(new { preview = rows, warnings });

        var valid = rows.Where(r => r.Error == null).ToList();
        var n = await _import.CommitHuntexPreviewAsync(valid, supplierId, ct);
        return Ok(new { imported = n, warnings });
    }

    [HttpPost("wholesaler")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<object>> ImportWholesaler(
        [FromForm] IFormFile file,
        [FromForm] Guid supplierId,
        [FromForm] string mappingJson,
        [FromForm] bool commit = false,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File required");
        var mapping = JsonSerializer.Deserialize<ColumnMappingDto>(mappingJson) ?? new ColumnMappingDto();

        await using var stream = file.OpenReadStream();
        var (rows, warnings) = await _import.PreviewWholesalerAsync(stream, file.FileName, supplierId, mapping, ct);
        if (!commit)
            return Ok(new { preview = rows, warnings });

        var valid = rows.Where(r => r.Error == null).ToList();
        var n = await _import.CommitWholesalerAsync(valid, supplierId, ct);
        return Ok(new { imported = n, warnings });
    }

    [HttpGet("presets")]
    public async Task<List<ImportPresetDto>> ListPresets([FromQuery] Guid? supplierId, CancellationToken ct)
    {
        var q = _db.ImportPresets.AsNoTracking().Include(p => p.Supplier).AsQueryable();
        if (supplierId.HasValue)
            q = q.Where(p => p.SupplierId == supplierId);
        var list = await q.OrderBy(p => p.Name).ToListAsync(ct);
        return list.Select(p => new ImportPresetDto
        {
            Id = p.Id,
            SupplierId = p.SupplierId,
            Name = p.Name,
            Mapping = ImportService.DeserializeMapping(p.ColumnMappingJson)
        }).ToList();
    }

    [HttpPost("presets")]
    public async Task<ImportPresetDto> SavePreset([FromBody] SaveImportPresetRequest req, CancellationToken ct)
    {
        var json = ImportService.SerializeMapping(req.Mapping);
        var preset = new ImportPreset
        {
            Id = Guid.NewGuid(),
            SupplierId = req.SupplierId,
            Name = req.Name.Trim(),
            ColumnMappingJson = json
        };
        _db.ImportPresets.Add(preset);
        await _db.SaveChangesAsync(ct);
        return new ImportPresetDto
        {
            Id = preset.Id,
            SupplierId = preset.SupplierId,
            Name = preset.Name,
            Mapping = req.Mapping
        };
    }

    [HttpDelete("presets/{id:guid}")]
    public async Task<IActionResult> DeletePreset(Guid id, CancellationToken ct)
    {
        var p = await _db.ImportPresets.FindAsync(new object[] { id }, ct);
        if (p == null) return NotFound();
        _db.ImportPresets.Remove(p);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
