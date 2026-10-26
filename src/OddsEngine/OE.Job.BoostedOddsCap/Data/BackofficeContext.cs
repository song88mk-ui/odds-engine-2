using Microsoft.EntityFrameworkCore;

namespace OE.Job.BoostedOddsCap.Data
{
    // Read-only EF context over the Backoffice SQL database, for one thing: who is emailed when a boost reaches its
    // cap. backoffice-final owns the table (Backoffice.Shared/Data/Backoffice/Entities/ClientInformation) and
    // edits it from its Clients page, where each client has an Email Address and a Notify email switch. Only the
    // four columns the job reads are mapped, and the entity is keyless because the job never saves or tracks.
    public class BackofficeContext : DbContext
    {
        public BackofficeContext(DbContextOptions<BackofficeContext> options) : base(options)
        {
        }

        public DbSet<ClientRecipientRow> ClientInformation { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClientRecipientRow>().HasNoKey().ToTable("ClientInformation");
        }
    }

    // Minimal projection of Backoffice ClientInformation. Brand is the client code, the same one PriceBoostConfig
    // and BoostedSettings carry (Backoffice's own client list maps it to ClientCode). Trading.dbo.Client, which the
    // job read for this before, is retired (flyway V18__RetireTradingClientTable) and was only a partial copy.
    public class ClientRecipientRow
    {
        public string Brand { get; set; }
        public string Email { get; set; }
        public bool IsEmailNotification { get; set; }
        public bool IsDeleted { get; set; }
    }
}
