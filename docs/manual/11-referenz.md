# Referenz

## Kommandozeile azdo-mcp.exe

Pfad (installierte Variante): `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe`. Bei der Portable-Variante liegt die Datei im entpackten Ordner.

```text
azdo-mcp.exe --stdio
azdo-mcp.exe discover <verbindung> [--apply]
azdo-mcp.exe --version
```

Allgemeines:

- Alle Befehle laufen ohne Admin-Rechte und nur für den angemeldeten Windows-Benutzer, weil sie dessen verschlüsselte PATs lesen.
- Meldungen und Hilfe schreibt das Programm in die Fehlerausgabe (stderr). Die Standardausgabe gehört beim Betrieb mit `--stdio` dem MCP-Protokoll.
- Ohne Befehl oder mit unbekanntem Befehl zeigt das Programm eine kurze Hilfe («MCP-Server für Azure DevOps», «Aufruf:  azdo-mcp.exe --stdio») und endet mit Exit-Code 2.

Exit-Codes:

| Code | Bedeutung |
|---|---|
| 0 | Erfolgreich (bei `--stdio`: Server regulär beendet) |
| 1 | `discover`: Verbindung unbekannt, Konfiguration ungültig, PAT nicht lesbar oder Azure DevOps nicht erreichbar |
| 2 | Aufruf ohne `--stdio` und ohne bekannten Befehl |
| 3 | Nicht unter Windows gestartet: «PAT-Speicherung benötigt Windows (DPAPI).» |

### `--stdio`

Startet den MCP-Server. Diesen Aufruf trägt der Manager auf der Seite «MCP-Clients» in die Clients ein; der Client startet den Server selbst und spricht über Standardein- und -ausgabe mit ihm. Sie starten ihn nicht von Hand.

Beim Start legt der Server den Datenordner an, löscht Protokolldateien, die älter als 30 Tage sind, und verbindet sich mit dem Manager (für Freigaben). Läuft der Manager nicht, arbeitet der Server trotzdem; Tools im Modus «Nachfragen» werden dann abgelehnt. Mehrere Server gleichzeitig (ein Server pro Client) sind normal.

Beispiel Claude Code (macht die Seite «MCP-Clients» per Klick):

```text
claude mcp add --scope user azure-devops -- "%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe" --stdio
```

Im Befehl, den die Seite kopiert, steht statt `%LOCALAPPDATA%` der ausgeschriebene Pfad.

### `discover`

Erkennt die Projekte einer Verbindung ohne Oberfläche – das Gegenstück zu «Projekte erkennen» bzw. «Neu einlesen» im Manager, z. B. für Skripte der IT. Die Verbindung muss im Manager bereits angelegt sein (Collection-URL und PAT).

```text
azdo-mcp.exe discover <verbindung>
azdo-mcp.exe discover <verbindung> --apply
```

| Teil | Bedeutung |
|---|---|
| `<verbindung>` | Name der Verbindung, wie unter «Verbindungen & PATs» angezeigt, z. B. `azdo-example` |
| ohne `--apply` | Zeigt Projekte und Rechte. **Ändert nichts.** |
| `--apply` | Übernimmt neue Projekte mit Voreinstellung (Lesen erlaubt, Schreiben nachfragen, Schreibschalter aus), aktualisiert die geprüften Rechte der bestehenden Projekte und markiert nicht mehr sichtbare Projekte. Bestehende Einstellungen (Kurznamen, Schreibschalter, Modi) bleiben unverändert; nichts wird gelöscht. |

Die Erkennung sendet an Azure DevOps nur Leseanfragen und eine Prüfung mit `validateOnly`, die kein Work Item anlegt. Der PAT erscheint nie in der Ausgabe.

Beispielausgabe:

```text
Verbindung azdo-example · azdo.example.local · angemeldet als Max Muster
PAT-Scope (Lesen/Schreiben): Work Items ja/ja · Code ja/n/a · Pipelines ja/n/a · Test ja/n/a · Iterationen ja/n/a

Rechte je Bereich als Lesen/Schreiben: ja, nein, n/a = nicht prüfbar, ? = noch nicht geprüft.
Projekt                          Work Items    Code          Pipelines     Test          Iterationen   Status
SHOP                             ja/ja         ja/n/a        ja/n/a        ja/n/a        ja/n/a        eingerichtet · schreibt mit Freigabe
OPS                              ja/nein       ja/n/a        ja/n/a        ja/n/a        ja/n/a        eingerichtet · nur Lesen · kein Projektrecht zum Bearbeiten
Musterlab                        ja/ja         ja/n/a        ja/n/a        ja/n/a        ja/n/a        neu

1 neu, 0 geändert, 0 nicht mehr sichtbar. Nichts geändert – mit --apply übernehmen.
```

Mit `--apply` endet die Ausgabe mit einer Zeile wie: «Übernommen: 1 neue Projekte (Lesen erlaubt, Schreiben nachfragen, Schreibschalter aus), Rechte von 2 Projekten aktualisiert, 0 als nicht mehr sichtbar markiert.» Neue Projekte erhalten einen vorgeschlagenen Kurznamen, den Sie im Manager ändern können.

Meldungen:

