using System.Security.Cryptography;
using System.Text;
using Consolidado.Api.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consolidado.Api.Controllers;

/// <summary>
/// Endpoint interno, chamado so pelo servico de lancamentos.
/// Protegido por chave de API porque nao ha usuario envolvido nessa chamada.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("internal/eventos")]
public class EventosController : ControllerBase
{
    private readonly IConsolidadoService _service;
    private readonly IConfiguration _config;

    public EventosController(IConsolidadoService service, IConfiguration config)
    {
        _service = service;
        _config = config;
    }

    [HttpPost]
    public async Task<IActionResult> Receber([FromBody] LancamentoRegistradoEvent evento, CancellationToken ct)
    {
        if (!ChaveValida())
            return Unauthorized();

        if (evento.Valor <= 0 || (evento.Tipo != "Credito" && evento.Tipo != "Debito"))
            return BadRequest(new { erro = "Evento inválido." });

        var aplicado = await _service.AplicarEventoAsync(evento, ct);

        // reenvio de um evento ja processado tambem e sucesso (idempotente)
        return aplicado ? Accepted() : Ok(new { mensagem = "Evento já processado." });
    }

    private bool ChaveValida()
    {
        var esperada = _config["Integracao:ApiKey"] ?? string.Empty;
        var recebida = Request.Headers["X-Api-Key"].ToString();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(esperada), Encoding.UTF8.GetBytes(recebida));
    }
}
