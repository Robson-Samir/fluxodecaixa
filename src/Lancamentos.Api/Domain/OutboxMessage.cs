using System.Text.Json;

namespace Lancamentos.Api.Domain;

/// <summary>
/// Evento publicado para o servico de consolidado. O EventoId serve de chave de
/// idempotencia do lado de la, ja que a entrega e "pelo menos uma vez".
/// </summary>
public record LancamentoRegistradoEvent(
    Guid EventoId,
    Guid LancamentoId,
    string Tipo,
    decimal Valor,
    DateOnly Data,
    DateTime OcorridoEmUtc);

/// <summary>
/// Linha da tabela de outbox. Gravada na mesma transacao do lancamento,
/// entao nunca perdemos um evento mesmo que o consolidado esteja fora do ar.
/// </summary>
public class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public DateTime CriadoEmUtc { get; private set; }
    public DateTime ProximaTentativaEmUtc { get; private set; }
    public DateTime? ProcessadoEmUtc { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }

    public static OutboxMessage De(LancamentoRegistradoEvent evento) => new()
    {
        Id = evento.EventoId,
        Payload = JsonSerializer.Serialize(evento),
        CriadoEmUtc = evento.OcorridoEmUtc,
        ProximaTentativaEmUtc = evento.OcorridoEmUtc
    };

    public void MarcarComoProcessada(DateTime agoraUtc)
    {
        ProcessadoEmUtc = agoraUtc;
        UltimoErro = null;
    }

    public void RegistrarFalha(string erro, DateTime agoraUtc)
    {
        Tentativas++;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;

        // backoff exponencial limitado a 1 minuto: 2s, 4s, 8s ... 60s
        var segundos = Math.Min(Math.Pow(2, Math.Min(Tentativas, 6)), 60);
        ProximaTentativaEmUtc = agoraUtc.AddSeconds(segundos);
    }
}
