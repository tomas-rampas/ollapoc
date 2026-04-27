using Microsoft.EntityFrameworkCore;
using RagServer.Infrastructure.Business.Entities;

namespace RagServer.Infrastructure.Business;

/// <summary>
/// EF Core context for operational / business-domain data (Counterparty, Settlement, etc.).
/// Separate from <c>CatalogDbContext</c> which holds MDM / catalog metadata.
/// </summary>
public sealed class BusinessDataContext(DbContextOptions<BusinessDataContext> options) : DbContext(options)
{
    public DbSet<CounterpartyRecord>           Counterparties          => Set<CounterpartyRecord>();
    public DbSet<CountryRecord>                Countries               => Set<CountryRecord>();
    public DbSet<CurrencyRecord>               Currencies              => Set<CurrencyRecord>();
    public DbSet<BookRecord>                   Books                   => Set<BookRecord>();
    public DbSet<SettlementRecord>             Settlements             => Set<SettlementRecord>();
    public DbSet<ClientAccountRecord>          ClientAccounts          => Set<ClientAccountRecord>();
    public DbSet<SettlementInstructionRecord>  SettlementInstructions  => Set<SettlementInstructionRecord>();
    public DbSet<RegionRecord>                 Regions                 => Set<RegionRecord>();
    public DbSet<LocationRecord>               Locations               => Set<LocationRecord>();
    public DbSet<UkSicCodeRecord>              UkSicCodes              => Set<UkSicCodeRecord>();
    public DbSet<NaceCodeRecord>               NaceCodes               => Set<NaceCodeRecord>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // ── Seed data (mirrors OperationalDataSeeder mock data) ───────────────

        mb.Entity<CounterpartyRecord>().HasData(
            new CounterpartyRecord { CounterpartyId = "CP001", Lei = "213800QLQCMM37VCSD95", LegalName = "Barclays PLC",          ShortName = "Barclays",        EntityType = "Bank",          IncorporationCountry = "GB", Status = "Active"     },
            new CounterpartyRecord { CounterpartyId = "CP002", Lei = "MLU0ZEK838MRDLSYJ528", LegalName = "HSBC Holdings",         ShortName = "HSBC",            EntityType = "Bank",          IncorporationCountry = "GB", Status = "Active"     },
            new CounterpartyRecord { CounterpartyId = "CP003", Lei = "R0MUWSFPU8MPRO8K5P83", LegalName = "BNP Paribas",           ShortName = "BNP",             EntityType = "Bank",          IncorporationCountry = "FR", Status = "Active"     },
            new CounterpartyRecord { CounterpartyId = "CP004", Lei = "7LTWFZYICNSX8D621K86",  LegalName = "Deutsche Bank",         ShortName = "Deutsche",        EntityType = "Bank",          IncorporationCountry = "DE", Status = "Active"     },
            new CounterpartyRecord { CounterpartyId = "CP005", Lei = "YGKPMXR9KLZ7DNMKN645", LegalName = "Nomura International",  ShortName = "Nomura",          EntityType = "Broker-Dealer", IncorporationCountry = "JP", Status = "Active"     },
            new CounterpartyRecord { CounterpartyId = "CP006", Lei = "549300MLUDYVRQOOXS22",  LegalName = "BlackRock Advisors",    ShortName = "BlackRock",       EntityType = "Asset Manager", IncorporationCountry = "US", Status = "Active"     },
            new CounterpartyRecord { CounterpartyId = "CP007", Lei = "O2RNE8IBXP4R0TD8PL99", LegalName = "Societe Generale",      ShortName = "SocGen",          EntityType = "Bank",          IncorporationCountry = "FR", Status = "Restricted" },
            new CounterpartyRecord { CounterpartyId = "CP008", Lei = "RILFO74KP1CM8P6PCT96",  LegalName = "Standard Chartered",    ShortName = "StanChart",       EntityType = "Bank",          IncorporationCountry = "GB", Status = "Active"     }
        );

