# AzDO-mcp – Release, Handbuch und README angleichen

Stand Konzept: 2026-11-19 · Thorsten Rosin

## Ziel und Ist/Soll

AzDO-mcp bekommt dieselbe Release-Kette wie app-launcher (geprüfter Tag, Signierung, Signaturprüfung, Handbuch-PDF, SHA256SUMS, Pflicht-Assets) und eine README nach dem Annota-Muster. Produktspezifisches bleibt: per-user-Installer (Velopack Setup.exe) und Portable-ZIP, kein MSI, keine Admin-Rechte.

Stand: Repo-Commit `ade31bd`, letztes Release `v2.5.0-rc.1` (Prerelease, unsigniert).

| Thema | AzDO-mcp heute | Vorbild | Soll |
| --- | --- | --- | --- |
| SDK-Version | `dotnet-version: 10.0.x` | app-launcher: `global.json` | `global.json`, CI und Release lesen daraus |
| CI | Build, Test, OpenSpec | app-launcher | dazu: Handbuch-HTML bauen, Artefaktname mit `runner.os` |
| Release-Skript | `build/build.ps1` | `build/Build-Release.ps1` | umbenennen, gleiche Parameter (`-OutputDir`, `-PfxPath`, `-CertificateThumbprint`, `-SkipTests`) |
| Tag-Prüfung | keine | `Check version`-Schritt | übernehmen |
| Signierung | keine | PFX aus Secrets, Pflicht für reguläre Releases | übernehmen (siehe offene Entscheidungen) |
| Signaturprüfung | keine | jede EXE im Paket und Setup geprüft | übernehmen |
| Handbuch | keins | `docs/manual/` → PDF, Release-Asset und im Paket | neu: `AzdoMcp-Handbuch-<Version>.pdf` |
| Prüfsummen | keine | `SHA256SUMS.txt` | übernehmen |
| Release-Assets | alles aus `artifacts/releases` + doppelte Portable-ZIP | Filter + Pflicht-Assets | Filter, Pflichtprüfung, `AzdoMcp-win-Portable.zip` von Velopack entfernen |
| README | technische Referenz | Annota: Nutzen, Bilder, Get started, Learn more | neu schreiben, Technik nach `docs/` |
| Repo-Dateien | nur `dependabot.yml` | CONTRIBUTING, SECURITY, PR-Template, LICENSE | übernehmen |
| Absicherung `main` | Repo privat, Free-Plan: keine Rulesets möglich | nur Maintainer pusht/merged, Pflicht-Checks | siehe Arbeitspaket A |

## A – CI, Repo-Dateien und Absicherung

CI und Repo-Dateien folgen app-launcher; die Regeln für `main` liegen in den GitHub-Einstellungen und setzt Thorsten selbst.

**A1 `global.json` (neu)** – wie app-launcher: `sdk.version` `10.0.100`, `rollForward: latestFeature`. Den Block `test.runner` erst setzen, wenn die Tests mit `dotnet test` laufen (A2).

**A2 `.github/workflows/ci.yml`**

- `setup-dotnet` mit `global-json-file: global.json` statt `dotnet-version`.
- Cache-Key zusätzlich über `**/global.json`.
- Testaufruf bleibt `dotnet run --project tests/AzdoMcp.Tests` (xUnit v3 als EXE, so in `CLAUDE.md`). Umstellung auf `dotnet test --solution` nur, wenn sie ohne neues Paket geht; sonst so lassen.
- Artefaktname `test-results-${{ runner.os }}`.
- Neuer Schritt nach dem Test: `./build/Build-Manual.ps1 -SkipPdf -OutputDir artifacts/manual` (Handbuch muss bauen), HTML als Artefakt `manual-html` hochladen.
- Job `openspec` bleibt.

**A3 Repo-Dateien (neu, aus app-launcher übernehmen, Namen und Befehle anpassen)**

| Datei | Inhalt |
| --- | --- |
| `CONTRIBUTING.md` | ein Maintainer (@trosinde), nur er pusht/merged auf `main`; Fork + Branch + PR; Tests lokal `dotnet run --project tests/AzdoMcp.Tests -c Release`; Review erst bei grünen Pflicht-Checks; Branch vor Merge aktuell; Squash-Merge; Schwachstellen → SECURITY.md |
| `SECURITY.md` | Meldung nur über GitHub Security Advisories, kein öffentliches Issue |
| `.github/pull_request_template.md` | «Was ändert sich?» + Checkliste: Tests, lokaler Testlauf, `openspec validate --all --strict`, CI grün |
| `LICENSE` | nur nach Entscheid (siehe offene Entscheidungen) |

