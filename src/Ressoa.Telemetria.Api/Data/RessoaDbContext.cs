using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ressoa.Telemetria.Api.Domain;

namespace Ressoa.Telemetria.Api.Data;

/// <summary>
/// Mapeia as tabelas já existentes no Oracle (criadas por scripts/script-bd.sql do repo Java).
/// A API .NET não cria esquema em produção.
/// </summary>
public class RessoaDbContext(DbContextOptions<RessoaDbContext> options) : DbContext(options)
{
    public DbSet<Dispositivo> Dispositivos => Set<Dispositivo>();
    public DbSet<Leitura> Leituras => Set<Leitura>();

    private static readonly ValueConverter<bool, string> SimNao = new(v => v ? "S" : "N", v => v == "S");

    private static readonly ValueConverter<DateTime, DateTime> Utc =
        new(v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dispositivo>(e =>
        {
            e.ToTable("DISPOSITIVO");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasColumnName("ID").ValueGeneratedOnAdd();
            e.Property(d => d.NumeroSerie).HasColumnName("NUMERO_SERIE").HasMaxLength(40).IsRequired();
            e.HasIndex(d => d.NumeroSerie).IsUnique();
            e.Property(d => d.UsuarioId).HasColumnName("USUARIO_ID");
            e.Property(d => d.BairroId).HasColumnName("BAIRRO_ID");
            e.Property(d => d.TipoRecipiente).HasColumnName("TIPO_RECIPIENTE").HasMaxLength(12);
            e.Property(d => d.VolumeLitros).HasColumnName("VOLUME_LITROS");
            e.Property(d => d.PulsosPorDia).HasColumnName("PULSOS_POR_DIA");
            e.Property(d => d.Status).HasColumnName("STATUS").HasMaxLength(15).HasConversion<string>();
            e.Property(d => d.InstaladoEm).HasColumnName("INSTALADO_EM").HasConversion(Utc);
        });

        modelBuilder.Entity<Leitura>(e =>
        {
            e.ToTable("LEITURA");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasColumnName("ID").ValueGeneratedOnAdd();
            e.Property(l => l.DispositivoId).HasColumnName("DISPOSITIVO_ID");
            e.HasOne(l => l.Dispositivo).WithMany().HasForeignKey(l => l.DispositivoId);
            e.Property(l => l.ChaveIdempotencia).HasColumnName("CHAVE_IDEMPOTENCIA").HasMaxLength(64).IsRequired();
            e.HasIndex(l => l.ChaveIdempotencia).IsUnique();
            e.HasIndex(l => new { l.DispositivoId, l.RegistradaEm });
            e.Property(l => l.RegistradaEm).HasColumnName("REGISTRADA_EM").HasConversion(Utc);
            e.Property(l => l.BateriaPct).HasColumnName("BATERIA_PCT");
            e.Property(l => l.TemperaturaAgua).HasColumnName("TEMPERATURA_AGUA").HasPrecision(4, 1);
            e.Property(l => l.PulsoOk).HasColumnName("PULSO_OK").HasMaxLength(1).HasConversion(SimNao);
        });
    }
}