        mb.Entity<CountryRecord>().HasData(
            new CountryRecord { CountryCode = "GB", CountryName = "United Kingdom", IsoAlpha3 = "GBR", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "US", CountryName = "United States",  IsoAlpha3 = "USA", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "DE", CountryName = "Germany",        IsoAlpha3 = "DEU", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "FR", CountryName = "France",         IsoAlpha3 = "FRA", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "JP", CountryName = "Japan",          IsoAlpha3 = "JPN", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "SG", CountryName = "Singapore",      IsoAlpha3 = "SGP", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "CH", CountryName = "Switzerland",    IsoAlpha3 = "CHE", FatfStatus = "Low",       SanctionsStatus = "Clean"          },
            new CountryRecord { CountryCode = "RU", CountryName = "Russia",         IsoAlpha3 = "RUS", FatfStatus = "High Risk", SanctionsStatus = "Full Sanctions" },
            new CountryRecord { CountryCode = "IR", CountryName = "Iran",           IsoAlpha3 = "IRN", FatfStatus = "Blacklist", SanctionsStatus = "Full Sanctions" },
            new CountryRecord { CountryCode = "CN", CountryName = "China",          IsoAlpha3 = "CHN", FatfStatus = "Medium",    SanctionsStatus = "Watch"          }
        );

        mb.Entity<CurrencyRecord>().HasData(
            new CurrencyRecord { CurrencyCode = "GBP", CurrencyName = "British Pound Sterling",       DecimalPlaces = 2, IsDeliverable = true,  IsActive = true },
            new CurrencyRecord { CurrencyCode = "USD", CurrencyName = "US Dollar",                    DecimalPlaces = 2, IsDeliverable = true,  IsActive = true },
            new CurrencyRecord { CurrencyCode = "EUR", CurrencyName = "Euro",                         DecimalPlaces = 2, IsDeliverable = true,  IsActive = true },
            new CurrencyRecord { CurrencyCode = "JPY", CurrencyName = "Japanese Yen",                 DecimalPlaces = 0, IsDeliverable = true,  IsActive = true },
            new CurrencyRecord { CurrencyCode = "CHF", CurrencyName = "Swiss Franc",                  DecimalPlaces = 2, IsDeliverable = true,  IsActive = true },
            new CurrencyRecord { CurrencyCode = "CNH", CurrencyName = "Chinese Renminbi (Offshore)",  DecimalPlaces = 2, IsDeliverable = false, IsActive = true },
            new CurrencyRecord { CurrencyCode = "SGD", CurrencyName = "Singapore Dollar",             DecimalPlaces = 2, IsDeliverable = true,  IsActive = true },
            new CurrencyRecord { CurrencyCode = "INR", CurrencyName = "Indian Rupee",                 DecimalPlaces = 2, IsDeliverable = false, IsActive = true }
        );

        mb.Entity<BookRecord>().HasData(
            new BookRecord { BookId = "BK001", BookCode = "RATES-GB-01",     LegalEntity = "Barclays Bank PLC",        AssetClass = "Rates",     Status = "Active",   BookingSystem = "MUREX"   },
            new BookRecord { BookId = "BK002", BookCode = "FX-EU-01",        LegalEntity = "BNP Paribas SA",           AssetClass = "FX",        Status = "Active",   BookingSystem = "CALYPSO" },
            new BookRecord { BookId = "BK003", BookCode = "CREDIT-US-01",    LegalEntity = "Deutsche Bank AG",         AssetClass = "Credit",    Status = "Active",   BookingSystem = "SUMMIT"  },
            new BookRecord { BookId = "BK004", BookCode = "EQUITY-JP-01",    LegalEntity = "Nomura International PLC", AssetClass = "Equity",    Status = "Active",   BookingSystem = "KONDOR"  },
            new BookRecord { BookId = "BK005", BookCode = "REPO-GB-01",      LegalEntity = "Barclays Bank PLC",        AssetClass = "Rates",     Status = "Active",   BookingSystem = "MUREX"   },
            new BookRecord { BookId = "BK006", BookCode = "COMMODITY-US-01", LegalEntity = "BlackRock Advisors LLC",   AssetClass = "Commodity", Status = "Active",   BookingSystem = "MUREX"   },
            new BookRecord { BookId = "BK007", BookCode = "FX-GB-02",        LegalEntity = "Standard Chartered Bank",  AssetClass = "FX",        Status = "Archived", BookingSystem = "CALYPSO" },
            new BookRecord { BookId = "BK008", BookCode = "RATES-FR-01",     LegalEntity = "Societe Generale SA",      AssetClass = "Rates",     Status = "Active",   BookingSystem = "MUREX"   }
        );