**A4 Absicherung gegen Änderungen Dritter – was die Vorbilder tun**

In beiden Vorbildern steht dasselbe Verhalten in CONTRIBUTING; durchgesetzt wird es über die Repo-Einstellungen. Die Einstellungen selbst konnte ich aus dieser Sitzung nicht lesen (keine API-Rechte auf die öffentlichen Repos); die Liste ist aus CONTRIBUTING und den Workflows abgeleitet.

| Verhalten | Umsetzung in GitHub | Wer |
| --- | --- | --- |
| Nur der Maintainer pusht und merged auf `main` | Ruleset für `main`: Pull Request Pflicht, kein Force-Push, kein Löschen; Bypass nur Repo-Admin | Thorsten |
| Review erst bei grüner CI | Ruleset: Pflicht-Checks `Build and test (Windows)` und `OpenSpec validate` | Thorsten |
| Branch vor Merge aktuell | Ruleset: «Require branches to be up to date» | Thorsten |
| Squash-Merge | Settings › General: nur «Allow squash merging» | Thorsten |
| Erste Workflow-Läufe neuer Konten freigeben | Settings › Actions: «Require approval for first-time contributors» | Thorsten |
| Workflows mit minimalen Rechten | `permissions: contents: read` global, `contents: write` nur im Release-Job; kein `pull_request_target` | Claude Code (schon so, beibehalten) |
| Secrets nur für Releases | Signier-Secrets nur im Release-Workflow, der nur auf Tags läuft | Claude Code |
| Actions aktuell | `dependabot.yml` wöchentlich (schon vorhanden) | – |

Wichtig: AzDO-mcp ist privat im Free-Plan. GitHub lehnt Rulesets dort ab («Upgrade to GitHub Pro or make this repository public»). Solange das Repo privat bleibt, können ohnehin nur eingeladene Mitwirkende Änderungen pushen. Die Regeln oben werden erst nötig, wenn das Repo öffentlich wird.

## B – Release-Workflow und Build-Release.ps1

Das Release läuft wie bei app-launcher: Tag prüfen, signieren, Signaturen prüfen, Handbuch beilegen, Prüfsummen schreiben, nur gefilterte Pflicht-Assets veröffentlichen.

**B1 `build/build.ps1` → `build/Build-Release.ps1`** (umbenennen, `git mv`)

- Parameter wie app-launcher: `-Version` (Pflicht, gleiches Muster), `-PfxPath`, `-PfxPassword` (SecureString), `-CertificateThumbprint`, `-TimestampUrl` (Standard `http://timestamp.digicert.com`), `-OutputDir` (Standard `artifacts/release`), `-SkipTests`. `-PfxPath` und `-CertificateThumbprint` schliessen sich aus.
- `Find-Dotnet` bleibt (Benutzer-SDK unter `%LOCALAPPDATA%`), Ausgaben auf Englisch wie im Vorbild.
- Ablauf:
    1. Restore, Build, Test (ausser `-SkipTests`).
    2. Publish `azdo-mcp.exe` und `AzdoMcp.Manager.exe` self-contained win-x64 in einen Pack-Ordner (wie heute).
    3. Handbuch: `Build-Manual.ps1 -Version <v>` aufrufen, PDF als `AzdoMcp-Handbuch.pdf` in den Pack-Ordner legen (Arbeitspaket C).
    4. Signier-Setup: PFX nach `Cert:\CurrentUser\My` importieren, per Thumbprint signieren, Passwort nie auf einer Kommandozeile, Zertifikat im `finally` wieder entfernen.
    5. Beide EXE im Pack-Ordner mit `signtool` signieren (sonst wäre die Portable-ZIP unsigniert: vpk signiert nur seine eigenen Kopien).
    6. Portable-ZIP `AzdoMcp-<v>-portable.zip` aus dem Pack-Ordner.
    7. `vpk pack` wie heute (per-user, `--shortcuts StartMenuRoot`) plus `--signParams "/sha1 <Thumbprint> /s My /fd sha256 /tr <url> /td sha256"`, Ausgabe in `-OutputDir`.
    8. Von Velopack erzeugtes `AzdoMcp-win-Portable.zip` löschen (Duplikat mit Update.exe; unsere Portable-Variante soll ohne Updates laufen, siehe Spec `packaging`).
    9. Signaturprüfung (nur mit Zertifikat): `AzdoMcp-win-Setup.exe`, jede EXE in `*-full.nupkg` unter `lib/app` und beide EXE in der Portable-ZIP müssen `Valid` sein; eigene EXE (`azdo-mcp.exe`, `AzdoMcp.*.exe`) müssen den Release-Thumbprint tragen; fehlt eine der beiden EXE im Paket → Abbruch.
    10. Handbuch zusätzlich als `AzdoMcp-Handbuch-<v>.pdf` nach `-OutputDir`.
    11. `SHA256SUMS.txt` über alle Dateien in `-OutputDir` (Format wie app-launcher).
