using Consolidado.Api.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consolidado.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/consolidado")]
public class ConsolidadoController : ControllerBase
{
    private readonly IConsolidadoService _service;

    public ConsolidadoController(IConsolidadoService service) => _service = service;

    [HttpGet("{data}")]
    [ProducesResponseType<SaldoDiarioResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Consultar(DateOnly data, CancellationToken ct)
        => Ok(await _service.ConsultarAsync(data, ct));
}
