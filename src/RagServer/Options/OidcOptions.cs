namespace RagServer.Options;

public sealed class OidcOptions
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackPath { get; set; } = "/signin-oidc";
}