- Ohne Zertifikat: Warnung «release is NOT signed», Build läuft weiter.
- Alle Verweise auf `build.ps1` anpassen (README, `docs/`, Spec `packaging`, `CLAUDE.md`).

**B2 `.github/workflows/release.yml`** – Struktur 1:1 von app-launcher:

- `concurrency.group: release-${{ github.event.inputs.tag || github.ref_name }}`.
- `setup-dotnet` mit `global-json-file`.
- Schritt *Check version* (Tag-Regex).
- Schritt *Prepare signing certificate* aus `SIGNING_PFX_BASE64`; fehlt das Secret, schlägt ein reguläres Release fehl, ein Prerelease (`-` im Tag) läuft unsigniert mit Warnung.
- Schritt *Trust self-signed test certificate (runner only)* unverändert.
- *Build, test, sign and package* ruft `./build/Build-Release.ps1` mit `-OutputDir artifacts/release`, Passwort aus `SIGNING_PFX_PASSWORD`.
- *Remove signing certificate* mit `if: always()`.
- *Create GitHub release*: Asset-Filter und Pflichtprüfung, Titel `AzDO MCP Manager $TAG`, `--generate-notes --verify-tag`, Prerelease bei `-`.

| Asset | Pflicht | Zweck |
| --- | --- | --- |
| `AzdoMcp-win-Setup.exe` | ja | Installer für Benutzer |
| `AzdoMcp-<v>-portable.zip` | ja | ohne Installation |
| `AzdoMcp-Handbuch-<v>.pdf` | ja | Handbuch ohne Installation lesbar |
| `SHA256SUMS.txt` | ja | Prüfsummen |
| `AzdoMcp-<v>-full.nupkg`, `*-delta.nupkg`, `RELEASES`, `releases.win.json`, `assets.win.json` | ja (ausser delta) | Velopack-Updates (`github:`-Quelle im Manager) |

Filter-Regex: `\.(nupkg|json)$|^AzdoMcp-win-Setup\.exe$|^AzdoMcp-.+-portable\.zip$|^AzdoMcp-Handbuch-.+\.pdf$|^RELEASES$|^SHA256SUMS\.txt$`.

Hinweis: Die Update-Quelle `github:trosinde/AzDO-mcp` funktioniert ohne Token nur, wenn das Repo öffentlich ist. Das betrifft Updates, nicht den Release-Ablauf.

## C – Benutzerhandbuch als Release-Asset

Das Handbuch entsteht wie bei app-launcher aus Markdown-Kapiteln, wird zu einem PDF gebaut, liegt jedem Release bei und ist durch einen Test an die Software gebunden.

**C1 Build-Werkzeug (aus app-launcher kopieren und anpassen)**

- `build/ManualBuilder/` (Konsolen-Tool net10.0, nicht Teil des Produkts, nicht in der Solution-Auslieferung). Namespace `AzdoMcp.ManualBuilder`, Texte unverändert deutsch.
- `build/Build-Manual.ps1`: Name `AzdoMcp-Handbuch`, Version aus `Directory.Build.props`, PDF über headless Edge, `-SkipPdf` für CI/Linux.
- Markdig zentral in `Directory.Packages.props` pinnen (Version wie app-launcher, 1.6.0). Die Regel «keine neuen Pakete» in `CLAUDE.md` gilt für Core, Server und Manager.Core; ManualBuilder und Tests sind nicht betroffen.

**C2 Inhalt `docs/manual/`** – Zielgruppe: Benutzer und IT, nicht Entwickler. Deutsch, Sie-Form, Schweizer Rechtschreibung (ss), Platzhalter laut `CLAUDE.md` (`https://azdo.example.local/DefaultCollection`, «Max Muster»).

