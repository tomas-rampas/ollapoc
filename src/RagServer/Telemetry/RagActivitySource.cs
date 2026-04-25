using System.Diagnostics;

namespace RagServer.Telemetry;

public static class RagActivitySource
{
    public static readonly ActivitySource Source = new("RagServer");
}
