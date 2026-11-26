# Update und Deinstallation

## Update

AzDO MCP Manager enthält keine fest eingebaute Update-Quelle. Ob und woher Updates kommen, legen Sie (oder Ihre IT) mit einer kleinen Datei im Datenordner fest. Updates gibt es nur für die installierte Variante (`AzdoMcp-win-Setup.exe`), nicht für die Portable-Variante.

### Update-Quelle einrichten

Legen Sie die Datei `%LOCALAPPDATA%\AzdoMcpData\update.json` an:

```json
{ "source": "https://<server>/<velopack-releases>/" }
```

`source` kann sein:

| Art | Beispiel |
|---|---|
| Web-Adresse eines Release-Ordners | `https://azdo.example.local/releases/azdo-mcp/` |
| Netzwerk- oder lokaler Ordner | `\\fileserver\software\AzdoMcp` oder `C:\Updates\AzdoMcp` |
| GitHub-Releases eines Repositorys | `github:trosinde/AzDO-mcp` |

Der Ordner bzw. die Adresse muss die Release-Dateien enthalten, die zu jedem Release gehören (u. a. `RELEASES` und die `.nupkg`-Pakete). Bei `github:` muss das Repository öffentlich erreichbar sein.

Optional schaltet `"checkOnStart": false` die automatische Prüfung beim Start ab:

```json
{ "source": "github:trosinde/AzDO-mcp", "checkOnStart": false }
```

Nach dem Anlegen oder Ändern von `update.json` den Manager beenden (Tray-Menü «Beenden») und neu starten.

### Update-Status im Footer

Links unten im Manager steht die Installationsart und darunter die Version mit dem Update-Status, z. B. «v2.5.0 · Updates: aktuell».

| Footer | Bedeutung |
|---|---|
| «Updates: aktuell» | Die Update-Quelle wurde geprüft, es gibt keine neuere Version. |
| «Update verfügbar» | Eine neuere Version liegt bereit; die Version steht in Klammern, z. B. «Update verfügbar (2.6.0)». Der Text ist ein Link («Jetzt aktualisieren»). |
| «Updates: nicht konfiguriert» | Installierte Variante, aber keine `update.json` bzw. kein `source` darin. |
| «Updates: prüfe …» | Die Prüfung läuft gerade. |
| «Update wird installiert …» | Das Update wird heruntergeladen und installiert. |
| «Updates: Fehler» | Prüfung oder Installation ist fehlgeschlagen, z. B. Quelle nicht erreichbar ([Meldungen und Fehlerbehebung](09-meldungen-und-fehlerbehebung.md)). |
| «Updates: aus» | Portable-Variante oder Entwickler-Build: Updates sind nicht möglich. |

Die Zeile darüber zeigt die Installationsart: «Benutzer-Installation · ohne Admin» bei der installierten Variante, «Portable · ohne Admin» bei der Portable-Variante.

### Update installieren

Der Manager prüft die Quelle beim Start. Installiert wird nur nach Ihrer Bestätigung:

1. Im Footer auf «Update verfügbar» klicken.
2. Die Rückfrage «Version 2.6.0 installieren?» lesen: «Laufende MCP-Server werden beendet; die Clients starten sie beim nächsten Aufruf neu. Der Manager startet danach neu.» Mit «Ja» bestätigen.
3. Der Manager bittet alle laufenden MCP-Server, sich zu beenden, lädt das Update, tauscht die Programmdateien aus und startet neu (minimiert im Tray).

Laufen nach 5 Sekunden noch Server, bricht das Update ab: «Update fehlgeschlagen» mit «… MCP-Server laufen noch – bitte die Clients schliessen und erneut versuchen.» Dann alle MCP-Clients schliessen und erneut klicken.

Was beim Update gleich bleibt:

