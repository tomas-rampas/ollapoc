using Microsoft.EntityFrameworkCore;
using RagServer.Infrastructure.Catalog.Entities;

namespace RagServer.Infrastructure.Catalog;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<EvalQuery>            EvalQueries      => Set<EvalQuery>();
    public DbSet<EvalResult>           EvalResults      => Set<EvalResult>();
    public DbSet<IngestionCursor>      IngestionCursors => Set<IngestionCursor>();
    public DbSet<IngestionRun>         IngestionRuns    => Set<IngestionRun>();
    public DbSet<CatalogEntity>        CatalogEntities  => Set<CatalogEntity>();
    public DbSet<CatalogAttribute>     CatalogAttributes => Set<CatalogAttribute>();
    public DbSet<CriticalDataElement>  CDEs             => Set<CriticalDataElement>();
    public DbSet<EntityRelationship>   EntityRelationships => Set<EntityRelationship>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<IngestionCursor>().HasKey(e => e.Source);

        // ── FK configuration ────────────────────────────────────────────────

        mb.Entity<CatalogAttribute>()
            .HasOne(a => a.Entity)
            .WithMany(e => e.Attributes)
            .HasForeignKey(a => a.CatalogEntityId);

        mb.Entity<CriticalDataElement>()
            .HasOne(c => c.Entity)
            .WithMany(e => e.CDEs)
            .HasForeignKey(c => c.CatalogEntityId);

        mb.Entity<EntityRelationship>()
            .HasOne(r => r.SourceEntity)
            .WithMany(e => e.SourceRelationships)
            .HasForeignKey(r => r.SourceEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<EntityRelationship>()
            .HasOne(r => r.TargetEntity)
            .WithMany(e => e.TargetRelationships)
            .HasForeignKey(r => r.TargetEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Seed data ────────────────────────────────────────────────────────

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

        mb.Entity<CatalogEntity>().HasData(
            new CatalogEntity { Id = 1, Name = "Trade",        EntityType = "financial_instrument", Description = "A financial contract between two counterparties" },
            new CatalogEntity { Id = 2, Name = "Settlement",   EntityType = "process",              Description = "The process of finalizing a trade transaction" },
            new CatalogEntity { Id = 3, Name = "Counterparty", EntityType = "party",                Description = "Legal entity on the other side of a trade" },
            new CatalogEntity { Id = 4, Name = "Portfolio",    EntityType = "financial_instrument", Description = "A collection of financial positions" },
            new CatalogEntity { Id = 5, Name = "Instrument",   EntityType = "financial_instrument", Description = "A tradeable financial asset or security" }
        );

        mb.Entity<CatalogAttribute>().HasData(
            // Trade attributes (CatalogEntityId = 1)
            new CatalogAttribute { Id = 1,  CatalogEntityId = 1, Name = "TradeId",        DataType = "string",   Description = "Unique identifier for the trade",            IsNullable = false },
            new CatalogAttribute { Id = 2,  CatalogEntityId = 1, Name = "TradeDate",      DataType = "datetime", Description = "Date the trade was executed",                IsNullable = false },
            new CatalogAttribute { Id = 3,  CatalogEntityId = 1, Name = "ValueDate",      DataType = "datetime", Description = "Date on which the trade value is effective",  IsNullable = false },
            new CatalogAttribute { Id = 4,  CatalogEntityId = 1, Name = "Notional",       DataType = "decimal",  Description = "Notional amount of the trade",               IsNullable = false },
            new CatalogAttribute { Id = 5,  CatalogEntityId = 1, Name = "Currency",       DataType = "string",   Description = "Currency of the notional amount",            IsNullable = false },
            new CatalogAttribute { Id = 6,  CatalogEntityId = 1, Name = "Status",         DataType = "string",   Description = "Current status of the trade lifecycle",      IsNullable = false },
            new CatalogAttribute { Id = 7,  CatalogEntityId = 1, Name = "InstrumentType", DataType = "string",   Description = "Type of financial instrument",               IsNullable = false },
            // Settlement attributes (CatalogEntityId = 2)
            new CatalogAttribute { Id = 8,  CatalogEntityId = 2, Name = "SettlementId",   DataType = "string",   Description = "Unique identifier for the settlement",       IsNullable = false },
            new CatalogAttribute { Id = 9,  CatalogEntityId = 2, Name = "SettlementDate", DataType = "datetime", Description = "Expected settlement date",                   IsNullable = false },
            new CatalogAttribute { Id = 10, CatalogEntityId = 2, Name = "Amount",         DataType = "decimal",  Description = "Settlement amount",                          IsNullable = false }
        );

        mb.Entity<CriticalDataElement>().HasData(
            new CriticalDataElement { Id = 1, CatalogEntityId = 1, Name = "TradeId",        GovernanceOwner = "Operations", RegulatoryReference = "EMIR Art.9",      Description = "Unique trade identifier" },
            new CriticalDataElement { Id = 2, CatalogEntityId = 1, Name = "CounterpartyLEI",GovernanceOwner = "Legal",      RegulatoryReference = "EMIR Art.9",      Description = "Legal Entity Identifier of counterparty" },
            new CriticalDataElement { Id = 3, CatalogEntityId = 1, Name = "Notional",       GovernanceOwner = "Risk",       RegulatoryReference = "EMIR Art.9",      Description = "Notional amount for margin calculation" },
            new CriticalDataElement { Id = 4, CatalogEntityId = 1, Name = "TradeDate",      GovernanceOwner = "Operations", RegulatoryReference = "MiFID II Art.26", Description = "Date the trade was executed" },
            new CriticalDataElement { Id = 5, CatalogEntityId = 2, Name = "SettlementDate", GovernanceOwner = "Operations", RegulatoryReference = null,              Description = "Expected settlement date" }
        );

        mb.Entity<EntityRelationship>().HasData(
            new EntityRelationship { Id = 1, SourceEntityId = 1, TargetEntityId = 3, RelationshipType = "hasCounterparty", Description = "A trade involves a counterparty" },
            new EntityRelationship { Id = 2, SourceEntityId = 1, TargetEntityId = 2, RelationshipType = "settledBy",       Description = "A trade is settled by a settlement instruction" },
            new EntityRelationship { Id = 3, SourceEntityId = 4, TargetEntityId = 5, RelationshipType = "contains",        Description = "A portfolio contains instruments" }
        );
    }
}
