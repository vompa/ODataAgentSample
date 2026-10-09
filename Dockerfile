# Build-Kontext ist das Repository-Root: docker build -t odata-agent-sample .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Erst nur die Projektdateien kopieren, damit der Restore-Layer im Cache bleibt
COPY Directory.Build.props ./
COPY src/ODataAgentSample/ODataAgentSample.csproj src/ODataAgentSample/
RUN dotnet restore src/ODataAgentSample/ODataAgentSample.csproj

# Die JSON-Daten werden als EmbeddedResource eingebunden (siehe csproj)
COPY data/ data/
COPY src/ src/
RUN dotnet publish src/ODataAgentSample/ODataAgentSample.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Nicht als root laufen (das Laufzeit-Image bringt den Benutzer "app" mit). Port 8080 ist der Standard dieser Images.
USER app
# Die Datenbank wird beim Start aus den eingebetteten JSON-Dateien neu aufgebaut; /tmp ist beschreibbar, aber fluechtig.
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Database__ConnectionString="Data Source=/tmp/odata-agent.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "ODataAgentSample.dll"]
