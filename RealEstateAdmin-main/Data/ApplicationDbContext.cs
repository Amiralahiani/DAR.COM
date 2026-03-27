using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<BienImmobilier> Biens { get; set; }
        public DbSet<BienImage> BienImages { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<SaleTransaction> Sales { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<AgentPerformance> AgentPerformances { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Mapping lecture-seule vers la table Identity pour les jointures métier.
            modelBuilder.Entity<UserReference>(entity =>
            {
                entity.ToTable("AspNetUsers", t => t.ExcludeFromMigrations());
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(255);
                entity.Property(e => e.Nom);
                entity.Property(e => e.UserName);
                entity.Property(e => e.Email);
                entity.Property(e => e.LockoutEnd);
            });

            // Configuration for AgentPerformance
            modelBuilder.Entity<AgentPerformance>(entity =>
            {
                entity.HasKey(e => e.AgentId);
                entity.Property(e => e.AgentId).HasMaxLength(450);
                entity.Property(e => e.PunctualityScore).HasPrecision(5, 2);
                entity.Property(e => e.FeedbackScore).HasPrecision(5, 2);
                entity.Property(e => e.ConversionScore).HasPrecision(5, 2);
                entity.Property(e => e.LastComputed).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            });

            // Configuration pour BienImmobilier
            modelBuilder.Entity<BienImmobilier>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Titre).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Prix).IsRequired().HasPrecision(18, 2);
                entity.Property(e => e.Adresse).HasMaxLength(500);
                entity.Property(e => e.ImageUrl).HasMaxLength(1000);
                entity.Property(e => e.UserId).HasMaxLength(450);
                entity.Property(e => e.IsPublished).HasDefaultValue(true);
                entity.Property(e => e.PublicationStatus).HasMaxLength(50).HasDefaultValue("En attente");
                entity.Property(e => e.PublicationValidatedByAdminId).HasMaxLength(450);
                entity.Property(e => e.StatutCommercial).HasMaxLength(50).HasDefaultValue("Disponible");
                
                // Relation vers l'utilisateur Identity (projection UserReference)
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
            });

            modelBuilder.Entity<BienImage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Url).HasMaxLength(1000);
                entity.HasOne(e => e.BienImmobilier)
                    .WithMany(b => b.Images)
                    .HasForeignKey(e => e.BienImmobilierId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configuration pour Message
            modelBuilder.Entity<Message>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NomUtilisateur).HasMaxLength(100);
                entity.Property(e => e.Email).HasMaxLength(200);
                entity.Property(e => e.Sujet).HasMaxLength(200);
                entity.Property(e => e.Contenu).HasMaxLength(5000);
                entity.Property(e => e.Destinataire).HasMaxLength(50).HasDefaultValue("Administration");
                entity.Property(e => e.DateCreation).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
                entity.Property(e => e.Statut).HasMaxLength(50).HasDefaultValue("Nouveau");
                entity.Property(e => e.TraiteParId).HasMaxLength(450);
            });

            // Configuration pour AuditLog
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).HasMaxLength(450);
                entity.Property(e => e.Action).HasMaxLength(200);
                entity.Property(e => e.EntityType).HasMaxLength(200);
                entity.Property(e => e.Details).HasMaxLength(2000);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            });

            // Configuration pour SaleTransaction
            modelBuilder.Entity<SaleTransaction>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.BuyerId).HasMaxLength(450);
                entity.Property(e => e.SellerId).HasMaxLength(450);
                entity.Property(e => e.AgentId).HasMaxLength(450);
                entity.Property(e => e.Amount).HasPrecision(18, 2);
                entity.Property(e => e.PaymentMethod).HasMaxLength(50).HasDefaultValue("Virement");
                entity.Property(e => e.PaymentStatus).HasMaxLength(50).HasDefaultValue("En attente");
                entity.Property(e => e.TransactionStatus).HasMaxLength(50).HasDefaultValue("Finalisée");
                entity.Property(e => e.Notes).HasMaxLength(1000);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.HasOne(e => e.BienImmobilier)
                    .WithMany()
                    .HasForeignKey(e => e.BienImmobilierId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Buyer)
                    .WithMany()
                    .HasForeignKey(e => e.BuyerId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);

                entity.HasOne(e => e.Seller)
                    .WithMany()
                    .HasForeignKey(e => e.SellerId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
            });

            // Configuration for AppSetting
            modelBuilder.Entity<AppSetting>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).HasMaxLength(200);
                entity.Property(e => e.Value).HasMaxLength(2000);
            });
        }
    }
}
