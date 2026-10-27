using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lancamentos.Api.Domain;
using Lancamentos.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lancamentos.Tests;

public class FakePublisher : IEventPublisher
{
    public bool Falhar { get; set; }
    public List<LancamentoRegistradoEvent> Enviados { get; } = [];

    public Task PublicarAsync(LancamentoRegistradoEvent evento, CancellationToken ct)
    {
        if (Falhar) throw new HttpRequestException("consolidado fora do ar");
        Enviados.Add(evento);
        return Task.CompletedTask;
    }
}

public class LancamentosApiFactory : WebApplicationFactory<Program>
{
    private readonly string _arquivoDb = Path.Combine(Path.GetTempPath(), $"lanc-{Guid.NewGuid():N}.db");
    public FakePublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_arquivoDb}");
        builder.UseSetting("Outbox:Habilitado", "false"); // o teste dispara o worker na mao

        builder.ConfigureServices(s =>
        {
            s.AddSingleton(Publisher);
            s.AddScoped<IEventPublisher>(sp => sp.GetRequiredService<FakePublisher>());
        });
    }

    public async Task<HttpClient> ClienteAutenticadoAsync()
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/token", new { usuario = "comerciante", senha = "senha123" });
        resp.EnsureSuccessStatusCode();
        var token = (await resp.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public OutboxWorker CriarWorker() => new(
        Services.GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        Services.GetRequiredService<IConfiguration>(),
        Services.GetRequiredService<ILogger<OutboxWorker>>());

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _arquivoDb, _arquivoDb + "-wal", _arquivoDb + "-shm" })
            try { File.Delete(f); } catch { /* arquivo temporario, nao atrapalha */ }
    }

    public record TokenDto(string AccessToken);
}

public class LancamentosApiTests : IClassFixture<LancamentosApiFactory>
{
    private readonly LancamentosApiFactory _factory;

    public LancamentosApiTests(LancamentosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task SemToken_Retorna401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/lancamentos");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_ComSenhaErrada_Retorna401()
    {
        var resp = await _factory.CreateClient()
            .PostAsJsonAsync("/auth/token", new { usuario = "comerciante", senha = "errada" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Registrar_Valido_Retorna201_EListaNoDia()
    {
        var client = await _factory.ClienteAutenticadoAsync();

        var resp = await client.PostAsJsonAsync("/api/lancamentos",
            new { tipo = "Credito", valor = 200.00, descricao = "Venda", data = "2026-01-15" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var lista = await client.GetFromJsonAsync<List<LancamentoDto>>("/api/lancamentos?data=2026-01-15");
        Assert.Single(lista!);
        Assert.Equal("Credito", lista![0].Tipo);
    }

    [Fact]
    public async Task Registrar_ValorInvalido_Retorna400()
    {
        var client = await _factory.ClienteAutenticadoAsync();

        var resp = await client.PostAsJsonAsync("/api/lancamentos",
            new { tipo = "Debito", valor = -5, descricao = "Conta de luz" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Registrar_TipoInvalido_Retorna400()
    {
        var client = await _factory.ClienteAutenticadoAsync();

        var resp = await client.PostAsJsonAsync("/api/lancamentos",
            new { tipo = "Pix", valor = 5, descricao = "Teste" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private record LancamentoDto(Guid Id, string Tipo, decimal Valor, string Descricao);
}

/// <summary>
/// O requisito principal: lancamentos continuam funcionando com o consolidado fora do ar.
/// </summary>
public class ResilienciaTests : IClassFixture<LancamentosApiFactory>
{
    private readonly LancamentosApiFactory _factory;

    public ResilienciaTests(LancamentosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ConsolidadoForaDoAr_LancamentoContinuaSendoAceito_EEventoEntregueDepois()
    {
        var client = await _factory.ClienteAutenticadoAsync();
        var worker = _factory.CriarWorker();
        _factory.Publisher.Enviados.Clear();

        // 1) consolidado indisponivel
        _factory.Publisher.Falhar = true;
        var resp = await client.PostAsJsonAsync("/api/lancamentos",
            new { tipo = "Debito", valor = 80, descricao = "Fornecedor", data = "2026-02-01" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        await worker.ProcessarLoteAsync(CancellationToken.None);
        Assert.Empty(_factory.Publisher.Enviados);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
            var pendente = await db.Outbox.SingleAsync(o => o.ProcessadoEmUtc == null);
            Assert.Equal(1, pendente.Tentativas);

            // 2) consolidado volta; adianta o relogio do backoff para nao esperar
            _factory.Publisher.Falhar = false;
            await db.Database.ExecuteSqlRawAsync("UPDATE Outbox SET ProximaTentativaEmUtc = '2000-01-01 00:00:00'");
        }

        await worker.ProcessarLoteAsync(CancellationToken.None);

        var enviado = Assert.Single(_factory.Publisher.Enviados);
        Assert.Equal("Debito", enviado.Tipo);
        Assert.Equal(80m, enviado.Valor);
    }
}
