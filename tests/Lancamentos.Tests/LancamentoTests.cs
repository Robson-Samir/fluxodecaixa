using Lancamentos.Api.Domain;
using Xunit;

namespace Lancamentos.Tests;

public class LancamentoTests
{
    private static readonly DateTime Agora = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Hoje = new(2026, 3, 10);

    [Fact]
    public void Criar_ComDadosValidos_PreencheOsCampos()
    {
        var l = Lancamento.Criar(TipoLancamento.Credito, 150.50m, "  Venda balcão ", Hoje, Agora);

        Assert.NotEqual(Guid.Empty, l.Id);
        Assert.Equal(TipoLancamento.Credito, l.Tipo);
        Assert.Equal(150.50m, l.Valor);
        Assert.Equal("Venda balcão", l.Descricao);
        Assert.Equal(Hoje, l.Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Criar_ComValorNaoPositivo_Falha(decimal valor)
    {
        Assert.Throws<DominioException>(() =>
            Lancamento.Criar(TipoLancamento.Debito, valor, "Fornecedor", Hoje, Agora));
    }

    [Fact]
    public void Criar_ComMaisDeDuasCasasDecimais_Falha()
    {
        Assert.Throws<DominioException>(() =>
            Lancamento.Criar(TipoLancamento.Debito, 10.999m, "Fornecedor", Hoje, Agora));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_SemDescricao_Falha(string? descricao)
    {
        Assert.Throws<DominioException>(() =>
            Lancamento.Criar(TipoLancamento.Credito, 10, descricao, Hoje, Agora));
    }

    [Fact]
    public void Outbox_RegistrarFalha_AplicaBackoffExponencial()
    {
        var evento = new LancamentoRegistradoEvent(Guid.NewGuid(), Guid.NewGuid(), "Credito", 10, Hoje, Agora);
        var msg = OutboxMessage.De(evento);

        msg.RegistrarFalha("fora do ar", Agora);
        Assert.Equal(Agora.AddSeconds(2), msg.ProximaTentativaEmUtc);

        msg.RegistrarFalha("fora do ar", Agora);
        Assert.Equal(Agora.AddSeconds(4), msg.ProximaTentativaEmUtc);

        for (var i = 0; i < 10; i++) msg.RegistrarFalha("fora do ar", Agora);
        Assert.Equal(Agora.AddSeconds(60), msg.ProximaTentativaEmUtc);
    }
}
