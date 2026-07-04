using OE.Shared.Data.Translation.Entities;
using Microsoft.EntityFrameworkCore;

namespace OE.Shared.Data.Translation
{
    public class TranslationContext : DbContext
    {
        public TranslationContext(DbContextOptions<TranslationContext> options)
            : base(options)
        {
        }

        public DbSet<TranslationCache2> TranslationCache2 { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TranslationCache2>(entity =>
            {
                // The single translation store (2026-07 translation-flow redesign; the retired
                // SportsbookDynamic/SportsbookGlossary tables are gone). Flat: one row per
                // STRING_ID, fixed TARGET_TEXT_* column per language. Source columns + liveness
                // are written by OE.Translation.Service, HAS_BET_PLACEMENT by OE.Translation.Job,
                // TARGET_TEXT_* by the external translation pipeline only; OE.Adapter.Service
                // preloads the whole table read-only at startup.
                entity.ToTable("TranslationCache2");

                entity.HasKey(e => e.Id)
                      .HasName("PK_TranslationCache2");

                entity.HasIndex(e => e.StringId)
                    .IsUnique()
                    .HasName("UX_TC2_STRING");

                entity.Property(e => e.Id)
                    .HasColumnName("ID");

                entity.Property(e => e.StringId)
                    .HasColumnName("STRING_ID")
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(e => e.SourceLanguage)
                    .HasColumnName("SOURCE_LANGUAGE")
                    .HasMaxLength(10)
                    .IsRequired();

                entity.Property(e => e.SourceText)
                    .HasColumnName("SOURCE_TEXT")
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(e => e.TargetTextZh)
                    .HasColumnName("TARGET_TEXT_ZH")
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(e => e.TargetTextEs)
                    .HasColumnName("TARGET_TEXT_ES")
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(e => e.TargetTextFr)
                    .HasColumnName("TARGET_TEXT_FR")
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(e => e.HasBetPlacement)
                    .HasColumnName("HAS_BET_PLACEMENT");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("CREATED_AT");

                entity.Property(e => e.GlossaryVersion)
                    .HasColumnName("GLOSSARY_VERSION");

                entity.Property(e => e.UpdatedAt)
                    .HasColumnName("UPDATED_AT");
            });
        }
    }
}
