# Nodia – utvecklingsmiljö med Docker

Startar **backend** (ASP.NET Core, .NET 10) och **frontend** (Vite + React) med ett
kommando. Databasen ligger kvar i **Supabase Cloud**, ingen lokal Postgres.

> Detta är en **utvecklingsmiljö**: källkoden monteras in i containrarna och
> hot reload är påslaget. Använd den inte för produktion.

## Förutsättningar

- Docker Desktop för Windows med **WSL2-backend** (Settings → General).
- Mappstruktur:
  ```
  Desktop\Projekt\nodia\
  ├── NodiaApi\   ← detta repo (compose.yaml ligger här)
  └── nodia\      ← frontendrepot
  ```
- Connection string satt i user-secrets (görs en gång):
  ```powershell
  cd $HOME\Desktop\Projekt\nodia\NodiaApi
  dotnet user-secrets set "ConnectionStrings:Supabase" "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require"
  ```

## Starta och stoppa

Kör från **PowerShell** (inte WSL-terminalen, se Felsökning), i `NodiaApi`-mappen:

| Vad | Kommando |
|---|---|
| Starta allt (första gången / efter ändrade paket) | `docker compose up --build` |
| Starta allt i bakgrunden | `docker compose up -d` |
| Stoppa allt (Ctrl+C om det körs i förgrunden) | `docker compose down` |
| Status för containrarna | `docker compose ps` |
| Starta om en tjänst | `docker compose restart backend` |
| Bygga om en tjänst | `docker compose up -d --build frontend` |
| Nollställa allt (raderar volymer: node_modules, bin/obj, NuGet-cache) | `docker compose down -v` |

När allt körs:

| Tjänst | Adress |
|---|---|
| Frontend | http://localhost:5173 |
| Backend / Swagger | http://localhost:5080/swagger |
| Backend via frontendens proxy | http://localhost:5173/api/health/db |

## Loggar

```powershell
docker compose logs -f              # alla tjänster, följ live
docker compose logs -f backend      # bara backend
docker compose logs -f frontend     # bara frontend
docker compose logs --tail 100 backend
```

Loggarna syns också i Docker Desktop → Containers → `nodia`.

## Så fungerar det

```
Webbläsare ──► localhost:5173 ──► [frontend]  vite dev
                                      │ /api/*  (Vite-proxy)
                                      ▼
               localhost:5080 ──► [backend]   dotnet watch ──► Supabase Cloud
```

- **Nätverk:** Compose skapar nätverket `nodia_default`. Containrarna når
  varandra via **tjänstenamnet** (`backend`, `frontend`). Inne i en container
  betyder `localhost` containern själv, därför pekar proxyn på `http://backend:8080`.
- **Portar:** `"5080:8080"` betyder *port på din dator : port i containern*.
  Backend lyssnar på 8080 inne i containern och nås på 5080 utifrån. Det
  krockar inte med 5131/7172 som Visual Studio använder.
- **Frontend → backend:** frontendkoden ska anropa relativa adresser som
  `fetch("/api/...")`. Vite skickar vidare till backend. Då behövs ingen CORS
  och samma kod fungerar med och utan Docker.
- **Hot reload:** källkoden monteras in (bind mount). Windows-filer i WSL2 ger
  inga ändringshändelser, så **polling** är påslaget
  (`DOTNET_USE_POLLING_FILE_WATCHER`, `WATCH_POLLING`).
- **Volymer:** `node_modules`, `bin`, `obj` och NuGet-cachen ligger i
  Docker-volymer. Linux-bygget krockar då inte med Windows-filerna som Visual
  Studio och `npm` på Windows använder.
- **HTTPS:** containrarna kör bara HTTP lokalt. En varning om
  "Failed to determine the https port" i backendloggen är väntad och ofarlig.

## Hemligheter och miljövariabler

- **Connection string** finns bara i dina user-secrets
  (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`). Mappen
  monteras **read-only** i backend-containern. Den finns inte i repot, inte i
  imagen och inte i `compose.yaml`.
- **Icke-hemliga inställningar** (miljö, portar, proxy-mål) står direkt i
  `compose.yaml`.
- Supabase-URL:en ligger i `appsettings.json`. Den är inte hemlig.
- `.env`-filer är gitignorerade om du senare behöver sådana.

## Köra utan Docker (som tidigare)

- **Backend:** öppna `NodiaApi.slnx` i Visual Studio och tryck Run, eller
  `dotnet run --launch-profile https`. Swagger finns på https://localhost:7172/swagger.
- **Frontend:** `cd ..\nodia`, sedan `npm install` och `npm run dev`. `/api`
  proxas automatiskt till `http://localhost:5131`.

Kör inte Docker-frontenden och den lokala frontenden samtidigt, eftersom båda
använder port 5173.

## Felsökning

**En container startar inte / stängs direkt**
```powershell
docker compose ps -a                 # visar status och exit-kod
docker compose logs backend          # läs felet
docker compose up backend            # starta bara den, i förgrunden
docker compose run --rm backend bash # gå in i en container och undersök (frontend: sh)
```

**`APPDATA saknas`**: du kör från WSL/Git Bash. Kör från PowerShell eller CMD.

**`port is already allocated`**: något annat använder porten. Stoppa den lokala
Vite- eller VS-instansen, eller hitta processen:
`Get-NetTCPConnection -LocalPort 5173 | Select OwningProcess`.

**Backend: `ConnectionString` är null / kan inte ansluta**
- Kontrollera user-secrets: `dotnet user-secrets list`.
- Kontrollera att filen syns i containern:
  `docker compose exec backend ls /root/.microsoft/usersecrets`.
- Använd Supabase **Session pooler**-strängen (IPv4). Direct connection är IPv6.

**Ändringar syns inte (ingen hot reload)**
- Kontrollera att `WATCH_POLLING` och `DOTNET_USE_POLLING_FILE_WATCHER` är satta:
  `docker compose exec backend env`.
- Vissa ändringar (t.ex. i `Program.cs`) kräver omstart. `dotnet watch` gör det
  automatiskt, se loggen.

**Nya npm-paket eller NuGet-paket**
Volymerna behåller gamla paket. Bygg om:
```powershell
docker compose down
docker volume rm nodia_frontend-node-modules   # eller nodia_backend-obj
docker compose up --build
```

**`Cannot find module @rolldown/binding-linux-...`**: Windows-`node_modules`
har hamnat i containern. Kör `docker compose down -v` och `docker compose up --build`.

**Börja helt om:** `docker compose down -v --rmi local`, sedan `docker compose up --build`.
