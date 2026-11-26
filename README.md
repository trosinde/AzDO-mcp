# AzDO-mcp – AzDO MCP Manager v2

MCP server for Azure DevOps (Azure DevOps Server on-prem and Azure DevOps Services) for
Claude Code, Claude Desktop, OpenCode and VS Code/GitHub Copilot – Windows, no admin rights.

- `azdo-mcp.exe --stdio` – the MCP server the client starts.
- `AzdoMcp.Manager.exe` – tray app: connections and PATs, tool permissions, approvals, client setup.

## Status

| Change | State |
|---|---|
| `add-mcp-core` – server, connections, 22 tools, audit log | implemented |
| `add-tool-permissions` – Erlaubt / Nachfragen / Gesperrt, approvals | implemented |
| `add-tray-manager` – WPF tray app | implemented, manual checks open |
| `add-windows-packaging` – per-user installer, portable zip | implemented |
| `add-project-discovery` – one connection per collection, project discovery wizard | implemented |
| `add-code-and-test-analysis` – PR, branch, pipeline and test data, link tracing, KPI analysis | implemented |

Specs: `openspec/changes/*/specs/**/spec.md`.

## Tools

| Group | Tools |
|---|---|
| Work Items lesen | get_work_item, query_by_id, execute_wiql, query_work_items, get_work_items_with_relations, get_linked_work_items, get_work_item_revisions, trace_links |
| Work Items schreiben | create_work_item, update_work_item, add_work_item_comment, update_work_item_comment, add_work_item_relation, add_work_item_attachment |
| Git | get_repositories, get_commits, get_branches, get_pull_requests, get_pull_request, analyze_pull_requests |
| Pipelines | get_pipelines, get_builds, get_releases |
| Test | get_test_plans, get_test_runs, get_test_results, analyze_test_results |
| Planung | get_iterations, get_iteration_capacity |
| System | list_projects, ping |

Every tool takes an optional `project` = the **alias** of a configured project; without it the default
project is used. Only configured projects are reachable – an unknown alias is an error.

### Auswertungen

Die Git-, Pipelines- und Test-Tools liefern Daten für Auswertungen, keine Git-Operationen: feste, kompakte
Felder (Personen als `{ name, login }`, keine Avatar- oder Identity-URLs), vollständiges Paging bis `top`
und `processed`/`truncated` im Ergebnis.

| Frage | Aufruf |
|---|---|
| Welche Branches in `app` sind seit 30 Tagen tot und ohne PR? | `get_branches` repository=app, staleDays=30, withoutPullRequest=true |
| Alle Review-Kommentare eines PRs | `get_pull_request` id=… (Reviewer mit Stimme und `votedAt`, Threads mit Datei/Zeile, Pushes, Dateien, Work Items) |
| Durchlaufzeit und Kommentare pro Entwickler, ohne Test-PRs | `analyze_pull_requests` groupBy=author, metrics=count,cycleTime,commentsPerPr, exclude={titlePattern:"^\\[TEST\\]"} |
| Reviewer: Reaktionszeit, Zustimmungsquote | `analyze_pull_requests` groupBy=reviewer, metrics=firstResponse,approvalRate,rejectionRate |
| Aktueller Stand eines Testplans | `get_test_results` planId=…, latest=testCase+configuration |
| Ergebnisse pro Build einer Release-Definition auf master | `analyze_test_results` releaseDefinitionId=…, branch=master, groupBy=build, metrics=total,passed,failed,passRate,newFailures |
| Was hängt an Story 4711 (PRs, Tests, Bugs)? | `trace_links` workItems=[4711]; mit refPattern=`REQ-\d+` auch IDs im Text |

`analyze_*` gruppieren Personen über das Login, zählen nur Text-Kommentare (keine Systemeinträge) und
werten bei Tests je Gruppe nur das neueste Ergebnis je Testfall und Konfiguration (`latest`); failed =
Failed + Aborted. Die Bewertung von Kommentarinhalten macht der KI-Client selbst.

## Verbindungen und Projekte

