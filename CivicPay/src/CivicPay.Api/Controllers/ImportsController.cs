using CivicPay.Application;
using CivicPay.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Api.Controllers;

[ApiController, Route("api/imports")]
public class ImportsController(IImportService service, CivicPayDbContext db) : ControllerBase
{
    /// <summary>Upload accounts or payments CSV. Rows are validated independently, with persisted rejection details.</summary>
    [HttpPost, RequestSizeLimit(2_100_000), ProducesResponseType<ReconciliationResult>(201), ProducesResponseType<ApiError>(400), ProducesResponseType<ApiError>(413)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string kind, CancellationToken ct)
    {
        Rules.Require(file.Length > 0, "INVALID_FILE_SIZE", "Upload a nonempty CSV.", 400);
        Rules.Require(file.Length <= 2_000_000, "IMPORT_TOO_LARGE", "CSV must not exceed 2 MB (2,000,000 bytes).", 413);
        await using var stream = file.OpenReadStream();
        var id = await service.ImportAsync(stream, file.FileName, kind, ct);
        return Created($"/api/imports/{id}/reconciliation", await service.ReconcileAsync(id, ct));
    }
    /// <summary>Retrieve totals, amount completeness, difference and reconciliation status.</summary>
    [HttpGet("{id:guid}/reconciliation")] public Task<ReconciliationResult> Reconcile(Guid id, CancellationToken ct) => service.ReconcileAsync(id, ct);
    /// <summary>Retrieve a batch's inspected rows. Maximum page size is 200.</summary>
    [HttpGet("{id:guid}/records")]
    public async Task<IActionResult> Records(Guid id, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var offset = Rules.PageOffset(page, pageSize);
        if (!await db.ImportBatches.AnyAsync(x => x.Id == id, ct))
            throw new BusinessException("BATCH_NOT_FOUND", "Import batch was not found.", 404);
        var q = db.ImportRecords.AsNoTracking().Where(x => x.ImportBatchId == id);
        return Ok(new
        {
            total = await q.CountAsync(ct),
            page,
            pageSize,
            items = await q.OrderBy(x => x.RowNumber).Skip(offset).Take(pageSize).Select(x => new { x.RowNumber, x.MunicipalityCode, x.SourceAmount, x.ImportedAmount, x.Status, x.ErrorCode, x.Message, x.TransactionId }).ToListAsync(ct)
        });
    }
}
