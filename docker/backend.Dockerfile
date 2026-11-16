# ---------------------------------------------------------------
# Nodia API – UTVECKLINGS-image (inte för produktion)
#
# Imagen innehåller bara .NET 10 SDK och projektets NuGet-paket.
# Själva källkoden monteras in från din dator via compose.yaml
# (bind mount), så att `dotnet watch` ser dina ändringar direkt.
# ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /src

# 1) Kopiera BARA projektfilen och kör restore.
#    Docker cachar varje steg (lager). Så länge NodiaApi.csproj inte
#    ändras återanvänds detta lager och paketen laddas inte ner igen.
COPY NodiaApi.csproj ./
RUN dotnet restore NodiaApi.csproj

# 2) Porten API:et lyssnar på inne i containern (sätts via ASPNETCORE_URLS).
EXPOSE 8080

# 3) Starta med hot reload. --no-launch-profile gör att
#    Properties/launchSettings.json (som är gjord för Windows/Visual Studio)
#    ignoreras; portar och miljö kommer i stället från compose.yaml.
ENTRYPOINT ["dotnet", "watch", "run", "--project", "NodiaApi.csproj", "--no-launch-profile"]
