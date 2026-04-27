using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Counterparties")]
public sealed class CounterpartyRecord
{
    [Key]
    [Column("counterparty_id")]
    [MaxLength(20)]
    public required string CounterpartyId { get; set; }

    [Column("lei")]
    [MaxLength(20)]
    public string? Lei { get; set; }

    [Column("legal_name")]
    [MaxLength(200)]
    public required string LegalName { get; set; }

    [Column("short_name")]
    [MaxLength(100)]
    public string? ShortName { get; set; }

    [Column("entity_type")]
    [MaxLength(50)]
    public required string EntityType { get; set; }

    [Column("incorporation_country")]
    [MaxLength(2)]
    public required string IncorporationCountry { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public required string Status { get; set; }
}