| Datei | Kapitel | Quelle im Repo |
| --- | --- | --- |
| `index.md` | Titelseite, Inhalt, Schreibweise | – |
| `01-ueberblick.md` | Zweck, Funktionsweise in einem Bild, Bestandteile | README, Spec `mcp-server` |
| `02-voraussetzungen.md` | Windows, PAT-Scopes je Bereich, unterstützte Clients | README, Spec `connections` |
| `03-installation.md` | Setup, `--silent`, Portable, SmartScreen, AppLocker/WDAC | README, Spec `packaging` |
| `04-verbindungen-und-projekte.md` | Wizard, Projekte erkennen, Kurznamen, Neu einlesen | Specs `setup-wizard`, `connections` |
| `05-mcp-clients.md` | Ein-Klick-Einrichtung, Einrichtungs-Prompt, Testfrage | Spec `client-setup` |
| `06-berechtigungen-und-freigaben.md` | Lesen/Schreiben, Erlaubt/Nachfragen/Gesperrt, Freigabefenster | Spec `tool-permissions` |
| `07-tools.md` | alle Tools je Gruppe, Parameter `project`, Auswertungen mit Beispielfragen | Spec `azdo-tools`, README |
| `08-protokoll-und-daten.md` | Protokoll, Datenordner, was gespeichert wird und was nicht | Spec `connections`, `CLAUDE.md` |
| `09-meldungen-und-fehlerbehebung.md` | jede Meldung von Manager und Server mit Ursache und Behebung | Views, `SecretScrubber`, Specs |
| `10-update-und-deinstallation.md` | `update.json`, Footer-Status, Migration 2.0/2.1, Deinstallation | README, Spec `packaging` |
| `11-referenz.md` | Kommandozeile (`--stdio`, `discover`, `--apply`), Pfade, Dateien | README |

Bilder: `docs/manual/images/`, zuerst aus den Artboards in `docs/ui-proposal/` gerendert (Playwright), später durch echte Bildschirmfotos mit gleichem Dateinamen ersetzt. Fehlende Bilder erscheinen als Platzhalter und stehen in `docs/manual/images/README.md`.

**C3 Vollständigkeitstest** – in `tests/AzdoMcp.Tests` (Klasse `ManualCompletenessTests`, Markdig dort referenzieren), analog app-launcher:

- jedes registrierte Tool (Name aus der Tool-Registrierung, nicht aus einer Liste im Test) steht in `07-tools.md`;
- jede Tool-Gruppe und jeder Modus (Erlaubt / Nachfragen / Gesperrt) steht im Handbuch;
- die Footer-Texte «Updates: aktuell», «Update verfügbar», «Updates: nicht konfiguriert», «Portable» stehen in Kapitel 10;
- Befehle `--stdio`, `discover`, `--apply` stehen in Kapitel 11;
- Fehlermeldung nennt den fehlenden Text und die Quelle.

Nicht im Umfang: Meldungen aus XAML zentral sammeln. Wenn der Test sie ohne Umbau nicht findet, nur Tools, Modi, Footer und CLI prüfen und die Lücke in `tasks.md` vermerken.

**C4 Handbuch im Paket** – `AzdoMcp-Handbuch.pdf` liegt im Installationsordner und in der Portable-ZIP. Ein Eintrag «Handbuch» im Tray-Menü oder Startmenü ist eine UI-Änderung und nur nach Entscheid (siehe offene Entscheidungen).

## D – README als Einstieg aus Nutzersicht

Die README verkauft den Nutzen in 30 Sekunden und führt zur Installation; alles Technische wandert nach `docs/` und ins Handbuch. Aufbau wie Annota, Sprache Deutsch (UI und Handbuch sind deutsch), Sie-Form.

**D1 Aufbau `README.md`**

