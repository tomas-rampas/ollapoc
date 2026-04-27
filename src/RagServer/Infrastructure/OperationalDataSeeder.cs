using Elastic.Clients.Elasticsearch;

namespace RagServer.Infrastructure;

/// <summary>
/// Seeds Elasticsearch operational indices with mock data on startup.
/// Idempotent: skips any index that already exists.
/// Index naming convention mirrors IrToDslCompiler: entity name lowercased + "s".
/// </summary>
public sealed class OperationalDataSeeder(
    ElasticsearchClient es,
    ILogger<OperationalDataSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await SeedCounterpartysAsync(ct);
            await SeedSettlementsAsync(ct);
            await SeedClientAccountsAsync(ct);
            await SeedBooksAsync(ct);
            await SeedSettlementInstructionsAsync(ct);
            await SeedCountrysAsync(ct);
            await SeedCurrencysAsync(ct);
            await SeedUkSicCodesAsync(ct);
            await SeedNaceCodesAsync(ct);
            await SeedRegionsAsync(ct);
            await SeedLocationsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OperationalDataSeeder failed — will retry on next start");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task SeedIndexAsync(string index, IEnumerable<object> docs, CancellationToken ct)
    {
        var exists = await es.Indices.ExistsAsync(index, ct);
        if (exists.Exists)
        {
            logger.LogInformation("Index '{Index}' already exists — skipping seed", index);
            return;
        }

        await es.Indices.CreateAsync(index, ct);

        var bulkResp = await es.BulkAsync(b => b.Index(index).IndexMany(docs), ct);
        if (bulkResp.Errors)
            logger.LogWarning("Bulk seed for '{Index}' completed with errors", index);
        else
            logger.LogInformation("Seeded index '{Index}' with {Count} documents", index, bulkResp.Items.Count);
    }

    private Task SeedCounterpartysAsync(CancellationToken ct) =>
        SeedIndexAsync("counterpartys", new object[]
        {
            new { counterparty_id = "CP001", name = "Barclays PLC",           type = "Bank",          country = "GB", status = "Active",     lei = "213800QLQCMM37VCSD95" },
            new { counterparty_id = "CP002", name = "HSBC Holdings",          type = "Bank",          country = "GB", status = "Active",     lei = "MLU0ZEK838MRDLSYJ528" },
            new { counterparty_id = "CP003", name = "BNP Paribas",            type = "Bank",          country = "FR", status = "Active",     lei = "R0MUWSFPU8MPRO8K5P83" },
            new { counterparty_id = "CP004", name = "Deutsche Bank",          type = "Bank",          country = "DE", status = "Active",     lei = "7LTWFZYICNSX8D621K86" },
            new { counterparty_id = "CP005", name = "Nomura International",   type = "Broker-Dealer", country = "JP", status = "Active",     lei = "YGKPMXR9KLZ7DNMKN645" },
            new { counterparty_id = "CP006", name = "BlackRock Advisors",     type = "Asset Manager", country = "US", status = "Active",     lei = "549300MLUDYVRQOOXS22" },
            new { counterparty_id = "CP007", name = "Societe Generale",       type = "Bank",          country = "FR", status = "Restricted", lei = "O2RNE8IBXP4R0TD8PL99" },
            new { counterparty_id = "CP008", name = "Standard Chartered",     type = "Bank",          country = "GB", status = "Active",     lei = "RILFO74KP1CM8P6PCT96" },
        }, ct);

    private Task SeedSettlementsAsync(CancellationToken ct) =>
        SeedIndexAsync("settlements", new object[]
        {
            new { settlement_id = "SET001", counterparty_id = "CP001", amount = 1250000.00, currency = "GBP", status = "Settled",   settlement_date = "2024-01-15" },
            new { settlement_id = "SET002", counterparty_id = "CP002", amount =  850000.00, currency = "USD", status = "Pending",   settlement_date = "2024-01-16" },
            new { settlement_id = "SET003", counterparty_id = "CP003", amount = 2100000.00, currency = "EUR", status = "Settled",   settlement_date = "2024-01-14" },
            new { settlement_id = "SET004", counterparty_id = "CP004", amount =  500000.00, currency = "USD", status = "Failed",    settlement_date = "2024-01-13" },
            new { settlement_id = "SET005", counterparty_id = "CP001", amount = 3200000.00, currency = "GBP", status = "Settled",   settlement_date = "2024-01-12" },
            new { settlement_id = "SET006", counterparty_id = "CP005", amount =  750000.00, currency = "USD", status = "Pending",   settlement_date = "2024-01-17" },
            new { settlement_id = "SET007", counterparty_id = "CP006", amount = 1800000.00, currency = "USD", status = "Settled",   settlement_date = "2024-01-11" },
            new { settlement_id = "SET008", counterparty_id = "CP007", amount =  420000.00, currency = "EUR", status = "Cancelled", settlement_date = "2024-01-10" },
        }, ct);

    private Task SeedClientAccountsAsync(CancellationToken ct) =>
        SeedIndexAsync("clientaccounts", new object[]
        {
            new { account_id = "ACC001", counterparty_id = "CP001", account_number = "GB29BARC20201530093459",      currency = "GBP", status = "Active",    account_type = "Segregated" },
            new { account_id = "ACC002", counterparty_id = "CP002", account_number = "GB82WEST12345698765432",      currency = "USD", status = "Active",    account_type = "Omnibus" },
            new { account_id = "ACC003", counterparty_id = "CP003", account_number = "FR7614508061200000000000000", currency = "EUR", status = "Active",    account_type = "Proprietary" },
            new { account_id = "ACC004", counterparty_id = "CP004", account_number = "DE89370400440532013000",      currency = "EUR", status = "Suspended", account_type = "Segregated" },
            new { account_id = "ACC005", counterparty_id = "CP001", account_number = "GB29BARC20201530093460",      currency = "USD", status = "Active",    account_type = "Custody" },
            new { account_id = "ACC006", counterparty_id = "CP006", account_number = "US12345678901234567",         currency = "USD", status = "Active",    account_type = "Segregated" },
            new { account_id = "ACC007", counterparty_id = "CP008", account_number = "GB73SCBL60161331926819",      currency = "GBP", status = "Active",    account_type = "Omnibus" },
            new { account_id = "ACC008", counterparty_id = "CP002", account_number = "GB94MIDL40051512345678",      currency = "EUR", status = "Closed",    account_type = "Proprietary" },
        }, ct);

    private Task SeedBooksAsync(CancellationToken ct) =>
        SeedIndexAsync("books", new object[]
        {
            new { book_id = "BK001", book_code = "RATES-GB-01",    legal_entity = "Barclays Bank PLC",      asset_class = "Rates",     status = "Active",   booking_system = "MUREX" },
            new { book_id = "BK002", book_code = "FX-EU-01",       legal_entity = "BNP Paribas SA",         asset_class = "FX",        status = "Active",   booking_system = "CALYPSO" },
            new { book_id = "BK003", book_code = "CREDIT-US-01",   legal_entity = "Deutsche Bank AG",       asset_class = "Credit",    status = "Active",   booking_system = "SUMMIT" },
            new { book_id = "BK004", book_code = "EQUITY-JP-01",   legal_entity = "Nomura International PLC", asset_class = "Equity", status = "Active",   booking_system = "KONDOR" },
            new { book_id = "BK005", book_code = "REPO-GB-01",     legal_entity = "Barclays Bank PLC",      asset_class = "Rates",     status = "Active",   booking_system = "MUREX" },
            new { book_id = "BK006", book_code = "COMMODITY-US-01", legal_entity = "BlackRock Advisors LLC", asset_class = "Commodity", status = "Active",  booking_system = "MUREX" },
            new { book_id = "BK007", book_code = "FX-GB-02",       legal_entity = "Standard Chartered Bank", asset_class = "FX",       status = "Archived", booking_system = "CALYPSO" },
            new { book_id = "BK008", book_code = "RATES-FR-01",    legal_entity = "Societe Generale SA",    asset_class = "Rates",     status = "Active",   booking_system = "MUREX" },
        }, ct);

    private Task SeedSettlementInstructionsAsync(CancellationToken ct) =>
        SeedIndexAsync("settlementinstructions", new object[]
        {
            new { instruction_id = "SSI001", counterparty_id = "CP001", currency = "GBP", instruction_type = "DVP", status = "Active" },
            new { instruction_id = "SSI002", counterparty_id = "CP002", currency = "USD", instruction_type = "FOP", status = "Active" },
            new { instruction_id = "SSI003", counterparty_id = "CP003", currency = "EUR", instruction_type = "DVP", status = "Active" },
            new { instruction_id = "SSI004", counterparty_id = "CP004", currency = "USD", instruction_type = "RVP", status = "Expired" },
            new { instruction_id = "SSI005", counterparty_id = "CP005", currency = "JPY", instruction_type = "DVP", status = "Active" },
            new { instruction_id = "SSI006", counterparty_id = "CP006", currency = "USD", instruction_type = "FOP", status = "Active" },
            new { instruction_id = "SSI007", counterparty_id = "CP007", currency = "EUR", instruction_type = "DFP", status = "Pending Verification" },
            new { instruction_id = "SSI008", counterparty_id = "CP008", currency = "GBP", instruction_type = "DVP", status = "Active" },
        }, ct);

    private Task SeedCountrysAsync(CancellationToken ct) =>
        SeedIndexAsync("countrys", new object[]
        {
            new { country_code = "GB", country_name = "United Kingdom", iso_alpha3 = "GBR", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "US", country_name = "United States",  iso_alpha3 = "USA", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "DE", country_name = "Germany",        iso_alpha3 = "DEU", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "FR", country_name = "France",         iso_alpha3 = "FRA", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "JP", country_name = "Japan",          iso_alpha3 = "JPN", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "SG", country_name = "Singapore",      iso_alpha3 = "SGP", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "CH", country_name = "Switzerland",    iso_alpha3 = "CHE", fatf_status = "Low",       sanctions_status = "Clean" },
            new { country_code = "RU", country_name = "Russia",         iso_alpha3 = "RUS", fatf_status = "High Risk", sanctions_status = "Full Sanctions" },
            new { country_code = "IR", country_name = "Iran",           iso_alpha3 = "IRN", fatf_status = "Blacklist", sanctions_status = "Full Sanctions" },
            new { country_code = "CN", country_name = "China",          iso_alpha3 = "CHN", fatf_status = "Medium",    sanctions_status = "Watch" },
        }, ct);

    private Task SeedCurrencysAsync(CancellationToken ct) =>
        SeedIndexAsync("currencys", new object[]
        {
            new { currency_code = "GBP", currency_name = "British Pound Sterling",     decimal_places = 2, is_deliverable = true,  is_active = true },
            new { currency_code = "USD", currency_name = "US Dollar",                  decimal_places = 2, is_deliverable = true,  is_active = true },
            new { currency_code = "EUR", currency_name = "Euro",                       decimal_places = 2, is_deliverable = true,  is_active = true },
            new { currency_code = "JPY", currency_name = "Japanese Yen",               decimal_places = 0, is_deliverable = true,  is_active = true },
            new { currency_code = "CHF", currency_name = "Swiss Franc",                decimal_places = 2, is_deliverable = true,  is_active = true },
            new { currency_code = "CNH", currency_name = "Chinese Renminbi (Offshore)", decimal_places = 2, is_deliverable = false, is_active = true },
            new { currency_code = "SGD", currency_name = "Singapore Dollar",           decimal_places = 2, is_deliverable = true,  is_active = true },
            new { currency_code = "INR", currency_name = "Indian Rupee",               decimal_places = 2, is_deliverable = false, is_active = true },
        }, ct);

    private Task SeedUkSicCodesAsync(CancellationToken ct) =>
        SeedIndexAsync("uksiccodes", new object[]
        {
            new { sic_code = "64110", description = "Central banking",                              section = "K", section_description = "Financial and insurance activities", division = "64", group = "641", @class = "6411" },
            new { sic_code = "64191", description = "Banks",                                        section = "K", section_description = "Financial and insurance activities", division = "64", group = "641", @class = "6419" },
            new { sic_code = "64201", description = "Activities of agricultural holding companies", section = "K", section_description = "Financial and insurance activities", division = "64", group = "642", @class = "6420" },
            new { sic_code = "64301", description = "Activities of investment trusts",              section = "K", section_description = "Financial and insurance activities", division = "64", group = "643", @class = "6430" },
            new { sic_code = "64999", description = "Other financial service activities",           section = "K", section_description = "Financial and insurance activities", division = "64", group = "649", @class = "6499" },
            new { sic_code = "65110", description = "Life insurance",                               section = "K", section_description = "Financial and insurance activities", division = "65", group = "651", @class = "6511" },
            new { sic_code = "65120", description = "Non-life insurance",                           section = "K", section_description = "Financial and insurance activities", division = "65", group = "651", @class = "6512" },
            new { sic_code = "66190", description = "Other activities auxiliary to financial services", section = "K", section_description = "Financial and insurance activities", division = "66", group = "661", @class = "6619" },
            new { sic_code = "66290", description = "Other activities auxiliary to insurance",      section = "K", section_description = "Financial and insurance activities", division = "66", group = "662", @class = "6629" },
            new { sic_code = "66300", description = "Fund management activities",                   section = "K", section_description = "Financial and insurance activities", division = "66", group = "663", @class = "6630" },
        }, ct);

    private Task SeedNaceCodesAsync(CancellationToken ct) =>
        SeedIndexAsync("nacecodes", new object[]
        {
            new { nace_code = "K64.11", description = "Central banking",                                       section = "K", division = "64", group = "64.1" },
            new { nace_code = "K64.19", description = "Other monetary intermediation",                         section = "K", division = "64", group = "64.1" },
            new { nace_code = "K64.20", description = "Activities of holding companies",                       section = "K", division = "64", group = "64.2" },
            new { nace_code = "K64.30", description = "Trusts, funds and similar financial entities",          section = "K", division = "64", group = "64.3" },
            new { nace_code = "K64.99", description = "Other financial service activities",                    section = "K", division = "64", group = "64.9" },
            new { nace_code = "K65.11", description = "Life reinsurance",                                      section = "K", division = "65", group = "65.1" },
            new { nace_code = "K65.12", description = "Non-life insurance",                                    section = "K", division = "65", group = "65.1" },
            new { nace_code = "K66.19", description = "Other activities auxiliary to financial services",      section = "K", division = "66", group = "66.1" },
            new { nace_code = "K66.29", description = "Other activities auxiliary to insurance",               section = "K", division = "66", group = "66.2" },
            new { nace_code = "K66.30", description = "Fund management activities",                            section = "K", division = "66", group = "66.3" },
        }, ct);

    private Task SeedRegionsAsync(CancellationToken ct) =>
        SeedIndexAsync("regions", new object[]
        {
            new { region_id = "TLC", region_name = "London",                      country = "GB", is_uk_region = true },
            new { region_id = "TLD", region_name = "South East",                  country = "GB", is_uk_region = true },
            new { region_id = "TLE", region_name = "East of England",             country = "GB", is_uk_region = true },
            new { region_id = "TLF", region_name = "South West",                  country = "GB", is_uk_region = true },
            new { region_id = "TLG", region_name = "West Midlands",               country = "GB", is_uk_region = true },
            new { region_id = "TLH", region_name = "East Midlands",               country = "GB", is_uk_region = true },
            new { region_id = "TLI", region_name = "Yorkshire and The Humber",    country = "GB", is_uk_region = true },
            new { region_id = "TLJ", region_name = "North West",                  country = "GB", is_uk_region = true },
            new { region_id = "TLK", region_name = "North East",                  country = "GB", is_uk_region = true },
            new { region_id = "TLL", region_name = "Scotland",                    country = "GB", is_uk_region = true },
            new { region_id = "TLM", region_name = "Wales",                       country = "GB", is_uk_region = true },
            new { region_id = "TLN", region_name = "Northern Ireland",            country = "GB", is_uk_region = true },
        }, ct);

    private Task SeedLocationsAsync(CancellationToken ct) =>
        SeedIndexAsync("locations", new object[]
        {
            new { location_id = "LOC001", location_name = "Canary Wharf Office",         address_line1 = "1 Canada Square",       city = "London",     postcode = "E14 5AB",  region_id = "TLC", country = "GB", business_type = "Head Office",       status = "Active",       sic_code = "64191", nace_code = "K64.19" },
            new { location_id = "LOC002", location_name = "City of London Branch",        address_line1 = "25 Old Broad Street",   city = "London",     postcode = "EC2N 1HQ", region_id = "TLC", country = "GB", business_type = "Branch",            status = "Active",       sic_code = "64191", nace_code = "K64.19" },
            new { location_id = "LOC003", location_name = "Manchester Office",            address_line1 = "1 Spinningfields",      city = "Manchester", postcode = "M3 3AP",   region_id = "TLJ", country = "GB", business_type = "Regional Office",   status = "Active",       sic_code = "64191", nace_code = "K64.19" },
            new { location_id = "LOC004", location_name = "Edinburgh Finance Centre",     address_line1 = "4 Rutland Square",      city = "Edinburgh",  postcode = "EH1 2AS",  region_id = "TLL", country = "GB", business_type = "Regional Office",   status = "Active",       sic_code = "64999", nace_code = "K64.99" },
            new { location_id = "LOC005", location_name = "Birmingham Branch",            address_line1 = "110 New Street",        city = "Birmingham", postcode = "B2 4HQ",   region_id = "TLG", country = "GB", business_type = "Branch",            status = "Active",       sic_code = "64191", nace_code = "K64.19" },
            new { location_id = "LOC006", location_name = "Leeds Trading Floor",          address_line1 = "1 Wellington Place",    city = "Leeds",      postcode = "LS1 4AP",  region_id = "TLI", country = "GB", business_type = "Trading Office",    status = "Active",       sic_code = "64191", nace_code = "K64.19" },
            new { location_id = "LOC007", location_name = "Bristol Back Office",          address_line1 = "Temple Quay House",     city = "Bristol",    postcode = "BS1 6DZ",  region_id = "TLF", country = "GB", business_type = "Back Office",       status = "Active",       sic_code = "66190", nace_code = "K66.19" },
            new { location_id = "LOC008", location_name = "Cardiff Operations",           address_line1 = "Central Square",        city = "Cardiff",    postcode = "CF10 1EP", region_id = "TLM", country = "GB", business_type = "Operations Centre", status = "Active",       sic_code = "66190", nace_code = "K66.19" },
            new { location_id = "LOC009", location_name = "Glasgow Technology Centre",    address_line1 = "110 St Vincent Street", city = "Glasgow",    postcode = "G2 5UB",   region_id = "TLL", country = "GB", business_type = "Data Centre",       status = "Active",       sic_code = "66190", nace_code = "K66.19" },
            new { location_id = "LOC010", location_name = "Belfast Service Centre",       address_line1 = "1 Donegall Square",     city = "Belfast",    postcode = "BT1 5GB",  region_id = "TLN", country = "GB", business_type = "Service Centre",    status = "Under Review", sic_code = "66190", nace_code = "K66.19" },
        }, ct);
}
