# CI, PR-flöde och auto-merge – NodiaApi

Allt utvecklingsarbete sker på feature-brancher. `main` ändras bara via PR:er
som har passerat alla obligatoriska kontroller. Samma upplägg finns i frontendrepot
`nodia` (som dessutom har en Lovable-branch, se dess `CI.md`).

## Flödet

1. Pusha en branch som heter `feature/<något>`.
2. **Auto PR** skapar en **draft-PR** mot `main` (om ingen öppen PR finns).
3. **Backend CI** och **Sensitive change gate** körs på PR:en vid varje push.
4. Markera PR:en **Ready for review** → **Auto-merge** slås på (squash).
5. GitHub mergar automatiskt när alla obligatoriska kontroller är gröna.
   Görs PR:en om till draft stängs auto-merge av igen.

Direkt push, force push och radering av `main` blockeras av rulesetet
`main-protection`. Inga undantag finns – inte för admin, inte för appen.

## Workflows

| Fil | Händelse | Gör | Rättigheter |
|---|---|---|---|
| `ci.yml` | PR mot `main`, push till `main` | restore, build, `dotnet test` (integrationstester med Testcontainers/PostgreSQL) | `contents: read` |
| `auto-pr.yml` | push till `feature/**` | skapar draft-PR om ingen finns och branchen har ändringar | app-token: contents read, pull requests write |
| `auto-merge.yml` | `pull_request_target`: opened, reopened, ready_for_review, converted_to_draft | slår på/av auto-merge (squash); bara PR:er från detta repo, aldrig forkar | app-token: contents write, pull requests write |
| `sensitive-gate.yml` | `pull_request_target`: opened, reopened, synchronize, edited, labeled, unlabeled | spärr för känsliga ändringar | `GITHUB_TOKEN`: contents read, pull-requests write |

En PR som öppnas direkt som "ready" (inte draft) får också auto-merge.

## Obligatoriska kontroller (status checks)

| Check | Workflow |
|---|---|
| `Restore, build & test` | `ci.yml` |
| `Sensitive change gate` | `sensitive-gate.yml` |

Båda är låsta till GitHub Actions (`integration_id` 15368), så ingen kan sätta en
grön status manuellt via API:t. Byter du namn på ett jobb måste rulesetet uppdateras.

## Känsliga ändringar

Rör en PR någon av sökvägarna i `SENSITIVE_PATTERNS` i `sensitive-gate.yml` blir
`Sensitive change gate` röd tills en människa godkänner:

- autentisering och behörigheter: `Auth/**`, `Program.cs`
- databas: `Migrations/**`, `Data/**`, `Models/**`
- konfiguration och beroenden: `appsettings*.json`, `*.csproj`, `*.slnx`, `dotnet-tools.json` m.fl.
- container: `docker/**`, `compose.yaml`, `.dockerignore`
- säkerhetstester och testinfrastruktur: `tests/*Security*`, `*Authentication*`, `*Isolation*`, `tests/*/Infrastructure/**`
- CI: `.github/**`

**Så godkänner du:** granska ändringarna och sätt etiketten `approved-sensitive` på PR:en.

Godkännandet räknas bara om:

- etiketten sattes av en **människa** (inte en bot och inte via en app),
- personen har rollen **admin** eller **maintain** i repot,
- etiketten sattes **efter den senaste pushen** till PR-branchen (GitHubs egen
  tidsstämpel) och efter ett eventuellt byte av basbranch.

Vid varje ny push, även *Update branch*, blir godkännandet ogiltigt. Etiketten tas
då bort automatiskt och måste sättas igen efter ny granskning.

Spärren checkar aldrig ut och kör aldrig kod från PR:en. Den läser bara GitHubs API
och hämtas alltid från `main` (`pull_request_target`), så en PR kan inte ändra den.

## GitHub App (`PR_BOT`)

En egen GitHub App behövs eftersom PR:er och mergar som görs med `GITHUB_TOKEN`
inte startar andra workflows. Utan appen skulle CI aldrig köras på de automatiska
PR:erna och auto-merge skulle aldrig slutföras.

**Repository permissions** (allt annat: *No access*):

| Behörighet | Nivå | Används till |
|---|---|---|
| Contents | Read and write | jämföra brancher; utföra squash-mergen; (frontend) synka `feature/lovable` |
| Pull requests | Read and write | skapa draft-PR, slå på/av auto-merge |
| Metadata | Read-only | obligatorisk |

