using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("NaceCodes")]
public sealed class NaceCodeRecord
{
    [Key]
    [Column("nace_code")]
    [MaxLength(20)]
    public required string NaceCode { get; set; }

    [Column("description")]
    [MaxLength(500)]
    public required string Description { get; set; }

    [Column("section")]
    [MaxLength(5)]
    public string? Section { get; set; }

    [Column("division")]
    [MaxLength(5)]
    public string? Division { get; set; }

    // "group" is a reserved C# keyword; use GroupCode for the property name
    [Column("group_code")]
    [MaxLength(10)]
    public string? GroupCode { get; set; }
}
