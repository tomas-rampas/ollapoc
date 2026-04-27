using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RagServer.Infrastructure.Business.Entities;

[Table("UkSicCodes")]
public sealed class UkSicCodeRecord
{
    [Key]
    [Column("sic_code")]
    [MaxLength(10)]
    public required string SicCode { get; set; }

    [Column("description")]
    [MaxLength(500)]
    public required string Description { get; set; }

    [Column("section")]
    [MaxLength(5)]
    public string? Section { get; set; }

    [Column("section_description")]
    [MaxLength(200)]
    public string? SectionDescription { get; set; }

    [Column("division")]
    [MaxLength(5)]
    public string? Division { get; set; }

    // "group" is a reserved C# keyword; use GroupCode for the property name
    [Column("group_code")]
    [MaxLength(5)]
    public string? GroupCode { get; set; }

    // "class" is also reserved; use ClassCode
    [Column("class_code")]
    [MaxLength(5)]
    public string? ClassCode { get; set; }
}
