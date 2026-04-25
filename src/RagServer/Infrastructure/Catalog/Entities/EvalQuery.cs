namespace RagServer.Infrastructure.Catalog.Entities;

public class EvalQuery
{
    public int    Id      { get; set; }
    public string UseCase { get; set; } = "";
    public string Query   { get; set; } = "";
    public string Tags    { get; set; } = "";
}
