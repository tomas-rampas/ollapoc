using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Countries")]
public sealed class CountryRecord
{
    [Key]
    [Column("country_code")]
    [MaxLength(2)]
    public required string CountryCode { get; set; }

    [Column("country_name")]
    [MaxLength(100)]
    public required string CountryName { get; set; }

    [Column("iso_alpha3")]
    [MaxLength(3)]
    public required string IsoAlpha3 { get; set; }

    [Column("fatf_status")]
    [MaxLength(50)]
    public required string FatfStatus { get; set; }

    [Column("sanctions_status")]
    [MaxLength(50)]
    public required string SanctionsStatus { get; set; }
}