| # | Block | Inhalt |
| --- | --- | --- |
| 1 | Kopf (zentriert) | Icon, «AzDO MCP Manager», Claim in einer Zeile (Vorschlag: «Azure DevOps für Ihren KI-Assistenten – Sie bestimmen, was er darf.»), Hero-Bild Übersicht |
| 2 | Problem und Nutzen | 3–4 Sätze: Work Items, PRs, Pipelines und Tests aus Claude, Copilot oder OpenCode abfragen; PATs sicher, keine Admin-Rechte, Windows |
| 3 | Status-Hinweis | eine Zeile: aktuelle Version, Link auf das neueste Release |
| 4 | «Warum AzDO MCP Manager» | 4 Abschnitte, je Überschrift + 2 Sätze + Bild (siehe D2) |
| 5 | Vorher / nachher | optional: Klickstrecke in Azure DevOps vs. eine Frage an den Assistenten (Beispiel-Prompt als Codeblock) |
| 6 | «Loslegen» | Setup herunterladen → Verbindung anlegen → Client mit einem Klick einbinden → Testfrage «Welche Azure-DevOps-Projekte kennst du?»; Portable-Variante in einem Satz |
| 7 | «Mehr erfahren» | Handbuch (PDF im neuesten Release), Tool-Übersicht, Sicherheit und Daten, Entwicklung |
| 8 | Roadmap | erledigte und offene Changes aus `openspec/`, je eine Zeile |
| 9 | Projekt | Datenschutz in einer Zeile (lokal, PAT verschlüsselt, keine Telemetrie, spricht nur mit Ihrem Azure DevOps), Beitragen, Sicherheit, Lizenz |

**D2 Abschnitte «Warum» und Bilder** (`docs/assets/`, aus `docs/ui-proposal/` gerendert, nur Platzhalterdaten)

| Abschnitt | Aussage | Bild (Artboard) |
| --- | --- | --- |
| In einer Minute verbunden | PAT eingeben, der Assistent findet Ihre Projekte und Rechte | `feature-1-erkennung.png` (`Erkennung.dc.html`) |
| Sie bestimmen, was die KI darf | Lesen und Schreiben je Bereich, jedes Tool Erlaubt / Nachfragen / Gesperrt, Freigabe per Toast | `feature-2-rechte.png` (`Tools.dc.html` + Toast aus `Tray.dc.html`) |
| Ein Klick für Ihren Assistenten | Claude Code, Claude Desktop, OpenCode, VS Code/Copilot eintragen ohne JSON-Bearbeitung | `feature-3-clients.png` (`Clients.dc.html`) |
| Fragen statt Klickstrecken | Auswertungen zu PRs, Reviews und Tests per Frage, mit Kurznamen wie «SHOP» | `feature-4-protokoll.png` (`Protokoll.dc.html`) |
| Hero | Übersicht aller Projekte | `hero.png` (`Main.dc.html`) |

Rendern: Skript `tools/Render-ReadmeImages.ps1` oder Node/Playwright-Skript unter `tools/`, Breite 1720 px (Anzeige 860), PNG. Gibt es kein App-Icon, Platz im Kopf leer lassen und als Aufgabe vermerken.

**D3 Was aus der heutigen README wohin geht**

| Heutiger Abschnitt | Neues Ziel |
| --- | --- |
| Status-Tabelle der Changes | Roadmap (gekürzt) |
| Tools, Auswertungen | Handbuch Kapitel 07, Kurzfassung in «Warum» |
| Verbindungen, Kurznamen, Lesen und Schreiben | Handbuch Kapitel 04 und 06 |
| Clients einbinden | Handbuch Kapitel 05 |
| Data | Handbuch Kapitel 08 |
| Installation, SmartScreen, AppLocker, Updates | Handbuch Kapitel 03 und 10; README nur «Loslegen» |
| Build | neu `docs/entwicklung.md` (wie app-launcher: Voraussetzungen, Build, Tests, Release, Workflows-Tabelle, Projektstruktur, OpenSpec-Arbeitsweise) |

Dazu `docs/README.md` als Dokumentübersicht (Tabelle Dokument / Inhalt / Für wen), wie bei app-launcher.

## OpenSpec-Change, Reihenfolge, Abnahme

Alles läuft als ein OpenSpec-Change `add-release-and-manual`, damit Spec und Code zusammenpassen (Regel in `CLAUDE.md`: bei Konflikt mit einer Spec anhalten und fragen).

**Spec-Deltas**

- `packaging`: MODIFIED «Build script» (`Build-Release.ps1`, Ausgabe `artifacts/release`, Handbuch, SHA256SUMS, Signierung optional per Parameter); ADDED «Signed release» (reguläre Releases signiert, Signaturprüfung bricht ab); ADDED «Release assets» (Pflicht-Assets laut Tabelle B2).
- `documentation` (neu): «Benutzerhandbuch im Release», «Vollständigkeit» (Test aus C3), «README als Einstieg».

**Reihenfolge der Commits** (klein, Englisch, je ein Thema)

