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
            new EvalQuery { Id = 5, UseCase = "UC3", Query = "Give me all open trades for counterparty 42.", Tags = "smoke,uc3" },
            new EvalQuery { Id = 6,  UseCase = "UC1", Query = "What is a trade lifecycle?",                       Tags = "uc1,golden" },
            new EvalQuery { Id = 7,  UseCase = "UC1", Query = "Explain settlement instructions.",                  Tags = "uc1,golden" },
            new EvalQuery { Id = 8,  UseCase = "UC1", Query = "What is a master netting agreement?",               Tags = "uc1,golden" },
            new EvalQuery { Id = 9,  UseCase = "UC1", Query = "What does margin call mean?",                       Tags = "uc1,golden" },
            new EvalQuery { Id = 10, UseCase = "UC1", Query = "Describe the role of a clearing house.",            Tags = "uc1,golden" },
            new EvalQuery { Id = 11, UseCase = "UC1", Query = "What is a credit default swap?",                    Tags = "uc1,golden" },
            new EvalQuery { Id = 12, UseCase = "UC1", Query = "What are the key components of a confirmation?",    Tags = "uc1,golden" },
            new EvalQuery { Id = 13, UseCase = "UC1", Query = "Explain the difference between OTC and ETD.",       Tags = "uc1,golden" },
            new EvalQuery { Id = 14, UseCase = "UC1", Query = "What is variation margin?",                         Tags = "uc1,golden" },
            new EvalQuery { Id = 15, UseCase = "UC1", Query = "What is initial margin?",                           Tags = "uc1,golden" },
            new EvalQuery { Id = 16, UseCase = "UC1", Query = "How does trade matching work?",                     Tags = "uc1,golden" },
            new EvalQuery { Id = 17, UseCase = "UC1", Query = "What is a nostro account?",                         Tags = "uc1,golden" },
            new EvalQuery { Id = 18, UseCase = "UC1", Query = "Describe the T+2 settlement cycle.",                Tags = "uc1,golden" },
            new EvalQuery { Id = 19, UseCase = "UC1", Query = "What is straight-through processing?",              Tags = "uc1,golden" },
            new EvalQuery { Id = 20, UseCase = "UC1", Query = "What is a repo agreement?",                         Tags = "uc1,golden" },
            new EvalQuery { Id = 21, UseCase = "UC1", Query = "Explain the purpose of a SWIFT message.",           Tags = "uc1,golden" },
            new EvalQuery { Id = 22, UseCase = "UC1", Query = "What is collateral management?",                    Tags = "uc1,golden" },
            new EvalQuery { Id = 23, UseCase = "UC1", Query = "What is a legal entity identifier (LEI)?",          Tags = "uc1,golden" },
            new EvalQuery { Id = 24, UseCase = "UC1", Query = "How does bilateral netting reduce exposure?",       Tags = "uc1,golden" },
            new EvalQuery { Id = 25, UseCase = "UC1", Query = "What is a credit support annex (CSA)?",             Tags = "uc1,golden" },
            new EvalQuery { Id = 26, UseCase = "UC1", Query = "What is MiFID II reporting?",                       Tags = "uc1,golden" },
            new EvalQuery { Id = 27, UseCase = "UC1", Query = "Explain what EMIR compliance requires.",            Tags = "uc1,golden" },
            new EvalQuery { Id = 28, UseCase = "UC1", Query = "What is a securities lending agreement?",           Tags = "uc1,golden" },
            new EvalQuery { Id = 29, UseCase = "UC1", Query = "How is a swap confirmed under ISDA protocols?",     Tags = "uc1,golden" },
            new EvalQuery { Id = 30, UseCase = "UC1", Query = "What is the role of a custodian bank?",             Tags = "uc1,golden" }
        );
    }
}