| Meldung (stderr) | Ursache |
|---|---|
| «Aufruf: azdo-mcp discover <verbindung> [--apply]» | Name der Verbindung fehlt. |
| «Unbekannte Verbindung „xyz“.» gefolgt von «Verbindungen: …» oder «Keine Verbindung eingerichtet.» | Name falsch geschrieben oder Verbindung nicht angelegt. |
| «PAT für azdo-example nicht lesbar – bitte ersetzen» | PAT im Manager neu eingeben ([Meldungen und Fehlerbehebung](09-meldungen-und-fehlerbehebung.md)). |
| Fehler von Azure DevOps, z. B. «401 PAT abgelaufen oder ungültig.» | Siehe [Meldungen und Fehlerbehebung](09-meldungen-und-fehlerbehebung.md). |

Laufende Server bemerken die mit `--apply` geänderte `connections.json` selbst und melden dem Client die neue Tool-Liste. Im Zweifel im Tray-Menü «Server neu starten» wählen.

### `--version`

Gibt Namen und Version des Servers aus (`azdo-mcp <Version>`), Exit-Code 0.

## Kommandozeile AzdoMcp.Manager.exe

| Aufruf | Wirkung |
|---|---|
| `AzdoMcp.Manager.exe` | Startet den Manager und öffnet das Fenster. Läuft er schon, holt ein zweiter Start nur das Fenster nach vorn. |
| `AzdoMcp.Manager.exe --minimized` | Startet den Manager nur als Tray-Symbol. So startet ihn «Autostart bei Anmeldung» und der Neustart nach einem Update. |

## Pfade

| Pfad | Inhalt |
|---|---|
| `%LOCALAPPDATA%\AzdoMcp\` | Programmordner der installierten Variante; wird bei der Deinstallation gelöscht |
| `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` | MCP-Server; Pfad bleibt bei Updates gleich |
| `%LOCALAPPDATA%\AzdoMcp\current\AzdoMcp.Manager.exe` | AzDO MCP Manager |
| `%LOCALAPPDATA%\AzdoMcpData\` | Benutzerdaten; bleiben bei Update und Deinstallation erhalten |
| `%LOCALAPPDATA%\AzdoMcpData\logs\` | Protokoll, eine Datei pro Tag; im Tray-Menü «Protokollordner öffnen» |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Wert `AzdoMcpManager` | «Autostart bei Anmeldung» (nur wenn eingeschaltet) |
| Startmenü «AzDO MCP Manager» | Verknüpfung der installierten Variante |

## Dateien im Datenordner

| Datei | Inhalt | Von Hand ändern? |
|---|---|---|
| `connections.json` | Verbindungen und Projekte, keine PATs | Nein, über den Manager |
| `permissions.json` | Modi unter «Tools & Rechte» | Nein, über den Manager |
| `secrets\<id>.bin` | PAT je Verbindung, DPAPI-verschlüsselt | Nein |
| `logs\audit-JJJJMMTT.jsonl` | Protokoll (nur Metadaten), 30 Tage | Nein |
| `reminders.json` | Letzte Anzeige der PAT-Erinnerung je Verbindung | Nein |
| `update.json` | Update-Quelle (`source`, optional `checkOnStart`) | Ja, siehe [Update und Deinstallation](10-update-und-deinstallation.md) |
| `connections.v1.bak.json`, `connections.v2.bak.json` | Sicherungen aus einer Migration | Nur zum Zurückgehen auf eine ältere Version |

Einzelheiten: [Protokoll und Daten](08-protokoll-und-daten.md).

## Konfigurationsdateien der MCP-Clients

Der Manager trägt den Server unter dem Namen `azure-devops` ein und legt vorher eine Sicherung `<Datei>.azdo-mcp-backup-<JJJJMMTThhmmss>` an.

| Client | Datei | Abschnitt |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude.json` (Eintrag über `claude mcp add --scope user`) | `mcpServers` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | `mcpServers` |
| OpenCode | `%USERPROFILE%\.config\opencode\opencode.json` bzw. `opencode.jsonc` | `mcp` |
| VS Code / GitHub Copilot | `%APPDATA%\Code\User\mcp.json`, für einen Workspace `.vscode\mcp.json` | `servers` |

Kein Eintrag enthält einen PAT oder eine Server-URL – nur den Pfad zu `azdo-mcp.exe` und `--stdio`.

## Umgebungsvariablen

| Variable | Wirkung |
|---|---|
| `AZDOMCP_DATA` | Anderer Datenordner statt `%LOCALAPPDATA%\AzdoMcpData\`, z. B. für Tests oder Vorführungen. Manager und Server müssen denselben Wert sehen. Im normalen Betrieb nicht setzen. |

## Tool-Gruppen und Modi

| Tool-Gruppe | Bereich |
|---|---|
| Work Items lesen | Work Items, Lesen |
| Work Items schreiben | Work Items, Schreiben |
| Git | Code, Lesen |
| Pipelines | Pipelines, Lesen |
| Test | Test, Lesen |
| Planung | Iterationen, Lesen |
| System | – |

Modi je Gruppe, Tool, Verbindung und Projekt: «Erlaubt» (KI führt sofort aus), «Nachfragen» (Freigabefenster, ohne Antwort nach 2 min abgelehnt), «Gesperrt» (Tool wird nicht angeboten). Die einzelnen Tools stehen im Kapitel [Tools](07-tools.md), die Regeln im Kapitel [Berechtigungen und Freigaben](06-berechtigungen-und-freigaben.md).