Appen får **inte** ha *Workflows*, *Administration* eller bypass i något ruleset.
Konsekvens: en PR som ändrar `.github/workflows/**` kan inte mergas av appen –
auto-merge avbryts och du mergar den manuellt (den kräver ändå `approved-sensitive`).

Varje workflow begränsar dessutom sin token till det steget behöver.

## Inställningar att göra i GitHub

Gör stegen i denna ordning. Ruleset aktiveras sist. Annars kan PR:en som inför
workflowen inte mergas, eftersom `Sensitive change gate` ännu inte finns på `main`.

### 1. Skapa GitHub-appen (en gång, gemensam för båda repona)
*Settings (ditt konto) → Developer settings → GitHub Apps → New GitHub App*

- **GitHub App name:** valfritt unikt namn, t.ex. `nodia-pr-bot-<suffix>`
- **Homepage URL:** repots URL
- **Webhook:** avmarkera *Active*
- **Repository permissions:** enligt tabellen ovan
- **Where can this GitHub App be installed?** *Only on this account*
- Skapa appen → notera **Client ID** → *Generate a private key* (`.pem`-fil)
- *Install App* → ditt konto → **Only select repositories**: `NodiaApi` och `nodia`

### 2. Variabel och secret (i båda repona)
*Settings → Secrets and variables → Actions*

- **Variables** → `PR_BOT_CLIENT_ID` = appens Client ID
- **Secrets** → `PR_BOT_PRIVATE_KEY` = hela innehållet i `.pem`-filen

Radera `.pem`-filen lokalt efteråt.

### 3. Etikett
*Issues → Labels → New label*: `approved-sensitive`.

### 4. Pull Requests-inställningar
*Settings → General → Pull Requests*

- ☐ Allow merge commits
- ☑ Allow squash merging
- ☐ Allow rebase merging
- ☑ Always suggest updating pull request branches
- ☑ **Allow auto-merge**
- ☑ Automatically delete head branches (**NodiaApi**; se frontendens `CI.md` för `nodia`)

### 5. Actions-inställningar
*Settings → Actions → General*

- **Workflow permissions:** *Read repository contents and packages permissions*
- ☐ **Allow GitHub Actions to create and approve pull requests** (ska vara avstängd)
- **Approval for running fork pull request workflows:** *Require approval for all external contributors*

### 6. Merga workflowen till main
Pusha branchen `ci/pr-automation` och öppna en PR manuellt. Merga den när
`Restore, build & test` är grön. Rulesetet är ännu inte aktivt.

### 7. Importera rulesetet
*Settings → Rules → Rulesets → New ruleset → Import a ruleset* → välj
`.github/rulesets/main-protection.json`. Kontrollera att *Bypass list* är tom och att
*Enforcement status* är *Active*.

Alternativt med GitHub CLI:

```sh
gh api repos/Lirijes/NodiaApi/rulesets --method POST --input .github/rulesets/main-protection.json
```

### 8. Verifiera
1. Redigera en fil direkt på `main` i GitHubs webbgränssnitt → GitHub ska kräva att
   du skapar en ny branch (direkt commit till `main` är spärrad).
2. Pusha `feature/ci-smoke-test` med en ofarlig ändring (t.ex. i `DOCKER.md`) →
   en draft-PR skapas, båda kontrollerna körs och blir gröna.
3. Markera *Ready for review* → PR:en squash-mergas automatiskt.
4. Ändra en fil i `Auth/` på en ny feature-branch → `Sensitive change gate` blir röd.
   Sätt `approved-sensitive` → grön. Pusha igen → etiketten tas bort och kontrollen blir röd.
5. Kontrollera att `Sensitive change gate` syns under *Checks* på PR:en och att
   *Merge*-knappen är spärrad medan den är röd.

## Kända begränsningar

- Spärren bygger på sökvägar. Känslig kod som läggs utanför listan fångas inte.
  Uppdatera `SENSITIVE_PATTERNS` när strukturen ändras (själva ändringen är känslig).
- Strax efter en push kan GitHubs aktivitets-API sakna pushen ännu. Spärren blir då
  röd med "Kör om kontrollen om en stund". Kör om jobbet eller sätt etiketten igen.
- Med "Require branches to be up to date" kan en PR behöva *Update branch* om `main`
  har ändrats. Det räknas som en push och ogiltigförklarar ett ev. godkännande.
