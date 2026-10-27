namespace Consolidado.Api.Domain;

public class SaldoDiario
{
    private SaldoDiario() { }

    public DateOnly Data { get; private set; }
    public decimal TotalCreditos { get; private set; }
    public decimal TotalDebitos { get; private set; }
    public int QuantidadeLancamentos { get; private set; }
    public DateTime AtualizadoEmUtc { get; private set; }

    public decimal Saldo => TotalCreditos - TotalDebitos;

    public static SaldoDiario Novo(DateOnly data) => new() { Data = data };

    public void Aplicar(string tipo, decimal valor, DateTime agoraUtc)
    {
        switch (tipo)
        {
            case "Credito":
                TotalCreditos += valor;
                break;
            case "Debito":
                TotalDebitos += valor;
                break;
            default:
                throw new ArgumentException($"Tipo de lançamento desconhecido: {tipo}");
        }

        QuantidadeLancamentos++;
        AtualizadoEmUtc = agoraUtc;
    }
}

/// <summary>Controle de idempotencia: cada evento so pode ser aplicado uma vez.</summary>
public class EventoProcessado
{
    public Guid Id { get; set; }
    public DateTime ProcessadoEmUtc { get; set; }
}
