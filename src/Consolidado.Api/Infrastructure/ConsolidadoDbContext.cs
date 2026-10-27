using Consolidado.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Consolidado.Api.Infrastructure;

public class ConsolidadoDbContext : DbContext
{
    public ConsolidadoDbContext(DbContextOptions<ConsolidadoDbContext> options) : base(options) { }

    public DbSet<SaldoDiario> Saldos => Set<SaldoDiario>();
    public DbSet<EventoProcessado> EventosProcessados => Set<EventoProcessado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SaldoDiario>(e =>
        {
            e.ToTable("SaldosDiarios");
            e.HasKey(x => x.Data);
            e.Ignore(x => x.Saldo);
        });

        modelBuilder.Entity<EventoProcessado>(e =>
        {
            e.ToTable("EventosProcessados");
            e.HasKey(x => x.Id);
        });
    }
}
