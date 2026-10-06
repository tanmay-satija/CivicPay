using CivicPay.Application;
using Microsoft.AspNetCore.Mvc;
namespace CivicPay.Api.Controllers;

[ApiController, Route("api/municipalities")]
public class ConfigurationController(IConfigurationService service) : ControllerBase
{
    /// <summary>List municipality configurations and enabled payment types.</summary>
    [HttpGet] public Task<List<ConfigurationResponse>> List(CancellationToken ct) => service.ListAsync(ct);
    /// <summary>Retrieve a municipality configuration including its update version.</summary>
    [HttpGet("{code}")]
    public Task<ConfigurationResponse> Get(string code, CancellationToken ct)
    {
        HttpContext.Items["MunicipalityCode"] = Rules.Code(code);
        return service.GetAsync(code, ct);
    }
    /// <summary>Retrieve payment types enabled for this municipality.</summary>
    [HttpGet("{code}/payment-types")] public async Task<string[]> Types(string code, CancellationToken ct) => (await Get(code, ct)).AcceptedPaymentTypes;
    /// <summary>Create a municipality. Currency must be CAD; minimum must be positive.</summary>
    [HttpPost, ProducesResponseType<ConfigurationResponse>(201), ProducesResponseType<ApiError>(422), ProducesResponseType<ApiError>(409)]
    public async Task<IActionResult> Create(ConfigurationRequest request, CancellationToken ct)
    {
        HttpContext.Items["MunicipalityCode"] = Rules.Code(request.MunicipalityCode);
        var result = await service.SaveAsync(request, true, ct);
        return CreatedAtAction(nameof(Get), new
        {
            code = result.MunicipalityCode
        }, result);
    }
    /// <summary>Update configuration. Supply the version returned by GET; stale updates return 409.</summary>
    [HttpPut("{code}"), ProducesResponseType<ConfigurationResponse>(200), ProducesResponseType<ApiError>(409), ProducesResponseType<ApiError>(422)]
    public async Task<ConfigurationResponse> Update(string code, ConfigurationRequest request, CancellationToken ct)
    {
        Rules.Require(Rules.Code(code) == Rules.Code(request.MunicipalityCode), "CODE_MISMATCH", "Route and request municipality codes must match.", 400);
        HttpContext.Items["MunicipalityCode"] = Rules.Code(code);
        return await service.SaveAsync(request, false, ct);
    }
}
