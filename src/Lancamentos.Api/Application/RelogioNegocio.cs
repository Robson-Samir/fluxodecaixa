namespace Lancamentos.Api.Application;

/// <summary>
/// O "dia" do fluxo de caixa e o dia do comerciante, nao o dia em UTC.
/// Sem isso, um lancamento feito as 22h em Sao Paulo cairia no dia seguinte.
/// </summary>
public class RelogioNegocio
{
    private readonly TimeProvider _relogio;
    private readonly TimeZoneInfo _fuso;

    public RelogioNegocio(TimeProvider relogio, IConfiguration config)
    {
        _relogio = relogio;
        _fuso = TimeZoneInfo.FindSystemTimeZoneById(config["Negocio:FusoHorario"] ?? "America/Sao_Paulo");
    }

    public DateOnly Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_relogio.GetUtcNow().UtcDateTime, _fuso));
}
