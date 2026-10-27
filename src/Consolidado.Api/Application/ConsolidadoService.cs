using Consolidado.Api.Domain;
using Consolidado.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Consolidado.Api.Application;

public record LancamentoRegistradoEvent(
    Guid EventoId,
    Guid LancamentoId,
    string Tipo,
    decimal Valor,
    DateOnly Data,
    DateTime OcorridoEmUtc);

public record SaldoDiarioResponse(
    DateOnly Data,
    decimal TotalCreditos,
    decimal TotalDebitos,
    decimal Saldo,
    int QuantidadeLancamentos,
    DateTime? AtualizadoEmUtc);

public interface IConsolidadoService
{
    /// <returns>false quando o evento ja tinha sido processado antes.</returns>
    Task<bool> AplicarEventoAsync(LancamentoRegistradoEvent evento, CancellationToken ct);
    Task<SaldoDiarioResponse> ConsultarAsync(DateOnly data, CancellationToken ct);
}

public class ConsolidadoService : IConsolidadoService
{
    // SQLite so aceita um escritor por vez. Com varias requisicoes simultaneas fazendo
    // "le, soma, grava" no mesmo dia, o lock evita perda de atualizacao.
    // Num banco de verdade isso vira UPDATE atomico ou lock de linha (ver docs).
    private static readonly SemaphoreSlim _escrita = new(1, 1);
    private static readonly TimeSpan _validadeCache = TimeSpan.FromSeconds(30);

    private readonly ConsolidadoDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _relogio;

    public ConsolidadoService(ConsolidadoDbContext db, IMemoryCache cache, TimeProvider relogio)
    {
        _db = db;
        _cache = cache;
        _relogio = relogio;
    }

    public async Task<bool> AplicarEventoAsync(LancamentoRegistradoEvent evento, CancellationToken ct)
    {
        await _escrita.WaitAsync(ct);
        try
        {
            if (await _db.EventosProcessados.AnyAsync(e => e.Id == evento.EventoId, ct))
                return false;

            var agora = _relogio.GetUtcNow().UtcDateTime;

            var saldo = await _db.Saldos.FirstOrDefaultAsync(s => s.Data == evento.Data, ct);
            if (saldo is null)
            {
                saldo = SaldoDiario.Novo(evento.Data);
                _db.Saldos.Add(saldo);
            }

            saldo.Aplicar(evento.Tipo, evento.Valor, agora);
            _db.EventosProcessados.Add(new EventoProcessado { Id = evento.EventoId, ProcessadoEmUtc = agora });

            await _db.SaveChangesAsync(ct);

            _cache.Remove(ChaveCache(evento.Data));
            return true;
        }
        finally
        {
            _escrita.Release();
        }
    }

    public async Task<SaldoDiarioResponse> ConsultarAsync(DateOnly data, CancellationToken ct)
    {
        var resposta = await _cache.GetOrCreateAsync(ChaveCache(data), async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = _validadeCache;

            var saldo = await _db.Saldos.AsNoTracking().FirstOrDefaultAsync(s => s.Data == data, ct);

            // dia sem movimento nao e erro, so tem tudo zerado
            return saldo is null
                ? new SaldoDiarioResponse(data, 0, 0, 0, 0, null)
                : new SaldoDiarioResponse(saldo.Data, saldo.TotalCreditos, saldo.TotalDebitos,
                    saldo.Saldo, saldo.QuantidadeLancamentos, saldo.AtualizadoEmUtc);
        });

        return resposta!;
    }

    private static string ChaveCache(DateOnly data) => $"saldo:{data:yyyy-MM-dd}";
}
