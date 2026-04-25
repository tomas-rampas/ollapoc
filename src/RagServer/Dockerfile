FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/RagServer/RagServer.csproj", "RagServer/"]
RUN dotnet restore "RagServer/RagServer.csproj"
COPY src/RagServer/ RagServer/
RUN dotnet publish "RagServer/RagServer.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root user
RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "RagServer.dll"]
