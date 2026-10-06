using CivicPay.Application;
using CivicPay.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Api.Controllers;

[ApiController, Route("api/payments")]
public class PaymentsController(IPaymentService service, CivicPayDbContext db) : ControllerBase
{
    /// <summary>Accept a synthetic payment. Identical retries return 200 and replayed=true; new records return 201.</summary>
    [HttpPost, ProducesResponseType<PaymentResponse>(201), ProducesResponseType<PaymentResponse>(200), ProducesResponseType<ApiError>(400), ProducesResponseType<ApiError>(404), ProducesResponseType<ApiError>(409), ProducesResponseType<ApiError>(422)]
    public async Task<IActionResult> Post(PaymentRequest request, CancellationToken ct)
    {
        HttpContext.Items["MunicipalityCode"] = Rules.Code(request.MunicipalityCode);
        var result = await service.AcceptAsync(request, null, ct);
        return result.Replayed ? Ok(result) : CreatedAtAction(nameof(Get), new
        {
            id = result.TransactionId
        }, result);
    }
    /// <summary>Retrieve an accepted transaction by its identifier.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await db.Transactions.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, municipalityCode = x.Municipality.Code, accountNumber = x.Account.AccountNumber, paymentType = x.PaymentType.Code, x.Amount, x.TransactionDate, x.ExternalReference, x.Status, x.ImportBatchId, x.CreatedAt }).SingleOrDefaultAsync(ct);
        if (p == null)
            throw new BusinessException("TRANSACTION_NOT_FOUND", "Transaction was not found.", 404);
        HttpContext.Items["MunicipalityCode"] = p.municipalityCode;
        return Ok(p);
    }
}