Eine **Verbindung** ist eine Collection mit genau einem PAT; darunter liegen die eingerichteten **Projekte**.
Der Einrichtungs-Wizard im Manager („+ Neue Verbindung“ bzw. „Projekte erkennen“) prüft den PAT, liest alle
sichtbaren Projekte, ermittelt pro Projekt das **effektive Recht** (PAT-Scope kombiniert mit deinen
Projektrechten in Azure DevOps) und geht dann jedes ausgewählte Projekt einzeln durch: Kurzname,
Standardprojekt, Schreibschalter (Voreinstellung aus) und Modus je Tool-Gruppe. „Neu einlesen“ zeigt später
neue, nicht mehr sichtbare und geänderte Projekte; nichts wird automatisch gelöscht, mehr Rechte schalten
Schreiben nie automatisch frei.

Ohne Oberfläche:

```powershell
azdo-mcp.exe discover <verbindung>           # zeigt Projekte und Rechte, ändert nichts
azdo-mcp.exe discover <verbindung> --apply   # übernimmt neue Projekte mit Voreinstellung
```

Die Erkennung sendet keine Schreibanfrage an Azure DevOps außer einer `validateOnly`-Prüfung, die nichts anlegt.

### Kurznamen

Jedes Projekt hat einen **Kurznamen**, über den du es im Prompt ansprichst – ohne zu sagen, dass es um
Azure DevOps geht:

> Zeig die offenen Bugs in SHOP · gib mir aus SHOP das Work Item 4711

Regeln: 2–10 Zeichen aus Buchstaben, Ziffern und `-`, beginnt mit einem Buchstaben, eindeutig über alle
Verbindungen (Groß-/Kleinschreibung egal, gespeichert in Großbuchstaben); `PR`, `BUG`, `ID`, `TEST`, `TASK`,
`ALL` sind gesperrt. Der Wizard schlägt einen vor („Demo Project One“ → `DPO`), unter „Verbindungen & PATs“
lässt er sich ändern. Der Server nennt der KI beim Verbinden alle Kurznamen und listet sie im Parameter
`project` jedes Tools; nach einer Umbenennung lädt der Client die Tool-Liste neu.

Work-Item-IDs gelten für die ganze Collection: Ein Tool liest oder ändert nur Work Items des angegebenen
Projekts. `project="SHOP"` mit der ID eines OPS-Work-Items wird vor jeder Freigabe abgelehnt
(„Work Item 5000 gehört nicht zu SHOP – es liegt in OPS“).

### Clients einbinden

Die Seite *MCP-Clients* trägt den Server mit einem Klick ein (Claude Code, Claude Desktop, OpenCode,
VS Code/Copilot). Alternativ zeigt jede Karte einen **Prompt zum Einrichten**: kopieren, in den jeweiligen
Assistenten einfügen, der Assistent richtet den Server selbst ein (bei Claude Desktop über einen Assistenten
mit Dateizugriff, z. B. Claude Code). Die Prompts enthalten keinen PAT und keine Server-URL. Danach den Client
neu starten und mit der Testfrage prüfen („Welche Azure-DevOps-Projekte kennst du?“). Findet der Manager in
OpenCode einen ungültigen `servers`-Block unter `mcp`, zeigt die Karte „prüfen“.

### Lesen und Schreiben

Jeder Bereich (Work Items, Code, Pipelines, Test, Iterationen) hat zwei Werte, **Lesen** und **Schreiben**:

| Wert | Bedeutung |
|---|---|
| ja | geprüft, erlaubt |
| nein | geprüft, verweigert – sperrt die Tools dieses Bereichs bzw. die Schreib-Tools |
| n/a | ohne echtes Schreiben nicht prüfbar (z. B. Code/Pipelines/Test/Iterationen Schreiben beim PAT) – sperrt nicht |
| ? | noch nicht geprüft („Neu einlesen“) – sperrt nicht |

Effektiv gilt je Spalte: „nein“ vor „?“ vor „n/a“ vor „ja“. Nur ein geprüftes „nein“ schränkt ein; bei „n/a“
entscheidet Azure DevOps beim Schreiben selbst. Der Status eines Projekts nennt bei „nur Lesen“ den Grund
(PAT, Projektrecht, Schreibschalter aus, Schreib-Tools gesperrt).

