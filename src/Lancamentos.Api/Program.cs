using System.Text;
using Lancamentos.Api.Application;
using Lancamentos.Api.Domain;
using Lancamentos.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<LancamentosDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<RelogioNegocio>();
builder.Services.AddScoped<ILancamentoService, LancamentoService>();

// transporte dos eventos para o consolidado
builder.Services.AddHttpClient<IEventPublisher, HttpEventPublisher>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Consolidado:BaseUrl"]!);
    c.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["Consolidado:ApiKey"]);
    c.Timeout = TimeSpan.FromSeconds(5);
});

if (builder.Configuration.GetValue("Outbox:Habilitado", true))
    builder.Services.AddHostedService<OutboxWorker>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// erros de regra de negocio viram 400 em vez de 500
app.UseExceptionHandler(erro => erro.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is DominioException)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { erro = ex.Message });
        return;
    }

    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { erro = "Erro interno ao processar a requisição." });
}));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program { }

