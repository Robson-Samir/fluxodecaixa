using Lancamentos.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Api.Infrastructure;

public class LancamentosDbContext : DbContext
{
    public LancamentosDbContext(DbContextOptions<LancamentosDbContext> options) : base(options) { }

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lancamento>(e =>
        {
            e.ToTable("Lancamentos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Descricao).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Data);
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("Outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).IsRequired();
            e.HasIndex(x => new { x.ProcessadoEmUtc, x.ProximaTentativaEmUtc });
        });
    }
}
