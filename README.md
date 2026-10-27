# Warhammer Matchmaker

Matchmaking automatique avec classement Elo pour Warhammer 40k sur Tabletop Simulator.
Spécifications : [PROJECT.md](PROJECT.md) · Plan : [PLAN.md](PLAN.md)

## Prérequis

- .NET SDK 10
- Docker
- Une clé d'API Web Steam (https://steamcommunity.com/dev/apikey)

## Démarrer en local

```sh
# 1. Base de données (PostgreSQL sur localhost:5455)
docker compose up -d

# 2. Clé Steam, stockée hors du dépôt (une seule fois)
dotnet user-secrets set "Steam:ApplicationKey" "<votre clé>" --project src/Matchmaker.Web

# 3. Application (les migrations sont appliquées au démarrage)
dotnet run --project src/Matchmaker.Web --launch-profile https
```

Puis ouvrir https://localhost:7040.

## Tests

```sh
dotnet test
```

Les tests web lancent un PostgreSQL jetable via Docker (Testcontainers) : Docker doit tourner.

## Migrations

```sh
dotnet tool restore
dotnet ef migrations add <Nom> -p src/Matchmaker.Web -o Data/Migrations
```
