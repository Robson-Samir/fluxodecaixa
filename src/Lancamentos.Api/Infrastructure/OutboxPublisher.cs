using System.Net.Http.Json;
using System.Text.Json;
using Lancamentos.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Api.Infrastructure;

/// <summary>
/// Abstracao do transporte. Hoje e HTTP; em producao seria trocado por
/// RabbitMQ/Kafka/Service Bus sem mexer no resto (ver ADR-003).
/// </summary>
public interface IEventPublisher
{
    Task PublicarAsync(LancamentoRegistradoEvent evento, CancellationToken ct);
}

public class HttpEventPublisher : IEventPublisher
{
    private readonly HttpClient _http;

    public HttpEventPublisher(HttpClient http) => _http = http;

    public async Task PublicarAsync(LancamentoRegistradoEvent evento, CancellationToken ct)
    {
        var resposta = await _http.PostAsJsonAsync("internal/eventos", evento, ct);
        resposta.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Le a outbox e entrega os eventos ao consolidado. Se ele estiver fora,
/// os eventos ficam acumulados e sao reenviados com backoff.
/// </summary>
public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _relogio;
    private readonly ILogger<OutboxWorker> _log;
    private readonly TimeSpan _intervalo;

    public OutboxWorker(IServiceScopeFactory scopes, TimeProvider relogio, IConfiguration config, ILogger<OutboxWorker> log)
    {
        _scopes = scopes;
        _relogio = relogio;
        _log = log;
        _intervalo = TimeSpan.FromMilliseconds(config.GetValue("Outbox:IntervaloMs", 1000));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarLoteAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Erro inesperado ao processar a outbox");
            }

            await Task.Delay(_intervalo, stoppingToken).ContinueWith(_ => { });
        }
    }

    public async Task ProcessarLoteAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var agora = _relogio.GetUtcNow().UtcDateTime;

        var pendentes = await db.Outbox
            .Where(m => m.ProcessadoEmUtc == null && m.ProximaTentativaEmUtc <= agora)
            .OrderBy(m => m.CriadoEmUtc)
            .Take(50)
            .ToListAsync(ct);

        foreach (var msg in pendentes)
        {
            try
            {
                var evento = JsonSerializer.Deserialize<LancamentoRegistradoEvent>(msg.Payload)!;
                await publisher.PublicarAsync(evento, ct);
                msg.MarcarComoProcessada(_relogio.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                msg.RegistrarFalha(ex.Message, _relogio.GetUtcNow().UtcDateTime);
                _log.LogWarning("Consolidado indisponível (evento {Id}, tentativa {N}): {Erro}",
                    msg.Id, msg.Tentativas, ex.Message);

                // consolidado fora: salva o que ja foi e espera o proximo ciclo,
                // sem martelar o servico com o resto do lote
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        if (pendentes.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
