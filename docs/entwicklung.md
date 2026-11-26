# Entwicklung

## Voraussetzungen

| Werkzeug | Version / Hinweis |
|---|---|
| Windows | x64. Die WPF-App, das Setup und die Signierung lassen sich nur unter Windows bauen und prüfen. |
| .NET SDK | laut [`global.json`](../global.json) (10.0.100 oder neueres Feature-Band). Ein SDK pro Benutzer unter `%LOCALAPPDATA%\Microsoft\dotnet` wird von `Build-Release.ps1` gefunden. |
| PowerShell | 7 (`pwsh`) für die Skripte unter `build/` |
| Velopack CLI `vpk` | als lokales Tool aus [`dotnet-tools.json`](../dotnet-tools.json): `dotnet tool restore` |
| Microsoft Edge | nur für das Handbuch als PDF (headless); ohne Edge `Build-Manual.ps1 -SkipPdf` |
| Node.js | 22, nur für `openspec validate` und das Rendern der Bilder unter `tools/` |
| Git | – |

Für Testläufe des Servers `AZDOMCP_DATA` auf einen temporären Ordner setzen, damit das echte Profil unter
`%LOCALAPPDATA%\AzdoMcpData\` unberührt bleibt.

## Bauen und testen

```powershell
dotnet restore AzdoMcp.sln
dotnet build AzdoMcp.sln -c Release
dotnet run --project tests/AzdoMcp.Tests -c Release
openspec validate --all --strict
```

Die Tests (xUnit v3) laufen als ausführbares Programm, mit Fakes für HTTP und DPAPI; sie brauchen weder ein
Azure DevOps noch Admin-Rechte. Unter Linux oder WSL mit einem Linux-SDK lässt sich die Solution nur mit
`-p:EnableWindowsTargeting=true` bauen.

## Handbuch bauen

Das Benutzerhandbuch liegt als Markdown in [`docs/manual/`](manual/) (`index.md` und Kapitel `NN-*.md`).
`build/Build-Manual.ps1` baut daraus mit `build/ManualBuilder` eine HTML-Datei und mit Edge ein PDF:

```powershell
pwsh ./build/Build-Manual.ps1 -SkipPdf      # nur HTML, auch unter Linux
pwsh ./build/Build-Manual.ps1               # HTML und PDF (Windows mit Edge)
```

| Parameter | Standard | Bedeutung |
|---|---|---|
| `-Version` | `<Version>` aus `Directory.Build.props` | Version auf der Titelseite |
| `-OutputDir` | `artifacts/manual` | Zielordner |
| `-Name` | `AzdoMcp-Handbuch` | Dateiname von `.html` und `.pdf` |
| `-EdgePath` | wird gesucht | Pfad zu `msedge.exe` |
| `-SkipPdf` | – | nur HTML bauen |
| `-Configuration` | `Release` | Build-Konfiguration des ManualBuilder |

Ein Test (`ManualCompletenessTests` in `tests/AzdoMcp.Tests`) prüft, dass das Handbuch jedes Tool, jede
Tool-Gruppe, die Modi, die Footer-Texte und die Kommandozeile beschreibt.

Bilder des Handbuchs und der README entstehen aus den UI-Entwürfen, siehe
[`docs/manual/images/README.md`](manual/images/README.md).

## Release bauen

```powershell
./build/Build-Release.ps1 -Version 2.5.0-rc.2                                              # unsigniert
./build/Build-Release.ps1 -Version 2.5.0 -CertificateThumbprint <Thumbprint>               # mit installiertem Zertifikat
./build/Build-Release.ps1 -Version 2.5.0 -PfxPath <Pfad.pfx> -PfxPassword (Read-Host -AsSecureString)
```

| Parameter | Standard | Bedeutung |
|---|---|---|
| `-Version` | Pflicht | `x.y.z` oder `x.y.z-suffix` |
| `-PfxPath` | – | Zertifikat als PFX; wird für den Build nach `Cert:\CurrentUser\My` importiert und danach entfernt |
| `-PfxPassword` | – | Passwort der PFX als `SecureString` (nie auf der Kommandozeile im Klartext) |
| `-CertificateThumbprint` | – | bereits installiertes Zertifikat; schliesst `-PfxPath` aus |
| `-TimestampUrl` | `http://timestamp.digicert.com` | Zeitstempel-Server für die Signatur |
| `-OutputDir` | `artifacts/release` | Ausgabeordner |
| `-SkipTests` | – | Tests überspringen (nur lokal) |

