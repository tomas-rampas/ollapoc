using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Currencies")]
public sealed class CurrencyRecord
{
    [Key]
    [Column("currency_code")]
    [MaxLength(3)]
    public required string CurrencyCode { get; set; }

    [Column("currency_name")]
    [MaxLength(100)]
    public required string CurrencyName { get; set; }

    [Column("decimal_places")]
    public int DecimalPlaces { get; set; }

    [Column("is_deliverable")]
    public bool IsDeliverable { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }
}