Nach dem Update auf 2.2 einmal „Neu einlesen“ je Verbindung: Gespeicherte Rechte aus 2.1 werden vorsichtig
umgerechnet (Work Items „nur Lesen“ → Schreiben „nein“), erst die neue Erkennung liefert die geprüften Werte.

Ab 2.4 gibt es den Bereich **Pipelines** (Builds und Releases lesen; PAT-Scope „Build (Read)“, für
Releases zusätzlich „Release (Read)“). Bestehende Projekte zeigen dort „?“ bis zum nächsten „Neu einlesen“.

## Data

`%LOCALAPPDATA%\AzdoMcpData\`: `connections.json` (schema 3, no secrets), `permissions.json`,
`secrets\<connection>.bin` (one PAT per connection, DPAPI, current Windows user only),
`logs\audit-YYYYMMDD.jsonl` (30 days, metadata only). Older files are migrated automatically:
schema 1 from 2.0 (backup `connections.v1.bak.json`) and schema 2 from 2.1 (backup `connections.v2.bak.json`;
to downgrade to 2.1, copy it back to `connections.json`).

## Installation (ohne Admin-Rechte)

**Installer** – `AzdoMcp-win-Setup.exe` doppelklicken. Kein UAC-Dialog; installiert nur für den
angemeldeten Benutzer nach `%LOCALAPPDATA%\AzdoMcp\`, legt „AzDO MCP Manager“ im Startmenü an,
aktiviert „Autostart bei Anmeldung“ und startet den Manager. Verteilung durch die IT: `AzdoMcp-win-Setup.exe --silent`
installiert ohne Fenster, startet den Manager dann aber erst bei der nächsten Anmeldung (Autostart).
Der Server liegt danach immer unter `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` – auch nach Updates,
MCP-Clients müssen also nie umkonfiguriert werden. Deinstallieren über *Einstellungen › Apps*;
Verbindungen, PATs und Protokoll in `%LOCALAPPDATA%\AzdoMcpData\` bleiben erhalten.

**Portable** – `AzdoMcp-<Version>-portable.zip` in einen beliebigen eigenen Ordner entpacken und
`AzdoMcp.Manager.exe` starten. Keine Updates; die Seite *MCP-Clients* trägt den tatsächlichen Pfad ein
(nach dem Verschieben des Ordners erneut eintragen).

**SmartScreen** – die Dateien sind (noch) nicht signiert. Erscheint „Der Computer wurde durch Windows
geschützt“: *Weitere Informationen › Trotzdem ausführen*.

**AppLocker / WDAC** – manche Firmenrichtlinien blockieren Programme unter `%LOCALAPPDATA%`. Dann die
Portable-Variante in einen erlaubten Ordner entpacken oder die IT um eine Freigabe für
`%LOCALAPPDATA%\AzdoMcp\` bitten.

**Updates** – kein Update-Server ist eingebaut. Zum Aktivieren `%LOCALAPPDATA%\AzdoMcpData\update.json` anlegen:

```json
{ "source": "https://<server>/<velopack-releases>/" }
```

`source` darf auch ein Netzwerk-/Ordnerpfad oder `github:<owner>/<repo>` sein. Der Manager prüft beim Start,
zeigt „Update verfügbar“ im Footer und installiert erst nach Bestätigung; laufende MCP-Server werden dafür
beendet und vom Client beim nächsten Aufruf neu gestartet.

## Build

```powershell
.\build\build.ps1 -Version 2.0.0
```

Restore, Build, Tests, Publish (self-contained win-x64), Portable-ZIP und Velopack-Pack. Ergebnis:
`artifacts\releases\AzdoMcp-win-Setup.exe` und `artifacts\AzdoMcp-2.0.0-portable.zip`. Bricht mit Exit-Code 1 ab,
sobald ein Schritt – auch ein Test – fehlschlägt. Entwicklung:

```powershell
dotnet build AzdoMcp.sln
dotnet run --project tests/AzdoMcp.Tests
```