- Der Pfad des Servers `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe`. Die MCP-Clients müssen nicht neu eingerichtet werden.
- Alle Benutzerdaten in `%LOCALAPPDATA%\AzdoMcpData\`: Verbindungen, PATs, Rechte, Protokoll, `update.json`.
- «Autostart bei Anmeldung».

**Ohne Update-Quelle** aktualisieren Sie, indem Sie die neue `AzdoMcp-win-Setup.exe` ausführen. Sie installiert über die bestehende Version; die Daten bleiben erhalten. Vorher die MCP-Clients schliessen.

**Portable-Variante:** Manager und Clients beenden, den Inhalt der neuen `AzdoMcp-<Version>-portable.zip` in denselben Ordner entpacken und die alten Dateien ersetzen. Bleibt der Ordner gleich, bleiben auch die Einträge in den Clients gültig.

### Nach dem Update: Migration älterer Versionen

Der Manager und der Server übernehmen Daten älterer Versionen beim ersten Lesen automatisch und nur einmal. Es geht keine Einstellung verloren.

| Von Version | Was geschieht | Sicherung |
|---|---|---|
| 1.x (`config.json` mit PATs im Klartext) | Die Übersicht meldet z. B. «1 PAT im Klartext gefunden». Nach «Importieren» sind die Einträge pro Collection zu Verbindungen zusammengefasst, die PATs verschlüsselt und die alte Datei gelöscht. | – |
| 2.0 | Jede alte Verbindung wird zu einer Verbindung mit einem Projekt (Kurzname, Schreibschalter und Standard wie bisher). Der PAT bleibt gültig. | `connections.v1.bak.json` |
| 2.1 | Gespeicherte Rechte werden vorsichtig auf Lesen/Schreiben umgerechnet; bei Work Items wird «nur Lesen» zu Schreiben «nein», damit vor der nächsten Erkennung nichts schreibbar wird. | `connections.v2.bak.json` |

Nach dem Update von 2.1 oder älter: Für jede Verbindung einmal «Neu einlesen» ausführen. Erst die neue Erkennung liefert die geprüften Rechte. Seit Version 2.4 gibt es den Bereich «Pipelines»; bestehende Projekte zeigen dort «?», bis Sie «Neu einlesen» ausführen.

**Zurück auf 2.1:** Die ältere Version installieren und `connections.v2.bak.json` nach `connections.json` kopieren (im Datenordner). Änderungen nach dem Update gehen dabei verloren.

## Deinstallation

### Installierte Variante

1. MCP-Clients schliessen.
2. «Einstellungen» → «Apps» → «Installierte Apps» → «AzDO MCP Manager» → «Deinstallieren».

Die Deinstallation braucht keine Admin-Rechte. Sie entfernt den Programmordner `%LOCALAPPDATA%\AzdoMcp\` mit Manager und Server, den Startmenü-Eintrag und «Autostart bei Anmeldung».

**Erhalten bleibt** der Datenordner `%LOCALAPPDATA%\AzdoMcpData\` mit Verbindungen, verschlüsselten PATs, Rechten, Protokoll und `update.json`. Eine spätere Neuinstallation findet alles wieder vor.

**Ebenfalls erhalten** bleiben die Einträge `azure-devops` in den Konfigurationsdateien der MCP-Clients. Sie zeigen nach der Deinstallation auf eine nicht mehr vorhandene Datei; die Clients melden dann, dass der Server nicht startet. Entfernen Sie die Einträge im Client, z. B. bei Claude Code mit `claude mcp remove --scope user azure-devops`, bei den anderen Clients in der jeweiligen Datei ([MCP-Clients](05-mcp-clients.md)). Die Sicherungen `*.azdo-mcp-backup-*` neben diesen Dateien können Sie löschen.

### Portable-Variante

1. Im Tray-Menü «Autostart bei Anmeldung» ausschalten, falls eingeschaltet.
2. Manager beenden (Tray-Menü «Beenden») und MCP-Clients schliessen.
3. Den Ordner der Portable-Variante löschen.
4. Einträge in den MCP-Clients entfernen (wie oben).

### Alle Daten entfernen

Sollen auch Verbindungen, PATs und Protokoll weg, löschen Sie nach der Deinstallation den Ordner:

```text
%LOCALAPPDATA%\AzdoMcpData\
```

Damit sind die verschlüsselten PATs entfernt. Widerrufen Sie die PATs zusätzlich in Azure DevOps («User settings» → «Personal access tokens»), wenn sie nicht mehr gebraucht werden.
