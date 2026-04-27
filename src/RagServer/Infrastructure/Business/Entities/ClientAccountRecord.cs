using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("ClientAccounts")]
public sealed class ClientAccountRecord
{
    [Key]
    [Column("account_id")]
    [MaxLength(20)]
    public required string AccountId { get; set; }

    [Column("counterparty_id")]
    [MaxLength(20)]
    public required string CounterpartyId { get; set; }

    [Column("account_number")]
    [MaxLength(50)]
    public required string AccountNumber { get; set; }

    [Column("currency")]
    [MaxLength(3)]
    public required string Currency { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public required string Status { get; set; }

    [Column("account_type")]
    [MaxLength(50)]
    public required string AccountType { get; set; }
}
