using Lancamentos.Api.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lancamentos.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/lancamentos")]
public class LancamentosController : ControllerBase
{
    private readonly ILancamentoService _service;
    private readonly RelogioNegocio _negocio;

    public LancamentosController(ILancamentoService service, RelogioNegocio negocio)
    {
        _service = service;
        _negocio = negocio;
    }

    [HttpPost]
    [ProducesResponseType<LancamentoResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Registrar([FromBody] RegistrarLancamentoRequest request, CancellationToken ct)
    {
        var criado = await _service.RegistrarAsync(request, ct);
        return CreatedAtAction(nameof(Listar), new { data = criado.Data.ToString("yyyy-MM-dd") }, criado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] DateOnly? data, CancellationToken ct)
    {
        var dia = data ?? _negocio.Hoje();
        return Ok(await _service.ListarPorDataAsync(dia, ct));
    }
}
