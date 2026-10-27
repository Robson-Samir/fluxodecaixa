namespace Lancamentos.Api.Domain;

public enum TipoLancamento
{
    Credito = 1,
    Debito = 2
}

public class DominioException : Exception
{
    public DominioException(string mensagem) : base(mensagem) { }
}

public class Lancamento
{
    // EF precisa de um construtor sem parametros
    private Lancamento() { }

    public Guid Id { get; private set; }
    public TipoLancamento Tipo { get; private set; }
    public decimal Valor { get; private set; }
    public string Descricao { get; private set; } = string.Empty;
    public DateOnly Data { get; private set; }
    public DateTime CriadoEmUtc { get; private set; }

    public static Lancamento Criar(TipoLancamento tipo, decimal valor, string? descricao, DateOnly data, DateTime agoraUtc)
    {
        if (!Enum.IsDefined(tipo))
            throw new DominioException("Tipo de lançamento inválido.");

        if (valor <= 0)
            throw new DominioException("O valor do lançamento deve ser maior que zero.");

        if (decimal.Round(valor, 2) != valor)
            throw new DominioException("O valor aceita no máximo duas casas decimais.");

        if (string.IsNullOrWhiteSpace(descricao))
            throw new DominioException("A descrição é obrigatória.");

        if (descricao.Trim().Length > 200)
            throw new DominioException("A descrição deve ter no máximo 200 caracteres.");

        return new Lancamento
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Valor = valor,
            Descricao = descricao.Trim(),
            Data = data,
            CriadoEmUtc = agoraUtc
        };
    }
}