        mb.Entity<SettlementRecord>().HasData(
            new SettlementRecord { SettlementId = "SET001", CounterpartyId = "CP001", Amount = 1250000.00m, Currency = "GBP", Status = "Settled",   SettlementDate = "2024-01-15" },
            new SettlementRecord { SettlementId = "SET002", CounterpartyId = "CP002", Amount =  850000.00m, Currency = "USD", Status = "Pending",   SettlementDate = "2024-01-16" },
            new SettlementRecord { SettlementId = "SET003", CounterpartyId = "CP003", Amount = 2100000.00m, Currency = "EUR", Status = "Settled",   SettlementDate = "2024-01-14" },
            new SettlementRecord { SettlementId = "SET004", CounterpartyId = "CP004", Amount =  500000.00m, Currency = "USD", Status = "Failed",    SettlementDate = "2024-01-13" },
            new SettlementRecord { SettlementId = "SET005", CounterpartyId = "CP001", Amount = 3200000.00m, Currency = "GBP", Status = "Settled",   SettlementDate = "2024-01-12" },
            new SettlementRecord { SettlementId = "SET006", CounterpartyId = "CP005", Amount =  750000.00m, Currency = "USD", Status = "Pending",   SettlementDate = "2024-01-17" },
            new SettlementRecord { SettlementId = "SET007", CounterpartyId = "CP006", Amount = 1800000.00m, Currency = "USD", Status = "Settled",   SettlementDate = "2024-01-11" },
            new SettlementRecord { SettlementId = "SET008", CounterpartyId = "CP007", Amount =  420000.00m, Currency = "EUR", Status = "Cancelled", SettlementDate = "2024-01-10" }
        );

        mb.Entity<ClientAccountRecord>().HasData(
            new ClientAccountRecord { AccountId = "ACC001", CounterpartyId = "CP001", AccountNumber = "GB29BARC20201530093459",      Currency = "GBP", Status = "Active",    AccountType = "Segregated"   },
            new ClientAccountRecord { AccountId = "ACC002", CounterpartyId = "CP002", AccountNumber = "GB82WEST12345698765432",      Currency = "USD", Status = "Active",    AccountType = "Omnibus"      },
            new ClientAccountRecord { AccountId = "ACC003", CounterpartyId = "CP003", AccountNumber = "FR7614508061200000000000000", Currency = "EUR", Status = "Active",    AccountType = "Proprietary"  },
            new ClientAccountRecord { AccountId = "ACC004", CounterpartyId = "CP004", AccountNumber = "DE89370400440532013000",      Currency = "EUR", Status = "Suspended", AccountType = "Segregated"   },
            new ClientAccountRecord { AccountId = "ACC005", CounterpartyId = "CP001", AccountNumber = "GB29BARC20201530093460",      Currency = "USD", Status = "Active",    AccountType = "Custody"      },
            new ClientAccountRecord { AccountId = "ACC006", CounterpartyId = "CP006", AccountNumber = "US12345678901234567",         Currency = "USD", Status = "Active",    AccountType = "Segregated"   },
            new ClientAccountRecord { AccountId = "ACC007", CounterpartyId = "CP008", AccountNumber = "GB73SCBL60161331926819",      Currency = "GBP", Status = "Active",    AccountType = "Omnibus"      },
            new ClientAccountRecord { AccountId = "ACC008", CounterpartyId = "CP002", AccountNumber = "GB94MIDL40051512345678",      Currency = "EUR", Status = "Closed",    AccountType = "Proprietary"  }
        );

        mb.Entity<SettlementInstructionRecord>().HasData(
            new SettlementInstructionRecord { InstructionId = "SSI001", CounterpartyId = "CP001", Currency = "GBP", InstructionType = "DVP", Status = "Active"               },
            new SettlementInstructionRecord { InstructionId = "SSI002", CounterpartyId = "CP002", Currency = "USD", InstructionType = "FOP", Status = "Active"               },
            new SettlementInstructionRecord { InstructionId = "SSI003", CounterpartyId = "CP003", Currency = "EUR", InstructionType = "DVP", Status = "Active"               },
            new SettlementInstructionRecord { InstructionId = "SSI004", CounterpartyId = "CP004", Currency = "USD", InstructionType = "RVP", Status = "Expired"              },
            new SettlementInstructionRecord { InstructionId = "SSI005", CounterpartyId = "CP005", Currency = "JPY", InstructionType = "DVP", Status = "Active"               },
            new SettlementInstructionRecord { InstructionId = "SSI006", CounterpartyId = "CP006", Currency = "USD", InstructionType = "FOP", Status = "Active"               },
            new SettlementInstructionRecord { InstructionId = "SSI007", CounterpartyId = "CP007", Currency = "EUR", InstructionType = "DFP", Status = "Pending Verification" },
            new SettlementInstructionRecord { InstructionId = "SSI008", CounterpartyId = "CP008", Currency = "GBP", InstructionType = "DVP", Status = "Active"               }
        );

