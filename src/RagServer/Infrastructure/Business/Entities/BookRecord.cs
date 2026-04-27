using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("Books")]
public sealed class BookRecord
{
    [Key]
    [Column("book_id")]
    [MaxLength(20)]
    public required string BookId { get; set; }

    [Column("book_code")]
    [MaxLength(50)]
    public required string BookCode { get; set; }

    [Column("legal_entity")]
    [MaxLength(200)]
    public required string LegalEntity { get; set; }

    [Column("asset_class")]
    [MaxLength(50)]
    public required string AssetClass { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public required string Status { get; set; }

    [Column("booking_system")]
    [MaxLength(50)]
    public required string BookingSystem { get; set; }
}
