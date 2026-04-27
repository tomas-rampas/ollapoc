using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Settlements")]
public sealed class SettlementRecord
{
    [Key]
    [Column("settlement_id")]
    [MaxLength(20)]
    public required string SettlementId { get; set; }

    [Column("counterparty_id")]
    [MaxLength(20)]
    public required string CounterpartyId { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("currency")]
    [MaxLength(3)]
    public required string Currency { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public required string Status { get; set; }

    [Column("settlement_date")]
    [MaxLength(20)]
    public required string SettlementDate { get; set; }
}