1. OpenSpec-Change anlegen (`/opsx:propose`), `openspec validate --all --strict` grün.
2. `global.json`, CI-Anpassungen (A1, A2 ohne Handbuch-Schritt).
3. CONTRIBUTING, SECURITY, PR-Template (A3).
4. `Build-Release.ps1` mit Signierung und Prüfsummen (B1).
5. `release.yml` (B2).
6. ManualBuilder, `Build-Manual.ps1`, CI-Schritt Handbuch (C1, A2).
7. Handbuch-Kapitel und Bilder (C2).
8. Vollständigkeitstest (C3), Handbuch im Paket (C4).
9. README, `docs/README.md`, `docs/entwicklung.md`, README-Bilder (D).
10. `tasks.md` abhaken, `CLAUDE.md` (Commands, Layout) aktualisieren.

**Abnahme**

- [ ] CI auf einem PR grün, Artefakte `test-results-Windows` und `manual-html` vorhanden
- [ ] `./build/Build-Release.ps1 -Version 2.5.0-rc.2` lokal ohne Zertifikat: Setup, Portable-ZIP, nupkg, RELEASES, json, Handbuch-PDF, SHA256SUMS in `artifacts/release`, kein `AzdoMcp-win-Portable.zip`
- [ ] Tag `v2.5.0-rc.2` erzeugt ein Prerelease mit genau den Pflicht-Assets
- [ ] Mit Test-Zertifikat: Signaturprüfung grün; manipulierte EXE lässt den Build scheitern
- [ ] Regulärer Tag ohne `SIGNING_PFX_BASE64` schlägt vor dem Veröffentlichen fehl
- [ ] Vollständigkeitstest scheitert, wenn ein Tool im Handbuch fehlt
- [ ] README zeigt Nutzen, Bilder und «Loslegen» ohne technische Referenz; alle Links gehen
- [ ] Keine echten PATs, Server-URLs oder Namen in Code, Bildern, Doku

## Offene Entscheidungen vor Übergabe

Fünf Punkte entscheidet Thorsten; die Voreinstellung gilt, wenn nichts eingetragen wird.

| # | Frage | Voreinstellung | Folge |
| --- | --- | --- | --- |
| 1 | Skill «dotnet deployment»: wo liegt er? | in dieser Sitzung nicht gefunden; Absicherung aus app-launcher/Annota abgeleitet (A4) | Skill nachreichen, dann A4 und B dagegen abgleichen |
| 2 | Signierung Pflicht für reguläre Releases? | ja, wie app-launcher | ohne Zertifikat nur noch Prereleases (`-rc`) |
| 3 | Repo öffentlich machen? | bleibt privat | Rulesets aus A4 nicht möglich (Free-Plan); `github:`-Updates brauchen öffentliches Repo |
| 4 | Lizenz | keine LICENSE, solange privat | bei öffentlich: MIT wie app-launcher und Annota |
| 5 | Eintrag «Handbuch» im Tray-Menü | nein, nur PDF im Programmordner und als Release-Asset | ja = UI-Änderung mit Spec `manager-ui` und deutschem Text |

## Prompt für Claude Code

Dieses Dokument als Markdown exportieren und als `docs/konzept/release-und-handbuch.md` ins Repo legen, dann den Prompt übergeben:

```text
Read docs/konzept/release-und-handbuch.md and CLAUDE.md. Reference repos (read-only, public):
https://github.com/trosinde/app-launcher (CI, release, build/Build-Release.ps1, build/Build-Manual.ps1,
build/ManualBuilder, docs/manual, tests/AppStart.Documentation.Tests, CONTRIBUTING.md, SECURITY.md,
.github/pull_request_template.md) and https://github.com/trosinde/Annota (README.md structure).

1. Run /opsx:propose for change "add-release-and-manual" with the spec deltas from the section
   "OpenSpec-Change". Stop and show me proposal, design and tasks before writing code.
2. After my OK, implement work packages A-D in the commit order given in the document.
   Keep product specifics: per-user Velopack setup, portable zip, no admin rights, no MSI.
3. Do not change GitHub repository settings; list the settings from A4 for me at the end.
4. Decisions in "Offene Entscheidungen" use the default column unless the document says otherwise.
5. Before each commit: dotnet build AzdoMcp.sln, dotnet run --project tests/AzdoMcp.Tests,
   openspec validate --all --strict. Small English commits, push to a branch, open a draft PR.
6. At the end report: what was done, what is open, which acceptance checks you could not run.
```
