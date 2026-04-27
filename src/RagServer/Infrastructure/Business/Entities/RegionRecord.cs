using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Regions")]
public sealed class RegionRecord
{
    [Key]
    [Column("region_id")]
    [MaxLength(10)]
    public required string RegionId { get; set; }

    [Column("region_name")]
    [MaxLength(100)]
    public required string RegionName { get; set; }

    [Column("country")]
    [MaxLength(2)]
    public required string Country { get; set; }

    [Column("is_uk_region")]
    public bool IsUkRegion { get; set; }
}
