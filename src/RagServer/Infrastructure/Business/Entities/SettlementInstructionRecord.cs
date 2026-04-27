using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("SettlementInstructions")]
public sealed class SettlementInstructionRecord
{
    [Key]
    [Column("instruction_id")]
    [MaxLength(20)]
    public required string InstructionId { get; set; }

    [Column("counterparty_id")]
    [MaxLength(20)]
    public required string CounterpartyId { get; set; }

    [Column("currency")]
    [MaxLength(3)]
    public required string Currency { get; set; }

    [Column("instruction_type")]
    [MaxLength(20)]
    public required string InstructionType { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public required string Status { get; set; }
}
