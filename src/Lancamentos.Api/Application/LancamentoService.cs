using Lancamentos.Api.Domain;
using Lancamentos.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Api.Application;

public record RegistrarLancamentoRequest(string Tipo, decimal Valor, string? Descricao, DateOnly? Data);

public record LancamentoResponse(Guid Id, string Tipo, decimal Valor, string Descricao, DateOnly Data, DateTime CriadoEmUtc)
{
    public static LancamentoResponse De(Lancamento l) =>
        new(l.Id, l.Tipo.ToString(), l.Valor, l.Descricao, l.Data, l.CriadoEmUtc);
}

public interface ILancamentoService
{
    Task<LancamentoResponse> RegistrarAsync(RegistrarLancamentoRequest request, CancellationToken ct);
    Task<IReadOnlyList<LancamentoResponse>> ListarPorDataAsync(DateOnly data, CancellationToken ct);
}

public class LancamentoService : ILancamentoService
{
    private readonly LancamentosDbContext _db;
    private readonly TimeProvider _relogio;
    private readonly RelogioNegocio _negocio;

    public LancamentoService(LancamentosDbContext db, TimeProvider relogio, RelogioNegocio negocio)
    {
        _db = db;
        _relogio = relogio;
        _negocio = negocio;
    }

    public async Task<LancamentoResponse> RegistrarAsync(RegistrarLancamentoRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<TipoLancamento>(request.Tipo, ignoreCase: true, out var tipo))
            throw new DominioException("Tipo inválido. Use 'Credito' ou 'Debito'.");

        var agora = _relogio.GetUtcNow().UtcDateTime;
        var data = request.Data ?? _negocio.Hoje();

        var lancamento = Lancamento.Criar(tipo, request.Valor, request.Descricao, data, agora);

        var evento = new LancamentoRegistradoEvent(
            Guid.NewGuid(), lancamento.Id, lancamento.Tipo.ToString(),
            lancamento.Valor, lancamento.Data, agora);

        // lancamento e outbox vao no mesmo SaveChanges (mesma transacao)
        _db.Lancamentos.Add(lancamento);
        _db.Outbox.Add(OutboxMessage.De(evento));
        await _db.SaveChangesAsync(ct);

        return LancamentoResponse.De(lancamento);
    }

    public async Task<IReadOnlyList<LancamentoResponse>> ListarPorDataAsync(DateOnly data, CancellationToken ct)
    {
        var itens = await _db.Lancamentos
            .AsNoTracking()
            .Where(l => l.Data == data)
            .OrderBy(l => l.CriadoEmUtc)
            .ToListAsync(ct);

        return itens.Select(LancamentoResponse.De).ToList();
    }
}