        mb.Entity<RegionRecord>().HasData(
            new RegionRecord { RegionId = "TLC", RegionName = "London",                   Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLD", RegionName = "South East",               Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLE", RegionName = "East of England",          Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLF", RegionName = "South West",               Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLG", RegionName = "West Midlands",            Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLH", RegionName = "East Midlands",            Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLI", RegionName = "Yorkshire and The Humber", Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLJ", RegionName = "North West",               Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLK", RegionName = "North East",               Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLL", RegionName = "Scotland",                 Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLM", RegionName = "Wales",                    Country = "GB", IsUkRegion = true },
            new RegionRecord { RegionId = "TLN", RegionName = "Northern Ireland",         Country = "GB", IsUkRegion = true }
        );

        mb.Entity<LocationRecord>().HasData(
            new LocationRecord { LocationId = "LOC001", LocationName = "Canary Wharf Office",      AddressLine1 = "1 Canada Square",       City = "London",     Postcode = "E14 5AB",  RegionId = "TLC", Country = "GB", BusinessType = "Head Office",       Status = "Active",       SicCode = "64191", NaceCode = "K64.19" },
            new LocationRecord { LocationId = "LOC002", LocationName = "City of London Branch",     AddressLine1 = "25 Old Broad Street",   City = "London",     Postcode = "EC2N 1HQ", RegionId = "TLC", Country = "GB", BusinessType = "Branch",            Status = "Active",       SicCode = "64191", NaceCode = "K64.19" },
            new LocationRecord { LocationId = "LOC003", LocationName = "Manchester Office",         AddressLine1 = "1 Spinningfields",      City = "Manchester", Postcode = "M3 3AP",   RegionId = "TLJ", Country = "GB", BusinessType = "Regional Office",   Status = "Active",       SicCode = "64191", NaceCode = "K64.19" },
            new LocationRecord { LocationId = "LOC004", LocationName = "Edinburgh Finance Centre",  AddressLine1 = "4 Rutland Square",      City = "Edinburgh",  Postcode = "EH1 2AS",  RegionId = "TLL", Country = "GB", BusinessType = "Regional Office",   Status = "Active",       SicCode = "64999", NaceCode = "K64.99" },
            new LocationRecord { LocationId = "LOC005", LocationName = "Birmingham Branch",         AddressLine1 = "110 New Street",        City = "Birmingham", Postcode = "B2 4HQ",   RegionId = "TLG", Country = "GB", BusinessType = "Branch",            Status = "Active",       SicCode = "64191", NaceCode = "K64.19" },
            new LocationRecord { LocationId = "LOC006", LocationName = "Leeds Trading Floor",       AddressLine1 = "1 Wellington Place",    City = "Leeds",      Postcode = "LS1 4AP",  RegionId = "TLI", Country = "GB", BusinessType = "Trading Office",    Status = "Active",       SicCode = "64191", NaceCode = "K64.19" },
            new LocationRecord { LocationId = "LOC007", LocationName = "Bristol Back Office",       AddressLine1 = "Temple Quay House",     City = "Bristol",    Postcode = "BS1 6DZ",  RegionId = "TLF", Country = "GB", BusinessType = "Back Office",       Status = "Active",       SicCode = "66190", NaceCode = "K66.19" },
            new LocationRecord { LocationId = "LOC008", LocationName = "Cardiff Operations",        AddressLine1 = "Central Square",        City = "Cardiff",    Postcode = "CF10 1EP", RegionId = "TLM", Country = "GB", BusinessType = "Operations Centre", Status = "Active",       SicCode = "66190", NaceCode = "K66.19" },
            new LocationRecord { LocationId = "LOC009", LocationName = "Glasgow Technology Centre", AddressLine1 = "110 St Vincent Street", City = "Glasgow",    Postcode = "G2 5UB",   RegionId = "TLL", Country = "GB", BusinessType = "Data Centre",       Status = "Active",       SicCode = "66190", NaceCode = "K66.19" },
            new LocationRecord { LocationId = "LOC010", LocationName = "Belfast Service Centre",    AddressLine1 = "1 Donegall Square",     City = "Belfast",    Postcode = "BT1 5GB",  RegionId = "TLN", Country = "GB", BusinessType = "Service Centre",    Status = "Under Review", SicCode = "66190", NaceCode = "K66.19" }
        );