Ablauf: Restore, Build, Tests, Publish beider EXE (self-contained win-x64) nach `artifacts/pack`, Handbuch-PDF
dazu, mit Zertifikat beide EXE signieren, Portable-ZIP, `vpk pack`, Signaturprüfung, `SHA256SUMS.txt`. Das
Skript bricht mit einem Exit-Code ungleich 0 ab, sobald ein Schritt – auch ein Test – fehlschlägt. Ohne
Zertifikat warnt es «release is NOT signed».

Ergebnis in `artifacts/release`:

- `AzdoMcp-win-Setup.exe` – Setup pro Benutzer, ohne Admin-Rechte
- `AzdoMcp-<Version>-portable.zip` – ohne Installation, ohne Updates
- `AzdoMcp-<Version>-full.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json` – Velopack-Updates
- `AzdoMcp-Handbuch-<Version>.pdf` – Benutzerhandbuch
- `SHA256SUMS.txt` – Prüfsummen aller anderen Dateien

## Workflows

| Workflow | Auslöser | Jobs und Ergebnis |
|---|---|---|
| [CI](../.github/workflows/ci.yml) | Push auf `main`, Pull Request, manuell | `Build and test (Windows)`: Restore, Build, Tests, Handbuch als HTML; Artefakte `test-results-Windows` und `manual-html`. `OpenSpec validate`: `openspec validate --all --strict`. Beide Jobs sind Pflicht-Checks für `main`. |
| [Release](../.github/workflows/release.yml) | Tag `v*.*.*`, manuell mit bestehendem Tag | Tag prüfen, Zertifikat aus den Secrets `SIGNING_PFX_BASE64` und `SIGNING_PFX_PASSWORD`, `Build-Release.ps1`, Zertifikat entfernen, GitHub Release «AzDO MCP Manager v…» mit den Pflicht-Assets. Reguläre Releases ohne Zertifikat schlagen fehl; Prereleases (Tag mit `-`, z. B. `v2.5.0-rc.2`) dürfen unsigniert sein. |

## Projektstruktur

```text
src/
  AzdoMcp.Core/           Verbindungen, DPAPI-Secrets, REST-Client, Tools, Protokoll (net10.0)
  AzdoMcp.Server/         azdo-mcp.exe: --stdio und discover, MCP-SDK (net10.0)
  AzdoMcp.Manager.Core/   ViewModels, Wizard, Pipe-Host, Client-Einrichtung (net10.0, testbar)
  AzdoMcp.Manager/        WPF-Tray-App AzdoMcp.Manager.exe (net10.0-windows)
tests/
  AzdoMcp.Tests/          xUnit v3 als EXE, Fakes für HTTP und DPAPI, Vollständigkeit des Handbuchs
build/
  Build-Release.ps1       Release: Build, Tests, Signierung, Pakete, Prüfsummen
  Build-Manual.ps1        Handbuch als HTML und PDF
  ManualBuilder/          Markdown -> HTML (nicht in AzdoMcp.sln, nicht ausgeliefert)
tools/                    Entwicklerwerkzeuge (Node): render-images.mjs rendert die Bilder
docs/
  manual/                 Benutzerhandbuch (Markdown), images/ mit den Bildern
  assets/                 Bilder der README
  ui-proposal/            UI-Entwürfe (deutsche Texte verbindlich)
  konzept/                Konzepte (Stand bei Erstellung)
openspec/                 Anforderungen, Entscheide, Aufgaben (spec-driven)
```

## Arbeitsweise: spec-driven mit OpenSpec

Jede Funktion beginnt als Anforderung mit Szenarien, nicht als Code.

1. Change unter `openspec/changes/<change>/` anlegen: `proposal.md`, `design.md`, `tasks.md` und die
   Spec-Deltas unter `specs/`.
2. `openspec validate --all --strict` muss grün sein.
3. Aufgaben aus `tasks.md` umsetzen und abhaken, mindestens ein automatisierter Test je Anforderung.
4. Nach dem Merge den Change archivieren (`openspec/changes/archive/`); die Specs unter `openspec/specs/`
   beschreiben dann den aktuellen Stand.

Widerspricht eine Regel in [`CLAUDE.md`](../CLAUDE.md) einer Spec: anhalten und den Owner fragen.

## Konventionen

- C# latest, Nullable an, Warnungen sind Fehler. Paketversionen zentral in `Directory.Packages.props`.
- Code, Kommentare und Commits Englisch; Oberfläche und Dokumentation Deutsch, Handbuch in Sie-Form und
  Schweizer Rechtschreibung.
- Keine echten PATs, Server-URLs, Namen oder Kundendaten – Platzhalter
  `https://azdo.example.local/DefaultCollection`, «Max Muster», Projekte SHOP, OPS, Musterlab.

## Beitragen

Siehe [CONTRIBUTING.md](../CONTRIBUTING.md). Sicherheitslücken bitte nur nach [SECURITY.md](../SECURITY.md)
melden.
