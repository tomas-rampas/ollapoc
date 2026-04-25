using Microsoft.EntityFrameworkCore;
using RagServer.Infrastructure.Catalog.Entities;

namespace RagServer.Infrastructure.Catalog;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<EvalQuery>       EvalQueries      => Set<EvalQuery>();
    public DbSet<EvalResult>      EvalResults      => Set<EvalResult>();
    public DbSet<IngestionCursor> IngestionCursors => Set<IngestionCursor>();
    public DbSet<IngestionRun>    IngestionRuns    => Set<IngestionRun>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<IngestionCursor>().HasKey(e => e.Source);

        mb.Entity<EvalQuery>().HasData(
            new EvalQuery { Id = 1, UseCase = "UC1", Query = "What is a counterparty?",                      Tags = "smoke,uc1" },
            new EvalQuery { Id = 2, UseCase = "UC1", Query = "Explain the purpose of the ISDA agreement.",  Tags = "smoke,uc1" },
            new EvalQuery { Id = 3, UseCase = "UC2", Query = "What attributes does a Trade entity have?",   Tags = "smoke,uc2" },
            new EvalQuery { Id = 4, UseCase = "UC2", Query = "List the critical data elements for risk.",   Tags = "smoke,uc2" },
            new EvalQuery { Id = 5, UseCase = "UC3", Query = "Give me all open trades for counterparty 42.", Tags = "smoke,uc3" }
        );
    }
}
