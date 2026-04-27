using Microsoft.EntityFrameworkCore;
using RagServer.Infrastructure.Catalog.Entities;

namespace RagServer.Infrastructure.Catalog;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<EvalQuery>            EvalQueries         => Set<EvalQuery>();
    public DbSet<EvalResult>           EvalResults         => Set<EvalResult>();
    public DbSet<IngestionCursor>      IngestionCursors    => Set<IngestionCursor>();
    public DbSet<IngestionRun>         IngestionRuns       => Set<IngestionRun>();
    public DbSet<CatalogEntity>        CatalogEntities     => Set<CatalogEntity>();
    public DbSet<CatalogAttribute>     CatalogAttributes   => Set<CatalogAttribute>();
    public DbSet<CriticalDataElement>  CDEs                => Set<CriticalDataElement>();
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

        // Self-reference for complex multi-value attributes
        mb.Entity<CatalogAttribute>()
            .HasOne(a => a.Parent)
            .WithMany(a => a.Children)
            .HasForeignKey(a => a.ParentAttributeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique per (parent, code) — scoped to child rows only
        mb.Entity<CatalogAttribute>()
            .HasIndex(a => new { a.ParentAttributeId, a.AttributeCode })
            .IsUnique()
            .HasFilter("[ParentAttributeId] IS NOT NULL");

        // ── Seed data ────────────────────────────────────────────────────────

        mb.Entity<EvalQuery>().HasData(
            new EvalQuery { Id = 1, UseCase = "UC1", Query = "What is a counterparty?",                      Tags = "smoke,uc1" },
            new EvalQuery { Id = 2, UseCase = "UC1", Query = "Explain the purpose of the ISDA agreement.",  Tags = "smoke,uc1" },
            new EvalQuery { Id = 3, UseCase = "UC2", Query = "What attributes does a Location entity have?",   Tags = "smoke,uc2" },
            new EvalQuery { Id = 4, UseCase = "UC2", Query = "List the critical data elements for risk.",   Tags = "smoke,uc2" },
            new EvalQuery { Id = 5, UseCase = "UC3", Query = "Give me all active locations in London.", Tags = "smoke,uc3" },
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
            new CatalogEntity { Id = 2,  Name = "Settlement",           EntityType = "process",              EntityCode = "settlement",             Description = "The process of finalizing a trade transaction" },
            new CatalogEntity { Id = 3,  Name = "Counterparty",         EntityType = "party",                EntityCode = "counterparty",           Description = "Legal entity on the other side of a trade" },
            new CatalogEntity { Id = 6,  Name = "ClientAccount",        EntityType = "account",              EntityCode = "clientaccount",          Description = "A client's trading or settlement account" },
            new CatalogEntity { Id = 7,  Name = "Book",                 EntityType = "financial_instrument", EntityCode = "book",                   Description = "A trading or banking book holding positions" },
            new CatalogEntity { Id = 8,  Name = "SettlementInstruction", EntityType = "instruction",         EntityCode = "settlementinstruction",  Description = "Standing settlement instruction for a counterparty" },
            new CatalogEntity { Id = 9,  Name = "Country",              EntityType = "reference",            EntityCode = "country",                Description = "ISO country reference data" },
            new CatalogEntity { Id = 10, Name = "Currency",             EntityType = "reference",            EntityCode = "currency",               Description = "ISO currency reference data" },
            new CatalogEntity { Id = 11, Name = "UkSicCode",  EntityType = "reference", EntityCode = "uksiccode",  Description = "UK Standard Industrial Classification 2007 code" },
            new CatalogEntity { Id = 12, Name = "NaceCode",   EntityType = "reference", EntityCode = "nacecode",   Description = "EU NACE Rev.2 economic activity classification code" },
            new CatalogEntity { Id = 13, Name = "Region",     EntityType = "reference", EntityCode = "region",     Description = "UK ONS ITL1 statistical region" },
            new CatalogEntity { Id = 14, Name = "Location",   EntityType = "reference", EntityCode = "location",   Description = "Physical business location with address and classification" }
        );

        mb.Entity<CatalogAttribute>().HasData(
            // ── Settlement attributes (CatalogEntityId = 2, IDs 8-10) ────────────
            new CatalogAttribute { Id = 8,  CatalogEntityId = 2, Name = "SettlementId",   DataType = "string",   Description = "Unique identifier for the settlement",       IsNullable = false },
            new CatalogAttribute { Id = 9,  CatalogEntityId = 2, Name = "SettlementDate", DataType = "datetime", Description = "Expected settlement date",                   IsNullable = false },
            new CatalogAttribute { Id = 10, CatalogEntityId = 2, Name = "Amount",         DataType = "decimal",  Description = "Settlement amount",                          IsNullable = false },

            // ── Counterparty flat (CatalogEntityId = 3, IDs 11-25) ───────────────
            new CatalogAttribute { Id = 11, CatalogEntityId = 3, Name = "Lei",                 DataType = "string",   IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Legal",       Sensitivity = "CONFIDENTIAL", AttributeCode = "lei",                  Description = "Legal Entity Identifier (ISO 17442, 20 chars)" },
            new CatalogAttribute { Id = 12, CatalogEntityId = 3, Name = "LegalName",           DataType = "string",   IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Legal",       Sensitivity = "INTERNAL",     AttributeCode = "legal_name",           Description = "Official registered legal name" },
            new CatalogAttribute { Id = 13, CatalogEntityId = 3, Name = "ShortName",           DataType = "string",   IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "short_name",           Description = "Short trading name or abbreviation" },
            new CatalogAttribute { Id = 14, CatalogEntityId = 3, Name = "EntityType",          DataType = "string",   IsNullable = false, IsMandatory = true,  Owner = "Legal",       Sensitivity = "INTERNAL",     AttributeCode = "entity_type",          Description = "LEGAL_ENTITY | BRANCH | FUND | INDIVIDUAL" },
            new CatalogAttribute { Id = 15, CatalogEntityId = 3, Name = "RegistrationNumber",  DataType = "string",   IsNullable = true,  Owner = "Legal",       Sensitivity = "INTERNAL",     AttributeCode = "registration_number",  Description = "Company registration number in jurisdiction" },
            new CatalogAttribute { Id = 16, CatalogEntityId = 3, Name = "IncorporationCountry", DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Legal",       Sensitivity = "INTERNAL",     AttributeCode = "incorporation_country", Description = "ISO 3166-1 alpha-2 country of incorporation" },
            new CatalogAttribute { Id = 17, CatalogEntityId = 3, Name = "Status",              DataType = "string",   IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "status",               Description = "ACTIVE | INACTIVE | SUSPENDED | TERMINATED" },
            new CatalogAttribute { Id = 18, CatalogEntityId = 3, Name = "CreditRating",        DataType = "string",   IsNullable = true,  Owner = "Risk",        Sensitivity = "CONFIDENTIAL", AttributeCode = "credit_rating",        Description = "External credit rating (Moody's / S&P / Fitch)" },
            new CatalogAttribute { Id = 19, CatalogEntityId = 3, Name = "FatcaStatus",         DataType = "string",   IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Tax",         Sensitivity = "CONFIDENTIAL", AttributeCode = "fatca_status",         Description = "FATCA classification: FFI | NFFE | EXEMPT | UNKNOWN" },
            new CatalogAttribute { Id = 20, CatalogEntityId = 3, Name = "KycStatus",           DataType = "string",   IsNullable = false, IsMandatory = true,  Owner = "Compliance",  Sensitivity = "RESTRICTED",   AttributeCode = "kyc_status",           Description = "KYC status: APPROVED | PENDING | EXPIRED | REJECTED" },
            new CatalogAttribute { Id = 21, CatalogEntityId = 3, Name = "KycExpiryDate",       DataType = "datetime", IsNullable = true,  Owner = "Compliance",  Sensitivity = "INTERNAL",     AttributeCode = "kyc_expiry_date",      Description = "Date the current KYC review expires" },
            new CatalogAttribute { Id = 22, CatalogEntityId = 3, Name = "RelationshipManager", DataType = "string",   IsNullable = true,  Owner = "Sales",        Sensitivity = "INTERNAL",     AttributeCode = "relationship_manager", Description = "Assigned RM name or employee ID" },
            new CatalogAttribute { Id = 23, CatalogEntityId = 3, Name = "Tier",                DataType = "string",   IsNullable = true,  Owner = "Sales",        Sensitivity = "INTERNAL",     AttributeCode = "tier",                 Description = "Client tier: PLATINUM | GOLD | SILVER | STANDARD" },
            new CatalogAttribute { Id = 24, CatalogEntityId = 3, Name = "OnboardingDate",      DataType = "datetime", IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "onboarding_date",      Description = "Date the counterparty was onboarded" },
            new CatalogAttribute { Id = 25, CatalogEntityId = 3, Name = "LastReviewDate",      DataType = "datetime", IsNullable = true,  Owner = "Compliance",  Sensitivity = "INTERNAL",     AttributeCode = "last_review_date",     Description = "Date of last periodic KYC/AML review" },

            // ── Counterparty complex parents (CatalogEntityId = 3, IDs 26-30) ────
            new CatalogAttribute { Id = 26, CatalogEntityId = 3, Name = "SystemMap",               DataType = "object[]", IsNullable = true, AttributeCode = "system_map",               Description = "Source-system identifiers — call GetChildAttributes to enumerate" },
            new CatalogAttribute { Id = 27, CatalogEntityId = 3, Name = "Addresses",               DataType = "object[]", IsNullable = true, AttributeCode = "addresses",                Description = "Address list — call GetChildAttributes for PHYSICAL/POSTAL/REGISTERED/PRINCIPAL_OFFICE types" },
            new CatalogAttribute { Id = 28, CatalogEntityId = 3, Name = "Identifiers",             DataType = "object[]", IsNullable = true, AttributeCode = "identifiers",              Description = "External regulatory and business identifiers — call GetChildAttributes for LEI/BIC/DUNS/CRN/GIIN" },
            new CatalogAttribute { Id = 29, CatalogEntityId = 3, Name = "ContactPersons",          DataType = "object[]", IsNullable = true, AttributeCode = "contact_persons",          Description = "Named contacts by role — call GetChildAttributes for COMPLIANCE/RELATIONSHIP_MANAGER/OPERATIONS" },
            new CatalogAttribute { Id = 30, CatalogEntityId = 3, Name = "RegulatoryClassifications", DataType = "object[]", IsNullable = true, AttributeCode = "regulatory_classifications", Description = "Per-regime classifications — call GetChildAttributes for EMIR/MIFID/FATCA/CRS" },

            // ── Counterparty complex children (IDs 31-50) ────────────────────────
            // system_map children (ParentAttributeId = 26)
            new CatalogAttribute { Id = 31, CatalogEntityId = 3, ParentAttributeId = 26, Name = "MUREX",               DataType = "string", IsNullable = true, AttributeCode = "MUREX",               Description = "Murex trading system counterparty code" },
            new CatalogAttribute { Id = 32, CatalogEntityId = 3, ParentAttributeId = 26, Name = "BLOOMBERG",           DataType = "string", IsNullable = true, AttributeCode = "BLOOMBERG",           Description = "Bloomberg terminal identifier" },
            new CatalogAttribute { Id = 33, CatalogEntityId = 3, ParentAttributeId = 26, Name = "SUMMIT",              DataType = "string", IsNullable = true, AttributeCode = "SUMMIT",              Description = "Murex Summit risk system reference" },
            new CatalogAttribute { Id = 34, CatalogEntityId = 3, ParentAttributeId = 26, Name = "GBS",                 DataType = "string", IsNullable = true, AttributeCode = "GBS",                 Description = "Global Banking System party reference" },
            // addresses children (ParentAttributeId = 27)
            new CatalogAttribute { Id = 35, CatalogEntityId = 3, ParentAttributeId = 27, Name = "PHYSICAL",           DataType = "object", IsNullable = true, AttributeCode = "PHYSICAL",           Description = "Physical office address — fields: line1, line2 (optional), city, postCode, country (ISO 3166-1 alpha-2)" },
            new CatalogAttribute { Id = 36, CatalogEntityId = 3, ParentAttributeId = 27, Name = "POSTAL",             DataType = "object", IsNullable = true, AttributeCode = "POSTAL",             Description = "Postal/mailing address — fields: line1, line2 (optional), city, postCode, country" },
            new CatalogAttribute { Id = 37, CatalogEntityId = 3, ParentAttributeId = 27, Name = "REGISTERED",         DataType = "object", IsNullable = true, AttributeCode = "REGISTERED",         Description = "Registered legal address per jurisdiction — fields: line1, city, postCode, country" },
            new CatalogAttribute { Id = 38, CatalogEntityId = 3, ParentAttributeId = 27, Name = "PRINCIPAL_OFFICE",   DataType = "object", IsNullable = true, AttributeCode = "PRINCIPAL_OFFICE",   Description = "Principal place of business — fields: line1, city, postCode, country" },
            // identifiers children (ParentAttributeId = 28)
            new CatalogAttribute { Id = 39, CatalogEntityId = 3, ParentAttributeId = 28, Name = "LEI",                DataType = "string", IsNullable = true, AttributeCode = "LEI",                Description = "Legal Entity Identifier (ISO 17442, 20 alphanumeric chars)" },
            new CatalogAttribute { Id = 40, CatalogEntityId = 3, ParentAttributeId = 28, Name = "BIC",                DataType = "string", IsNullable = true, AttributeCode = "BIC",                Description = "SWIFT Bank Identifier Code (8 or 11 chars)" },
            new CatalogAttribute { Id = 41, CatalogEntityId = 3, ParentAttributeId = 28, Name = "DUNS",               DataType = "string", IsNullable = true, AttributeCode = "DUNS",               Description = "Dun & Bradstreet DUNS number (9 digits)" },
            new CatalogAttribute { Id = 42, CatalogEntityId = 3, ParentAttributeId = 28, Name = "CRN",                DataType = "string", IsNullable = true, AttributeCode = "CRN",                Description = "Company Registration Number (jurisdiction-specific)" },
            new CatalogAttribute { Id = 43, CatalogEntityId = 3, ParentAttributeId = 28, Name = "GIIN",               DataType = "string", IsNullable = true, AttributeCode = "GIIN",               Description = "Global Intermediary Identification Number (FATCA registration)" },
            // contact_persons children (ParentAttributeId = 29)
            new CatalogAttribute { Id = 44, CatalogEntityId = 3, ParentAttributeId = 29, Name = "COMPLIANCE",         DataType = "object", IsNullable = true, AttributeCode = "COMPLIANCE",         Description = "Compliance contact — fields: name, email, phone, department" },
            new CatalogAttribute { Id = 45, CatalogEntityId = 3, ParentAttributeId = 29, Name = "RELATIONSHIP_MANAGER", DataType = "object", IsNullable = true, AttributeCode = "RELATIONSHIP_MANAGER", Description = "Assigned relationship manager — fields: name, email, phone" },
            new CatalogAttribute { Id = 46, CatalogEntityId = 3, ParentAttributeId = 29, Name = "OPERATIONS",         DataType = "object", IsNullable = true, AttributeCode = "OPERATIONS",         Description = "Operations/settlement contact — fields: name, email, phone" },
            // regulatory_classifications children (ParentAttributeId = 30)
            new CatalogAttribute { Id = 47, CatalogEntityId = 3, ParentAttributeId = 30, Name = "EMIR",               DataType = "object", IsNullable = true, AttributeCode = "EMIR",               Description = "EMIR classification — fields: classification, nfcThreshold, reportingObligation" },
            new CatalogAttribute { Id = 48, CatalogEntityId = 3, ParentAttributeId = 30, Name = "MIFID",              DataType = "object", IsNullable = true, AttributeCode = "MIFID",              Description = "MiFID II classification — fields: clientCategory (RETAIL|PROFESSIONAL|ELIGIBLE_COUNTERPARTY)" },
            new CatalogAttribute { Id = 49, CatalogEntityId = 3, ParentAttributeId = 30, Name = "FATCA",              DataType = "object", IsNullable = true, AttributeCode = "FATCA",              Description = "FATCA classification — fields: status (FFI|NFFE|EXEMPT), giin, withholdingRate" },
            new CatalogAttribute { Id = 50, CatalogEntityId = 3, ParentAttributeId = 30, Name = "CRS",                DataType = "object", IsNullable = true, AttributeCode = "CRS",                Description = "Common Reporting Standard tax residency — fields: taxResidency, tin, reportingJurisdiction" },

            // ── ClientAccount flat (CatalogEntityId = 6, IDs 51-62) ─────────────
            new CatalogAttribute { Id = 51, CatalogEntityId = 6, Name = "AccountNumber",     DataType = "string",   IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Operations", Sensitivity = "CONFIDENTIAL", AttributeCode = "account_number",     Description = "Unique account number per counterparty" },
            new CatalogAttribute { Id = 52, CatalogEntityId = 6, Name = "AccountName",       DataType = "string",   IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "account_name",       Description = "Descriptive account name" },
            new CatalogAttribute { Id = 53, CatalogEntityId = 6, Name = "AccountType",       DataType = "string",   IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "account_type",       Description = "PROPRIETARY | OMNIBUS | SEGREGATED | CUSTODY" },
            new CatalogAttribute { Id = 54, CatalogEntityId = 6, Name = "Currency",          DataType = "string",   IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Risk",       Sensitivity = "INTERNAL",     AttributeCode = "currency",           Description = "Base currency (ISO 4217)" },
            new CatalogAttribute { Id = 55, CatalogEntityId = 6, Name = "Status",            DataType = "string",   IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "status",             Description = "ACTIVE | INACTIVE | SUSPENDED | CLOSED" },
            new CatalogAttribute { Id = 56, CatalogEntityId = 6, Name = "CounterpartyId",    DataType = "int",      IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "counterparty_id",    Description = "FK to Counterparty entity" },
            new CatalogAttribute { Id = 57, CatalogEntityId = 6, Name = "Custodian",         DataType = "string",   IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "custodian",          Description = "Custodian bank name or BIC" },
            new CatalogAttribute { Id = 58, CatalogEntityId = 6, Name = "SettlementCountry", DataType = "string",   IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "settlement_country", Description = "ISO 3166-1 alpha-2 primary settlement country" },
            new CatalogAttribute { Id = 59, CatalogEntityId = 6, Name = "RegulatoryRegime",  DataType = "string",   IsNullable = true,  Owner = "Compliance",  Sensitivity = "INTERNAL",     AttributeCode = "regulatory_regime",  Description = "Primary regulatory regime: EMIR | DODD_FRANK | SFTR" },
            new CatalogAttribute { Id = 60, CatalogEntityId = 6, Name = "AccountManager",    DataType = "string",   IsNullable = true,  Owner = "Sales",       Sensitivity = "INTERNAL",     AttributeCode = "account_manager",    Description = "Assigned account manager name" },
            new CatalogAttribute { Id = 61, CatalogEntityId = 6, Name = "OpeningDate",       DataType = "datetime", IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "opening_date",       Description = "Date the account was opened" },
            new CatalogAttribute { Id = 62, CatalogEntityId = 6, Name = "CreditLimit",       DataType = "decimal",  IsNullable = true,  Owner = "Risk",        Sensitivity = "CONFIDENTIAL", AttributeCode = "credit_limit",       Description = "Approved credit limit in base currency" },

            // ── ClientAccount complex parents (CatalogEntityId = 6, IDs 63-65) ───
            new CatalogAttribute { Id = 63, CatalogEntityId = 6, Name = "SystemMap",             DataType = "object[]", IsNullable = true, AttributeCode = "system_map",             Description = "Source-system account identifiers — call GetChildAttributes for MUREX/JETBRIDGE/MEMPHIS/GBS" },
            new CatalogAttribute { Id = 64, CatalogEntityId = 6, Name = "Addresses",             DataType = "object[]", IsNullable = true, AttributeCode = "addresses",              Description = "Statement delivery addresses — call GetChildAttributes for POSTAL/ELECTRONIC types" },
            new CatalogAttribute { Id = 65, CatalogEntityId = 6, Name = "AuthorizedSignatories", DataType = "object[]", IsNullable = true, AttributeCode = "authorized_signatories", Description = "Authorized signatories — call GetChildAttributes for PRIMARY/SECONDARY/BACKUP" },

            // ── ClientAccount complex children (IDs 66-74) ───────────────────────
            // system_map children (ParentAttributeId = 63)
            new CatalogAttribute { Id = 66, CatalogEntityId = 6, ParentAttributeId = 63, Name = "MUREX",     DataType = "string", IsNullable = true, AttributeCode = "MUREX",     Description = "Murex account mapping reference" },
            new CatalogAttribute { Id = 67, CatalogEntityId = 6, ParentAttributeId = 63, Name = "JETBRIDGE", DataType = "string", IsNullable = true, AttributeCode = "JETBRIDGE", Description = "JetBridge operations system account code" },
            new CatalogAttribute { Id = 68, CatalogEntityId = 6, ParentAttributeId = 63, Name = "MEMPHIS",   DataType = "string", IsNullable = true, AttributeCode = "MEMPHIS",   Description = "MEMPHIS settlement system account reference" },
            new CatalogAttribute { Id = 69, CatalogEntityId = 6, ParentAttributeId = 63, Name = "GBS",       DataType = "string", IsNullable = true, AttributeCode = "GBS",       Description = "Global Banking System account number" },
            // addresses children (ParentAttributeId = 64)
            new CatalogAttribute { Id = 70, CatalogEntityId = 6, ParentAttributeId = 64, Name = "POSTAL",    DataType = "object", IsNullable = true, AttributeCode = "POSTAL",    Description = "Statement postal address — fields: line1, city, postCode, country" },
            new CatalogAttribute { Id = 71, CatalogEntityId = 6, ParentAttributeId = 64, Name = "ELECTRONIC", DataType = "object", IsNullable = true, AttributeCode = "ELECTRONIC", Description = "Electronic statement delivery — fields: email, format (PDF|CSV|XML), frequency" },
            // authorized_signatories children (ParentAttributeId = 65)
            new CatalogAttribute { Id = 72, CatalogEntityId = 6, ParentAttributeId = 65, Name = "PRIMARY",   DataType = "object", IsNullable = true, AttributeCode = "PRIMARY",   Description = "Primary authorized signatory — fields: name, role, authorizedLimit, currency, validFrom, validTo" },
            new CatalogAttribute { Id = 73, CatalogEntityId = 6, ParentAttributeId = 65, Name = "SECONDARY", DataType = "object", IsNullable = true, AttributeCode = "SECONDARY", Description = "Secondary authorized signatory — same fields as PRIMARY" },
            new CatalogAttribute { Id = 74, CatalogEntityId = 6, ParentAttributeId = 65, Name = "BACKUP",    DataType = "object", IsNullable = true, AttributeCode = "BACKUP",    Description = "Backup/emergency signatory — same fields as PRIMARY" },

            // ── Book flat (CatalogEntityId = 7, IDs 75-86) ──────────────────────
            new CatalogAttribute { Id = 75, CatalogEntityId = 7, Name = "BookCode",      DataType = "string",  IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Finance",    Sensitivity = "INTERNAL",     AttributeCode = "book_code",      Description = "Globally unique book identifier" },
            new CatalogAttribute { Id = 76, CatalogEntityId = 7, Name = "BookName",      DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Finance",    Sensitivity = "INTERNAL",     AttributeCode = "book_name",      Description = "Descriptive name of the book" },
            new CatalogAttribute { Id = 77, CatalogEntityId = 7, Name = "BookType",      DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Finance",    Sensitivity = "INTERNAL",     AttributeCode = "book_type",      Description = "TRADING | BANKING | HEDGING | REPO" },
            new CatalogAttribute { Id = 78, CatalogEntityId = 7, Name = "AssetClass",    DataType = "string",  IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Risk",       Sensitivity = "INTERNAL",     AttributeCode = "asset_class",    Description = "RATES | FX | CREDIT | EQUITY | COMMODITY | MIXED" },
            new CatalogAttribute { Id = 79, CatalogEntityId = 7, Name = "LegalEntity",   DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Legal",      Sensitivity = "INTERNAL",     AttributeCode = "legal_entity",   Description = "Legal entity the book belongs to (LEI or name)" },
            new CatalogAttribute { Id = 80, CatalogEntityId = 7, Name = "CostCenter",    DataType = "string",  IsNullable = true,  Owner = "Finance",    Sensitivity = "INTERNAL",     AttributeCode = "cost_center",    Description = "Finance cost center code" },
            new CatalogAttribute { Id = 81, CatalogEntityId = 7, Name = "ProfitCenter",  DataType = "string",  IsNullable = true,  Owner = "Finance",    Sensitivity = "INTERNAL",     AttributeCode = "profit_center",  Description = "Profit center for P&L attribution" },
            new CatalogAttribute { Id = 82, CatalogEntityId = 7, Name = "BookLimit",     DataType = "decimal", IsNullable = true,  Owner = "Risk",        Sensitivity = "CONFIDENTIAL", AttributeCode = "book_limit",     Description = "Approved notional limit for the book" },
            new CatalogAttribute { Id = 83, CatalogEntityId = 7, Name = "LimitCurrency", DataType = "string",  IsNullable = true,  Owner = "Risk",        Sensitivity = "INTERNAL",     AttributeCode = "limit_currency", Description = "Currency of the book limit (required when BookLimit is set)" },
            new CatalogAttribute { Id = 84, CatalogEntityId = 7, Name = "BookingSystem", DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "booking_system", Description = "Primary booking system: MUREX | CALYPSO | SUMMIT | KONDOR" },
            new CatalogAttribute { Id = 85, CatalogEntityId = 7, Name = "Status",        DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "status",         Description = "ACTIVE | INACTIVE | ARCHIVED" },
            new CatalogAttribute { Id = 86, CatalogEntityId = 7, Name = "RegulationType", DataType = "string", IsNullable = true,  Owner = "Risk",        Sensitivity = "INTERNAL",     AttributeCode = "regulation_type", Description = "FRTB_TRADING | FRTB_BANKING | SA | IMA" },

            // ── Book complex parents (CatalogEntityId = 7, IDs 87-89) ────────────
            new CatalogAttribute { Id = 87, CatalogEntityId = 7, Name = "SystemMap",  DataType = "object[]", IsNullable = true, AttributeCode = "system_map",  Description = "Trading system book identifiers — call GetChildAttributes for MUREX/JETBRIDGE/CPI" },
            new CatalogAttribute { Id = 88, CatalogEntityId = 7, Name = "RiskLimits", DataType = "object[]", IsNullable = true, AttributeCode = "risk_limits",  Description = "Risk limit types — call GetChildAttributes for DV01/PV01/VAR/NOTIONAL" },
            new CatalogAttribute { Id = 89, CatalogEntityId = 7, Name = "Traders",    DataType = "object[]", IsNullable = true, AttributeCode = "traders",      Description = "Trader assignments by role — call GetChildAttributes for PRIMARY/BACKUP/APPROVER" },

            // ── Book complex children (IDs 90-99) ────────────────────────────────
            // system_map children (ParentAttributeId = 87)
            new CatalogAttribute { Id = 90, CatalogEntityId = 7, ParentAttributeId = 87, Name = "MUREX",    DataType = "string", IsNullable = true, AttributeCode = "MUREX",    Description = "Murex book reference code" },
            new CatalogAttribute { Id = 91, CatalogEntityId = 7, ParentAttributeId = 87, Name = "JETBRIDGE", DataType = "string", IsNullable = true, AttributeCode = "JETBRIDGE", Description = "JetBridge operations book mapping" },
            new CatalogAttribute { Id = 92, CatalogEntityId = 7, ParentAttributeId = 87, Name = "CPI",      DataType = "string", IsNullable = true, AttributeCode = "CPI",      Description = "CPI risk system book identifier" },
            // risk_limits children (ParentAttributeId = 88)
            new CatalogAttribute { Id = 93, CatalogEntityId = 7, ParentAttributeId = 88, Name = "DV01",     DataType = "object", IsNullable = true, AttributeCode = "DV01",     Description = "Dollar Value 01 limit — fields: value, currency, tenor, effectiveDate" },
            new CatalogAttribute { Id = 94, CatalogEntityId = 7, ParentAttributeId = 88, Name = "PV01",     DataType = "object", IsNullable = true, AttributeCode = "PV01",     Description = "Present Value 01 limit — fields: value, currency, effectiveDate" },
            new CatalogAttribute { Id = 95, CatalogEntityId = 7, ParentAttributeId = 88, Name = "VAR",      DataType = "object", IsNullable = true, AttributeCode = "VAR",      Description = "Value at Risk limit — fields: value, currency, confidence, horizon" },
            new CatalogAttribute { Id = 96, CatalogEntityId = 7, ParentAttributeId = 88, Name = "NOTIONAL", DataType = "object", IsNullable = true, AttributeCode = "NOTIONAL", Description = "Notional limit — fields: value, currency, effectiveDate" },
            // traders children (ParentAttributeId = 89)
            new CatalogAttribute { Id = 97, CatalogEntityId = 7, ParentAttributeId = 89, Name = "PRIMARY",  DataType = "object", IsNullable = true, AttributeCode = "PRIMARY",  Description = "Primary trader — fields: name, blotter, maxPosition, currency" },
            new CatalogAttribute { Id = 98, CatalogEntityId = 7, ParentAttributeId = 89, Name = "BACKUP",   DataType = "object", IsNullable = true, AttributeCode = "BACKUP",   Description = "Backup trader — same fields as PRIMARY" },
            new CatalogAttribute { Id = 99, CatalogEntityId = 7, ParentAttributeId = 89, Name = "APPROVER", DataType = "object", IsNullable = true, AttributeCode = "APPROVER", Description = "Trade approver — fields: name, approvalLimit, currency" },

            // ── SettlementInstruction flat (CatalogEntityId = 8, IDs 100-111) ────
            new CatalogAttribute { Id = 100, CatalogEntityId = 8, Name = "InstructionId",          DataType = "string",  IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "instruction_id",           Description = "Unique SSI identifier" },
            new CatalogAttribute { Id = 101, CatalogEntityId = 8, Name = "InstructionType",        DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "instruction_type",         Description = "DVP | FOP | RVP | DFP — delivery vs/free of payment" },
            new CatalogAttribute { Id = 102, CatalogEntityId = 8, Name = "SwiftBic",               DataType = "string",  IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "swift_bic",                Description = "Beneficiary bank SWIFT BIC (8 or 11 chars)" },
            new CatalogAttribute { Id = 103, CatalogEntityId = 8, Name = "Iban",                   DataType = "string",  IsNullable = true,  Owner = "Operations", Sensitivity = "CONFIDENTIAL", AttributeCode = "iban",                     Description = "International Bank Account Number (up to 34 chars)" },
            new CatalogAttribute { Id = 104, CatalogEntityId = 8, Name = "AccountWithInstitution", DataType = "string",  IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "account_with_institution", Description = "Name of the account-holding institution" },
            new CatalogAttribute { Id = 105, CatalogEntityId = 8, Name = "BeneficiaryAccount",     DataType = "string",  IsNullable = true,  Owner = "Operations", Sensitivity = "CONFIDENTIAL", AttributeCode = "beneficiary_account",      Description = "Beneficiary account number" },
            new CatalogAttribute { Id = 106, CatalogEntityId = 8, Name = "Currency",               DataType = "string",  IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "currency",                 Description = "Settlement currency (ISO 4217)" },
            new CatalogAttribute { Id = 107, CatalogEntityId = 8, Name = "SettlementMethod",       DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "settlement_method",        Description = "CLS | RTGS | BILATERAL | NETTING" },
            new CatalogAttribute { Id = 108, CatalogEntityId = 8, Name = "Status",                 DataType = "string",  IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "status",                   Description = "ACTIVE | PENDING_VERIFICATION | EXPIRED | CANCELLED" },
            new CatalogAttribute { Id = 109, CatalogEntityId = 8, Name = "CounterpartyId",         DataType = "int",     IsNullable = false, IsMandatory = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "counterparty_id",          Description = "FK to Counterparty — must be ACTIVE status" },
            new CatalogAttribute { Id = 110, CatalogEntityId = 8, Name = "ClearingHouse",          DataType = "string",  IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "clearing_house",           Description = "DTCC | EUROCLEAR | CLEARSTREAM | LCH | NONE" },
            new CatalogAttribute { Id = 111, CatalogEntityId = 8, Name = "CountryOfSettlement",    DataType = "string",  IsNullable = true,  Owner = "Operations", Sensitivity = "INTERNAL",     AttributeCode = "country_of_settlement",    Description = "ISO 3166-1 alpha-2 country where settlement occurs" },

            // ── SettlementInstruction complex parents (CatalogEntityId = 8, IDs 112-114) ─
            new CatalogAttribute { Id = 112, CatalogEntityId = 8, Name = "SystemMap",          DataType = "object[]", IsNullable = true, AttributeCode = "system_map",          Description = "Settlement ops system identifiers — call GetChildAttributes for MEMPHIS/JETBRIDGE" },
            new CatalogAttribute { Id = 113, CatalogEntityId = 8, Name = "CorrespondentBanks", DataType = "object[]", IsNullable = true, AttributeCode = "correspondent_banks", Description = "Bank chain for settlement — call GetChildAttributes for INTERMEDIARY/COVER/CORRESPONDENT" },
            new CatalogAttribute { Id = 114, CatalogEntityId = 8, Name = "NostroAccount",      DataType = "object[]", IsNullable = true, AttributeCode = "nostro_account",      Description = "Nostro accounts by currency — call GetChildAttributes for USD/EUR/GBP/CHF" },

            // ── SettlementInstruction complex children (IDs 115-123) ─────────────
            // system_map children (ParentAttributeId = 112)
            new CatalogAttribute { Id = 115, CatalogEntityId = 8, ParentAttributeId = 112, Name = "MEMPHIS",       DataType = "string", IsNullable = true, AttributeCode = "MEMPHIS",       Description = "MEMPHIS settlement ops system reference" },
            new CatalogAttribute { Id = 116, CatalogEntityId = 8, ParentAttributeId = 112, Name = "JETBRIDGE",     DataType = "string", IsNullable = true, AttributeCode = "JETBRIDGE",     Description = "JetBridge settlement gateway identifier" },
            // correspondent_banks children (ParentAttributeId = 113)
            new CatalogAttribute { Id = 117, CatalogEntityId = 8, ParentAttributeId = 113, Name = "INTERMEDIARY",  DataType = "object", IsNullable = true, AttributeCode = "INTERMEDIARY",  Description = "Intermediary bank — fields: bankName, bic, accountNumber, currency" },
            new CatalogAttribute { Id = 118, CatalogEntityId = 8, ParentAttributeId = 113, Name = "COVER",         DataType = "object", IsNullable = true, AttributeCode = "COVER",         Description = "Cover payment bank — fields: bankName, bic, currency" },
            new CatalogAttribute { Id = 119, CatalogEntityId = 8, ParentAttributeId = 113, Name = "CORRESPONDENT", DataType = "object", IsNullable = true, AttributeCode = "CORRESPONDENT", Description = "Correspondent bank — fields: bankName, bic, accountNumber, nostroAccount" },
            // nostro_account children (ParentAttributeId = 114)
            new CatalogAttribute { Id = 120, CatalogEntityId = 8, ParentAttributeId = 114, Name = "USD", DataType = "object", IsNullable = true, AttributeCode = "USD", Description = "USD nostro account — fields: bankName, bic, accountNumber, iban" },
            new CatalogAttribute { Id = 121, CatalogEntityId = 8, ParentAttributeId = 114, Name = "EUR", DataType = "object", IsNullable = true, AttributeCode = "EUR", Description = "EUR nostro account — fields: bankName, bic, accountNumber, iban" },
            new CatalogAttribute { Id = 122, CatalogEntityId = 8, ParentAttributeId = 114, Name = "GBP", DataType = "object", IsNullable = true, AttributeCode = "GBP", Description = "GBP nostro account — fields: bankName, bic, accountNumber, sortCode" },
            new CatalogAttribute { Id = 123, CatalogEntityId = 8, ParentAttributeId = 114, Name = "CHF", DataType = "object", IsNullable = true, AttributeCode = "CHF", Description = "CHF nostro account — fields: bankName, bic, accountNumber, iban" },

            // ── Country flat (CatalogEntityId = 9, IDs 124-134) ─────────────────
            new CatalogAttribute { Id = 124, CatalogEntityId = 9, Name = "Iso2Code",        DataType = "string", IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "iso2_code",        Description = "ISO 3166-1 alpha-2 code (2 uppercase letters)" },
            new CatalogAttribute { Id = 125, CatalogEntityId = 9, Name = "Iso3Code",        DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "iso3_code",        Description = "ISO 3166-1 alpha-3 code (3 uppercase letters)" },
            new CatalogAttribute { Id = 126, CatalogEntityId = 9, Name = "IsoNumericCode",  DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "iso_numeric_code", Description = "ISO 3166-1 numeric code (3 digits)" },
            new CatalogAttribute { Id = 127, CatalogEntityId = 9, Name = "CountryName",     DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "country_name",     Description = "Official English country name" },
            new CatalogAttribute { Id = 128, CatalogEntityId = 9, Name = "Region",          DataType = "string", IsNullable = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "region",           Description = "UN macro-geographical region (e.g. Europe, Americas)" },
            new CatalogAttribute { Id = 129, CatalogEntityId = 9, Name = "SubRegion",       DataType = "string", IsNullable = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "sub_region",       Description = "UN sub-region (e.g. Northern Europe, South Asia)" },
            new CatalogAttribute { Id = 130, CatalogEntityId = 9, Name = "DefaultCurrency", DataType = "string", IsNullable = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "default_currency", Description = "Primary ISO 4217 currency code for this country" },
            new CatalogAttribute { Id = 131, CatalogEntityId = 9, Name = "FatfStatus",      DataType = "string", IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Compliance",      Sensitivity = "INTERNAL", AttributeCode = "fatf_status",      Description = "FATF risk status: LOW | MEDIUM | HIGH_RISK | BLACKLIST" },
            new CatalogAttribute { Id = 132, CatalogEntityId = 9, Name = "OecdMember",      DataType = "bool",   IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "oecd_member",      Description = "Whether the country is an OECD member state" },
            new CatalogAttribute { Id = 133, CatalogEntityId = 9, Name = "SanctionsStatus", DataType = "string", IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Compliance",      Sensitivity = "INTERNAL", AttributeCode = "sanctions_status", Description = "CLEAN | WATCH | PARTIAL_SANCTIONS | FULL_SANCTIONS" },
            new CatalogAttribute { Id = 134, CatalogEntityId = 9, Name = "Status",          DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "status",           Description = "ACTIVE | DEPRECATED" },

            // ── Country complex parents (CatalogEntityId = 9, IDs 135-136) ───────
            new CatalogAttribute { Id = 135, CatalogEntityId = 9, Name = "SystemMap", DataType = "object[]", IsNullable = true, AttributeCode = "system_map", Description = "Reference data system identifiers — call GetChildAttributes for CPI/GBS" },
            new CatalogAttribute { Id = 136, CatalogEntityId = 9, Name = "TimeZones", DataType = "object[]", IsNullable = true, AttributeCode = "time_zones", Description = "Country time zones — call GetChildAttributes for MAIN/EAST/WEST" },

            // ── Country complex children (IDs 137-141) ───────────────────────────
            // system_map children (ParentAttributeId = 135)
            new CatalogAttribute { Id = 137, CatalogEntityId = 9, ParentAttributeId = 135, Name = "CPI",  DataType = "string", IsNullable = true, AttributeCode = "CPI",  Description = "CPI reference data hub country code" },
            new CatalogAttribute { Id = 138, CatalogEntityId = 9, ParentAttributeId = 135, Name = "GBS",  DataType = "string", IsNullable = true, AttributeCode = "GBS",  Description = "Global Banking System country reference" },
            // time_zones children (ParentAttributeId = 136)
            new CatalogAttribute { Id = 139, CatalogEntityId = 9, ParentAttributeId = 136, Name = "MAIN", DataType = "object", IsNullable = true, AttributeCode = "MAIN", Description = "Main/primary time zone — fields: tzName, utcOffset, observesDst" },
            new CatalogAttribute { Id = 140, CatalogEntityId = 9, ParentAttributeId = 136, Name = "EAST", DataType = "object", IsNullable = true, AttributeCode = "EAST", Description = "Eastern region time zone (for large countries) — fields: tzName, utcOffset" },
            new CatalogAttribute { Id = 141, CatalogEntityId = 9, ParentAttributeId = 136, Name = "WEST", DataType = "object", IsNullable = true, AttributeCode = "WEST", Description = "Western region time zone (for large countries) — fields: tzName, utcOffset" },

            // ── Currency flat (CatalogEntityId = 10, IDs 142-150) ───────────────
            new CatalogAttribute { Id = 142, CatalogEntityId = 10, Name = "IsoCode",           DataType = "string", IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "iso_code",            Description = "ISO 4217 currency code (3 uppercase letters)" },
            new CatalogAttribute { Id = 143, CatalogEntityId = 10, Name = "IsoNumericCode",    DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "iso_numeric_code",    Description = "ISO 4217 numeric currency code (3 digits)" },
            new CatalogAttribute { Id = 144, CatalogEntityId = 10, Name = "CurrencyName",      DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "currency_name",       Description = "Official currency name" },
            new CatalogAttribute { Id = 145, CatalogEntityId = 10, Name = "Symbol",            DataType = "string", IsNullable = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "symbol",              Description = "Currency symbol (e.g. $, €, £)" },
            new CatalogAttribute { Id = 146, CatalogEntityId = 10, Name = "DecimalPlaces",     DataType = "int",    IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "decimal_places",      Description = "Number of decimal places (ISO 4217): 0, 2 or 3" },
            new CatalogAttribute { Id = 147, CatalogEntityId = 10, Name = "Status",            DataType = "string", IsNullable = false, IsMandatory = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "status",              Description = "ACTIVE | DEPRECATED | SUPERSEDED" },
            new CatalogAttribute { Id = 148, CatalogEntityId = 10, Name = "CentralBank",       DataType = "string", IsNullable = true,  Owner = "Data Governance", Sensitivity = "PUBLIC",    AttributeCode = "central_bank",        Description = "Issuing central bank name" },
            new CatalogAttribute { Id = 149, CatalogEntityId = 10, Name = "IsDeliverable",     DataType = "bool",   IsNullable = false, IsMandatory = true,  IsCde = true,  Owner = "Operations",      Sensitivity = "INTERNAL", AttributeCode = "is_deliverable",      Description = "True if physically settleable; false for NDF currencies" },
            new CatalogAttribute { Id = 150, CatalogEntityId = 10, Name = "SettlementCurrency", DataType = "string", IsNullable = true, Owner = "Operations",      Sensitivity = "INTERNAL", AttributeCode = "settlement_currency", Description = "Settlement proxy currency for NDF (required when IsDeliverable=false)" },

            // ── Currency complex parents (CatalogEntityId = 10, IDs 151-153) ─────
            new CatalogAttribute { Id = 151, CatalogEntityId = 10, Name = "SystemMap",           DataType = "object[]", IsNullable = true, AttributeCode = "system_map",           Description = "Reference data system identifiers — call GetChildAttributes for CPI/BLOOMBERG" },
            new CatalogAttribute { Id = 152, CatalogEntityId = 10, Name = "ClsSettlementPairs",  DataType = "object[]", IsNullable = true, AttributeCode = "cls_settlement_pairs", Description = "CLS-eligible settlement currency pairs — call GetChildAttributes for USD/EUR/GBP/JPY" },
            new CatalogAttribute { Id = 153, CatalogEntityId = 10, Name = "RoundingConventions", DataType = "object[]", IsNullable = true, AttributeCode = "rounding_conventions", Description = "Rounding rules by context — call GetChildAttributes for PAYMENTS/PRICING/REPORTING" },

            // ── Currency complex children (IDs 154-162) ──────────────────────────
            // system_map children (ParentAttributeId = 151)
            new CatalogAttribute { Id = 154, CatalogEntityId = 10, ParentAttributeId = 151, Name = "CPI",       DataType = "string", IsNullable = true, AttributeCode = "CPI",       Description = "CPI reference data hub currency code" },
            new CatalogAttribute { Id = 155, CatalogEntityId = 10, ParentAttributeId = 151, Name = "BLOOMBERG", DataType = "string", IsNullable = true, AttributeCode = "BLOOMBERG", Description = "Bloomberg currency identifier" },
            // cls_settlement_pairs children (ParentAttributeId = 152)
            new CatalogAttribute { Id = 156, CatalogEntityId = 10, ParentAttributeId = 152, Name = "USD", DataType = "object", IsNullable = true, AttributeCode = "USD", Description = "CLS USD settlement window — fields: settlementWindow, cutoffTime, valueDate" },
            new CatalogAttribute { Id = 157, CatalogEntityId = 10, ParentAttributeId = 152, Name = "EUR", DataType = "object", IsNullable = true, AttributeCode = "EUR", Description = "CLS EUR settlement window — same fields as USD" },
            new CatalogAttribute { Id = 158, CatalogEntityId = 10, ParentAttributeId = 152, Name = "GBP", DataType = "object", IsNullable = true, AttributeCode = "GBP", Description = "CLS GBP settlement window — same fields as USD" },
            new CatalogAttribute { Id = 159, CatalogEntityId = 10, ParentAttributeId = 152, Name = "JPY", DataType = "object", IsNullable = true, AttributeCode = "JPY", Description = "CLS JPY settlement window — same fields as USD" },
            // rounding_conventions children (ParentAttributeId = 153)
            new CatalogAttribute { Id = 160, CatalogEntityId = 10, ParentAttributeId = 153, Name = "PAYMENTS",  DataType = "object", IsNullable = true, AttributeCode = "PAYMENTS",  Description = "Payment rounding — fields: decimalPlaces, roundingMethod (HALF_UP|HALF_EVEN)" },
            new CatalogAttribute { Id = 161, CatalogEntityId = 10, ParentAttributeId = 153, Name = "PRICING",   DataType = "object", IsNullable = true, AttributeCode = "PRICING",   Description = "Pricing rounding — fields: decimalPlaces, roundingMethod" },
            new CatalogAttribute { Id = 162, CatalogEntityId = 10, ParentAttributeId = 153, Name = "REPORTING", DataType = "object", IsNullable = true, AttributeCode = "REPORTING", Description = "Reporting rounding (often ISO standard) — fields: decimalPlaces, roundingMethod" },

            // ── UkSicCode (CatalogEntityId = 11, IDs 163-169) ────────────────────
            new CatalogAttribute { Id = 163, CatalogEntityId = 11, Name = "SicCode",            DataType = "string",  IsNullable = false, IsMandatory = true,  AttributeCode = "sic_code",            Description = "5-digit SIC 2007 code (e.g. 64191)" },
            new CatalogAttribute { Id = 164, CatalogEntityId = 11, Name = "Description",        DataType = "string",  IsNullable = false, IsMandatory = true,  AttributeCode = "description",         Description = "Human-readable description of the SIC activity" },
            new CatalogAttribute { Id = 165, CatalogEntityId = 11, Name = "Section",            DataType = "string",  IsNullable = true,  AttributeCode = "section",             Description = "SIC section letter (A-U, e.g. K for Financial activities)" },
            new CatalogAttribute { Id = 166, CatalogEntityId = 11, Name = "SectionDescription", DataType = "string",  IsNullable = true,  AttributeCode = "section_description",  Description = "Human-readable section description" },
            new CatalogAttribute { Id = 167, CatalogEntityId = 11, Name = "Division",           DataType = "string",  IsNullable = true,  AttributeCode = "division",            Description = "2-digit division code (e.g. 64)" },
            new CatalogAttribute { Id = 168, CatalogEntityId = 11, Name = "Group",              DataType = "string",  IsNullable = true,  AttributeCode = "group",               Description = "3-digit group code (e.g. 641)" },
            new CatalogAttribute { Id = 169, CatalogEntityId = 11, Name = "Class",              DataType = "string",  IsNullable = true,  AttributeCode = "class",               Description = "4-digit class code (e.g. 6419)" },

            // ── NaceCode (CatalogEntityId = 12, IDs 170-174) ─────────────────────
            new CatalogAttribute { Id = 170, CatalogEntityId = 12, Name = "NaceCode",   DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "nace_code",  Description = "NACE Rev.2 code in format K64.19" },
            new CatalogAttribute { Id = 171, CatalogEntityId = 12, Name = "Description", DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "description", Description = "Human-readable NACE activity description" },
            new CatalogAttribute { Id = 172, CatalogEntityId = 12, Name = "Section",    DataType = "string", IsNullable = true,  AttributeCode = "section",     Description = "NACE section letter (e.g. K)" },
            new CatalogAttribute { Id = 173, CatalogEntityId = 12, Name = "Division",   DataType = "string", IsNullable = true,  AttributeCode = "division",    Description = "NACE division (2-digit, e.g. 64)" },
            new CatalogAttribute { Id = 174, CatalogEntityId = 12, Name = "Group",      DataType = "string", IsNullable = true,  AttributeCode = "group",       Description = "NACE group (e.g. 64.1)" },

            // ── Region (CatalogEntityId = 13, IDs 175-178) ───────────────────────
            new CatalogAttribute { Id = 175, CatalogEntityId = 13, Name = "RegionId",   DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "region_id",   Description = "ONS ITL1 region code (e.g. TLC for London)" },
            new CatalogAttribute { Id = 176, CatalogEntityId = 13, Name = "RegionName", DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "region_name", Description = "Region name (e.g. London, South East)" },
            new CatalogAttribute { Id = 177, CatalogEntityId = 13, Name = "Country",    DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "country",     Description = "ISO 3166-1 alpha-2 country code" },
            new CatalogAttribute { Id = 178, CatalogEntityId = 13, Name = "IsUkRegion", DataType = "bool",   IsNullable = false, AttributeCode = "is_uk_region", Description = "True for UK ONS ITL1 regions" },

            // ── Location (CatalogEntityId = 14, IDs 179-189) ─────────────────────
            new CatalogAttribute { Id = 179, CatalogEntityId = 14, Name = "LocationId",   DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "location_id",   Description = "Unique location identifier (e.g. LOC001)" },
            new CatalogAttribute { Id = 180, CatalogEntityId = 14, Name = "LocationName", DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "location_name", Description = "Descriptive name of the location" },
            new CatalogAttribute { Id = 181, CatalogEntityId = 14, Name = "AddressLine1", DataType = "string", IsNullable = true,  AttributeCode = "address_line1", Description = "First line of the postal address" },
            new CatalogAttribute { Id = 182, CatalogEntityId = 14, Name = "City",         DataType = "string", IsNullable = true,  AttributeCode = "city",          Description = "City name" },
            new CatalogAttribute { Id = 183, CatalogEntityId = 14, Name = "Postcode",     DataType = "string", IsNullable = true,  AttributeCode = "postcode",      Description = "UK postcode" },
            new CatalogAttribute { Id = 184, CatalogEntityId = 14, Name = "RegionId",     DataType = "string", IsNullable = true,  AttributeCode = "region_id",     Description = "FK to Region entity (ONS ITL1 code)" },
            new CatalogAttribute { Id = 185, CatalogEntityId = 14, Name = "Country",      DataType = "string", IsNullable = true,  AttributeCode = "country",       Description = "ISO 3166-1 alpha-2 country code" },
            new CatalogAttribute { Id = 186, CatalogEntityId = 14, Name = "BusinessType", DataType = "string", IsNullable = true,  AttributeCode = "business_type", Description = "Head Office | Branch | Regional Office | Trading Office | Back Office | Data Centre | Service Centre" },
            new CatalogAttribute { Id = 187, CatalogEntityId = 14, Name = "Status",       DataType = "string", IsNullable = false, IsMandatory = true,  AttributeCode = "status",        Description = "Active | Inactive | Under Review" },
            new CatalogAttribute { Id = 188, CatalogEntityId = 14, Name = "SicCode",      DataType = "string", IsNullable = true,  AttributeCode = "sic_code",      Description = "UK SIC 2007 code for this location's primary activity" },
            new CatalogAttribute { Id = 189, CatalogEntityId = 14, Name = "NaceCode",     DataType = "string", IsNullable = true,  AttributeCode = "nace_code",     Description = "NACE Rev.2 code for this location's primary activity" }
        );

        mb.Entity<CriticalDataElement>().HasData(
            new CriticalDataElement { Id = 5,  CatalogEntityId = 2,  Name = "SettlementDate",  GovernanceOwner = "Operations",     RegulatoryReference = null,                     Description = "Expected settlement date" },
            new CriticalDataElement { Id = 6,  CatalogEntityId = 3,  Name = "LegalName",       GovernanceOwner = "Legal",          RegulatoryReference = "GLEIF",                  Description = "Official registered legal name — must match GLEIF registry" },
            new CriticalDataElement { Id = 7,  CatalogEntityId = 3,  Name = "Lei",             GovernanceOwner = "Legal",          RegulatoryReference = "ISO 17442 / GLEIF",      Description = "Legal Entity Identifier — mandatory for all EMIR/MiFID reporting" },
            new CriticalDataElement { Id = 8,  CatalogEntityId = 3,  Name = "FatcaStatus",     GovernanceOwner = "Tax",            RegulatoryReference = "FATCA IGA",              Description = "FATCA classification determines US withholding obligations" },
            new CriticalDataElement { Id = 9,  CatalogEntityId = 6,  Name = "AccountNumber",   GovernanceOwner = "Operations",     RegulatoryReference = null,                     Description = "Unique account number — required for settlement and booking" },
            new CriticalDataElement { Id = 10, CatalogEntityId = 6,  Name = "Currency",        GovernanceOwner = "Risk",           RegulatoryReference = "ISO 4217",               Description = "Base currency — drives P&L calculation and reporting" },
            new CriticalDataElement { Id = 11, CatalogEntityId = 7,  Name = "BookCode",        GovernanceOwner = "Finance",        RegulatoryReference = null,                     Description = "Globally unique book identifier — used in all trade bookings" },
            new CriticalDataElement { Id = 12, CatalogEntityId = 7,  Name = "AssetClass",      GovernanceOwner = "Risk",           RegulatoryReference = "FRTB / CRR II",          Description = "Asset class drives capital requirement calculation under FRTB" },
            new CriticalDataElement { Id = 13, CatalogEntityId = 8,  Name = "InstructionId",   GovernanceOwner = "Operations",     RegulatoryReference = null,                     Description = "Unique SSI identifier — referenced on all settlement messages" },
            new CriticalDataElement { Id = 14, CatalogEntityId = 8,  Name = "Currency",        GovernanceOwner = "Operations",     RegulatoryReference = "ISO 4217",               Description = "Settlement currency — must match trade currency" },
            new CriticalDataElement { Id = 15, CatalogEntityId = 9,  Name = "Iso2Code",        GovernanceOwner = "Data Governance", RegulatoryReference = "ISO 3166-1",            Description = "Country code — used in address validation and regulatory reporting" },
            new CriticalDataElement { Id = 16, CatalogEntityId = 9,  Name = "FatfStatus",      GovernanceOwner = "Compliance",     RegulatoryReference = "FATF Recommendations",   Description = "FATF risk status drives AML/EDD requirements" },
            new CriticalDataElement { Id = 17, CatalogEntityId = 9,  Name = "SanctionsStatus", GovernanceOwner = "Compliance",     RegulatoryReference = "OFAC / EU / UN",         Description = "Sanctions status — controls whether transactions are permitted" },
            new CriticalDataElement { Id = 18, CatalogEntityId = 10, Name = "IsoCode",         GovernanceOwner = "Data Governance", RegulatoryReference = "ISO 4217",              Description = "Currency code — mandatory reference in all financial transactions" },
            new CriticalDataElement { Id = 19, CatalogEntityId = 10, Name = "DecimalPlaces",   GovernanceOwner = "Data Governance", RegulatoryReference = "ISO 4217",              Description = "Decimal places — required for correct rounding in calculations" },
            new CriticalDataElement { Id = 20, CatalogEntityId = 10, Name = "IsDeliverable",   GovernanceOwner = "Operations",     RegulatoryReference = null,                     Description = "Deliverability flag — determines settlement method for FX trades" }
        );

        mb.Entity<EntityRelationship>().HasData(
            new EntityRelationship { Id = 4,  SourceEntityId = 3, TargetEntityId = 6,  RelationshipType = "hasAccount",            Description = "A counterparty holds one or more client accounts" },
            new EntityRelationship { Id = 5,  SourceEntityId = 6, TargetEntityId = 7,  RelationshipType = "bookedToBook",          Description = "A client account's trades are booked to a trading/banking book" },
            new EntityRelationship { Id = 6,  SourceEntityId = 8, TargetEntityId = 3,  RelationshipType = "issuedForCounterparty", Description = "A settlement instruction is issued for a specific counterparty" },
            new EntityRelationship { Id = 7,  SourceEntityId = 6, TargetEntityId = 9,  RelationshipType = "domiciledIn",           Description = "A client account is domiciled in a country" },
            new EntityRelationship { Id = 8,  SourceEntityId = 6, TargetEntityId = 10, RelationshipType = "denominatedIn",         Description = "A client account is denominated in a currency" },
            new EntityRelationship { Id = 9,  SourceEntityId = 7, TargetEntityId = 10, RelationshipType = "limitedInCurrency",     Description = "A book's risk limit is expressed in a currency" },
            new EntityRelationship { Id = 10, SourceEntityId = 9, TargetEntityId = 10, RelationshipType = "defaultCurrency",       Description = "A country has a default trading currency" },
            new EntityRelationship { Id = 11, SourceEntityId = 8, TargetEntityId = 10, RelationshipType = "settledIn",             Description = "A settlement instruction is denominated in a currency" },
            new EntityRelationship { Id = 12, SourceEntityId = 14, TargetEntityId = 13, RelationshipType = "locatedInRegion", Description = "A location is situated in a UK ONS ITL1 region" },
            new EntityRelationship { Id = 13, SourceEntityId = 14, TargetEntityId = 11, RelationshipType = "classifiedBySic",  Description = "A location is classified by a UK SIC 2007 code" },
            new EntityRelationship { Id = 14, SourceEntityId = 14, TargetEntityId = 12, RelationshipType = "classifiedByNace", Description = "A location is classified by a NACE Rev.2 code" }
        );
    }
}
