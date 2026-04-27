using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Locations")]
public sealed class LocationRecord
{
    [Key]
    [Column("location_id")]
    [MaxLength(20)]
    public required string LocationId { get; set; }

    [Column("location_name")]
    [MaxLength(200)]
    public required string LocationName { get; set; }

    [Column("address_line1")]
    [MaxLength(200)]
    public string? AddressLine1 { get; set; }

    [Column("city")]
    [MaxLength(100)]
    public string? City { get; set; }

    [Column("postcode")]
    [MaxLength(20)]
    public string? Postcode { get; set; }

    [Column("region_id")]
    [MaxLength(10)]
    public string? RegionId { get; set; }

    [Column("country")]
    [MaxLength(2)]
    public string? Country { get; set; }

    [Column("business_type")]
    [MaxLength(50)]
    public string? BusinessType { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public required string Status { get; set; }

    [Column("sic_code")]
    [MaxLength(10)]
    public string? SicCode { get; set; }

    [Column("nace_code")]
    [MaxLength(10)]
    public string? NaceCode { get; set; }
}
