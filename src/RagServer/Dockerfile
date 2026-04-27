FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/RagServer/RagServer.csproj", "RagServer/"]
RUN dotnet restore "RagServer/RagServer.csproj"

COPY src/RagServer/ RagServer/
# Do NOT pass --no-restore: Blazor SDK targets that emit _framework/ static web assets
# run during the restore phase of dotnet publish. Skipping restore with --no-restore
# causes blazor.web.js to be absent from the staticwebassets manifest and wwwroot/_framework/.
RUN dotnet publish "RagServer/RagServer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

RUN useradd --no-create-home --shell /bin/false appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "RagServer.dll"]
