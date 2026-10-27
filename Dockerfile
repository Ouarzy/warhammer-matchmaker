# Image de production du site (voir deploy/ et docs/DEPLOIEMENT.md).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauration des paquets dans une couche à part : elle reste en cache tant que les projets ne changent pas.
COPY src/Matchmaker.Domain/Matchmaker.Domain.csproj src/Matchmaker.Domain/
COPY src/Matchmaker.Web/Matchmaker.Web.csproj src/Matchmaker.Web/
RUN dotnet restore src/Matchmaker.Web/Matchmaker.Web.csproj

COPY src/ src/
COPY tts/WarhammerMatchmaker.lua tts/
# Sans --no-restore : la publication ajoute ses propres paquets (dont les scripts Blazor de _framework),
# absents d'une restauration faite seule.
RUN dotnet publish src/Matchmaker.Web/Matchmaker.Web.csproj -c Release -o /app
# Garde-fou : sans ce script, aucune page interactive ne fonctionne.
RUN test -f /app/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Rattache l'image publiée sur ghcr.io au dépôt GitHub (même visibilité, lien vers le code).
LABEL org.opencontainers.image.source="https://github.com/Ouarzy/warhammer-matchmaker"
WORKDIR /app

# Dossier des clés de connexion, monté sur un volume en production.
RUN mkdir -p /data/keys && chown "$APP_UID" /data/keys
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/data/keys

COPY --from=build /app .

# Utilisateur sans privilèges fourni par l'image .NET.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Matchmaker.Web.dll"]
