using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Consolidado.Api.Application;
using Consolidado.Api.Domain;
using Consolidado.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Consolidado.Tests;

public class SaldoDiarioTests
{
    [Fact]
    public void Aplicar_SomaCreditosEDebitos_ECalculaSaldo()
    {
        var saldo = SaldoDiario.Novo(new DateOnly(2026, 3, 10));
        var agora = DateTime.UtcNow;

        saldo.Aplicar("Credito", 300m, agora);
        saldo.Aplicar("Credito", 100m, agora);
        saldo.Aplicar("Debito", 150.25m, agora);

        Assert.Equal(400m, saldo.TotalCreditos);
        Assert.Equal(150.25m, saldo.TotalDebitos);
        Assert.Equal(249.75m, saldo.Saldo);
        Assert.Equal(3, saldo.QuantidadeLancamentos);
    }

    [Fact]
    public void Aplicar_TipoDesconhecido_Falha()
    {
        var saldo = SaldoDiario.Novo(new DateOnly(2026, 3, 10));
        Assert.Throws<ArgumentException>(() => saldo.Aplicar("Pix", 10m, DateTime.UtcNow));
    }
}

public class ConsolidadoServiceTests : IDisposable
{
    private static readonly DateOnly Dia = new(2026, 3, 10);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private readonly ConsolidadoDbContext _db;
    private readonly ConsolidadoService _service;

    public ConsolidadoServiceTests()
    {
        _conexao.Open();
        _db = new ConsolidadoDbContext(new DbContextOptionsBuilder<ConsolidadoDbContext>().UseSqlite(_conexao).Options);
        _db.Database.EnsureCreated();
        _service = new ConsolidadoService(_db, new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
    }

    private static LancamentoRegistradoEvent Evento(string tipo, decimal valor, DateOnly? data = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), tipo, valor, data ?? Dia, DateTime.UtcNow);

    [Fact]
    public async Task DiaSemMovimento_RetornaTudoZerado()
    {
        var r = await _service.ConsultarAsync(Dia, CancellationToken.None);

        Assert.Equal(0, r.Saldo);
        Assert.Equal(0, r.QuantidadeLancamentos);
    }

    [Fact]
    public async Task EventosDoMesmoDia_SaoConsolidados()
    {
        await _service.AplicarEventoAsync(Evento("Credito", 500m), CancellationToken.None);
        await _service.AplicarEventoAsync(Evento("Debito", 120m), CancellationToken.None);
        await _service.AplicarEventoAsync(Evento("Credito", 20m, Dia.AddDays(1)), CancellationToken.None);

        var r = await _service.ConsultarAsync(Dia, CancellationToken.None);

        Assert.Equal(500m, r.TotalCreditos);
        Assert.Equal(120m, r.TotalDebitos);
        Assert.Equal(380m, r.Saldo);
    }

    [Fact]
    public async Task EventoReenviado_NaoDuplicaOValor()
    {
        var evento = Evento("Credito", 100m);

        Assert.True(await _service.AplicarEventoAsync(evento, CancellationToken.None));
        Assert.False(await _service.AplicarEventoAsync(evento, CancellationToken.None));

        var r = await _service.ConsultarAsync(Dia, CancellationToken.None);
        Assert.Equal(100m, r.Saldo);
        Assert.Equal(1, r.QuantidadeLancamentos);
    }

    [Fact]
    public async Task Consulta_ReflitaNovoEventoMesmoComCache()
    {
        await _service.AplicarEventoAsync(Evento("Credito", 100m), CancellationToken.None);
        var antes = await _service.ConsultarAsync(Dia, CancellationToken.None); // aquece o cache
        Assert.Equal(100m, antes.Saldo);

        await _service.AplicarEventoAsync(Evento("Credito", 50m), CancellationToken.None);

        var depois = await _service.ConsultarAsync(Dia, CancellationToken.None);
        Assert.Equal(150m, depois.Saldo);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }
}

public class ConsolidadoApiFactory : WebApplicationFactory<Program>
{
    private readonly string _arquivoDb = Path.Combine(Path.GetTempPath(), $"cons-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseSetting("ConnectionStrings:Default", $"Data Source={_arquivoDb}");

    public HttpClient ClienteAutenticado()
    {
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("chave-somente-para-desenvolvimento-local-1234567890"));
        var jwt = new JwtSecurityToken("fluxocaixa", "fluxocaixa-api", expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _arquivoDb, _arquivoDb + "-wal", _arquivoDb + "-shm" })
            try { File.Delete(f); } catch { /* temporario */ }
    }
}

public class ConsolidadoApiTests : IClassFixture<ConsolidadoApiFactory>
{
    private readonly ConsolidadoApiFactory _factory;

    public ConsolidadoApiTests(ConsolidadoApiFactory factory) => _factory = factory;

    private static LancamentoRegistradoEvent Evento(string tipo, decimal valor) =>
        new(Guid.NewGuid(), Guid.NewGuid(), tipo, valor, new DateOnly(2026, 5, 20), DateTime.UtcNow);

    private HttpClient ClienteInterno(string chave = "chave-interna-dev")
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", chave);
        return c;
    }

    [Fact]
    public async Task Consulta_SemToken_Retorna401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/consolidado/2026-05-20");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Evento_ComChaveErrada_Retorna401()
    {
        var resp = await ClienteInterno("outra").PostAsJsonAsync("/internal/eventos", Evento("Credito", 10));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Evento_ValorInvalido_Retorna400()
    {
        var resp = await ClienteInterno().PostAsJsonAsync("/internal/eventos", Evento("Credito", 0));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task FluxoCompleto_EventosViramSaldoConsultavel_EReenvioEIdempotente()
    {
        var interno = ClienteInterno();
        var credito = Evento("Credito", 1000);

        Assert.Equal(HttpStatusCode.Accepted, (await interno.PostAsJsonAsync("/internal/eventos", credito)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await interno.PostAsJsonAsync("/internal/eventos", credito)).StatusCode); // reenvio
        await interno.PostAsJsonAsync("/internal/eventos", Evento("Debito", 250));

        var saldo = await _factory.ClienteAutenticado()
            .GetFromJsonAsync<SaldoDiarioResponse>("/api/consolidado/2026-05-20");

        Assert.Equal(1000m, saldo!.TotalCreditos);
        Assert.Equal(250m, saldo.TotalDebitos);
        Assert.Equal(750m, saldo.Saldo);
        Assert.Equal(2, saldo.QuantidadeLancamentos);
    }
}