        mb.Entity<UkSicCodeRecord>().HasData(
            new UkSicCodeRecord { SicCode = "64110", Description = "Central banking",                              Section = "K", SectionDescription = "Financial and insurance activities", Division = "64", GroupCode = "641", ClassCode = "6411" },
            new UkSicCodeRecord { SicCode = "64191", Description = "Banks",                                        Section = "K", SectionDescription = "Financial and insurance activities", Division = "64", GroupCode = "641", ClassCode = "6419" },
            new UkSicCodeRecord { SicCode = "64201", Description = "Activities of agricultural holding companies", Section = "K", SectionDescription = "Financial and insurance activities", Division = "64", GroupCode = "642", ClassCode = "6420" },
            new UkSicCodeRecord { SicCode = "64301", Description = "Activities of investment trusts",              Section = "K", SectionDescription = "Financial and insurance activities", Division = "64", GroupCode = "643", ClassCode = "6430" },
            new UkSicCodeRecord { SicCode = "64999", Description = "Other financial service activities",           Section = "K", SectionDescription = "Financial and insurance activities", Division = "64", GroupCode = "649", ClassCode = "6499" },
            new UkSicCodeRecord { SicCode = "65110", Description = "Life insurance",                               Section = "K", SectionDescription = "Financial and insurance activities", Division = "65", GroupCode = "651", ClassCode = "6511" },
            new UkSicCodeRecord { SicCode = "65120", Description = "Non-life insurance",                           Section = "K", SectionDescription = "Financial and insurance activities", Division = "65", GroupCode = "651", ClassCode = "6512" },
            new UkSicCodeRecord { SicCode = "66190", Description = "Other activities auxiliary to financial services", Section = "K", SectionDescription = "Financial and insurance activities", Division = "66", GroupCode = "661", ClassCode = "6619" },
            new UkSicCodeRecord { SicCode = "66290", Description = "Other activities auxiliary to insurance",      Section = "K", SectionDescription = "Financial and insurance activities", Division = "66", GroupCode = "662", ClassCode = "6629" },
            new UkSicCodeRecord { SicCode = "66300", Description = "Fund management activities",                   Section = "K", SectionDescription = "Financial and insurance activities", Division = "66", GroupCode = "663", ClassCode = "6630" }
        );

        mb.Entity<NaceCodeRecord>().HasData(
            new NaceCodeRecord { NaceCode = "K64.11", Description = "Central banking",                                  Section = "K", Division = "64", GroupCode = "64.1" },
            new NaceCodeRecord { NaceCode = "K64.19", Description = "Other monetary intermediation",                    Section = "K", Division = "64", GroupCode = "64.1" },
            new NaceCodeRecord { NaceCode = "K64.20", Description = "Activities of holding companies",                  Section = "K", Division = "64", GroupCode = "64.2" },
            new NaceCodeRecord { NaceCode = "K64.30", Description = "Trusts, funds and similar financial entities",     Section = "K", Division = "64", GroupCode = "64.3" },
            new NaceCodeRecord { NaceCode = "K64.99", Description = "Other financial service activities",               Section = "K", Division = "64", GroupCode = "64.9" },
            new NaceCodeRecord { NaceCode = "K65.11", Description = "Life reinsurance",                                 Section = "K", Division = "65", GroupCode = "65.1" },
            new NaceCodeRecord { NaceCode = "K65.12", Description = "Non-life insurance",                               Section = "K", Division = "65", GroupCode = "65.1" },
            new NaceCodeRecord { NaceCode = "K66.19", Description = "Other activities auxiliary to financial services", Section = "K", Division = "66", GroupCode = "66.1" },
            new NaceCodeRecord { NaceCode = "K66.29", Description = "Other activities auxiliary to insurance",          Section = "K", Division = "66", GroupCode = "66.2" },
            new NaceCodeRecord { NaceCode = "K66.30", Description = "Fund management activities",                       Section = "K", Division = "66", GroupCode = "66.3" }
        );
    }
}
