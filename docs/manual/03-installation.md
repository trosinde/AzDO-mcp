# Installation

Es gibt zwei Varianten. Beide brauchen keine Admin-Rechte.

| Variante | Datei | Für | Updates |
|---|---|---|---|
| **Setup** | `AzdoMcp-win-Setup.exe` | Normalfall: Installation für den angemeldeten Benutzer, Startmenü, Autostart | ja, wenn eine Update-Quelle eingerichtet ist (siehe [Update und Deinstallation](10-update-und-deinstallation.md)) |
| **Portable** | `AzdoMcp-<Version>-portable.zip` | PCs, auf denen unter `%LOCALAPPDATA%` keine Programme laufen dürfen, oder zum Ausprobieren | nein |

Beide Dateien und das Handbuch als `AzdoMcp-Handbuch-<Version>.pdf` liegen bei jedem Release. Mit `SHA256SUMS.txt` aus demselben Release prüfen Sie, ob eine Datei unverändert ist:

```powershell
Get-FileHash .\AzdoMcp-win-Setup.exe -Algorithm SHA256
```

Der ausgegebene Wert muss mit der Zeile für die Datei in `SHA256SUMS.txt` übereinstimmen.

## Installation mit dem Setup

1. `AzdoMcp-win-Setup.exe` herunterladen und per Doppelklick starten. Es erscheint keine UAC-Abfrage.
2. Das Setup installiert für den angemeldeten Benutzer nach `%LOCALAPPDATA%\AzdoMcp\`, legt im Startmenü den Eintrag «AzDO MCP Manager» an und aktiviert «Autostart bei Anmeldung».
3. Danach startet der Manager und öffnet das Hauptfenster mit der «Übersicht». Zusätzlich erscheint sein Symbol im Infobereich der Taskleiste. Beim Autostart nach der Anmeldung startet er nur im Infobereich, ohne Fenster.
4. Weiter mit dem Einrichten der ersten Verbindung (siehe [Verbindungen und Projekte](04-verbindungen-und-projekte.md)).

Nach der Installation liegen die Dateien so:

| Pfad | Inhalt |
|---|---|
| `%LOCALAPPDATA%\AzdoMcp\current\AzdoMcp.Manager.exe` | Manager |
| `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` | MCP-Server. Dieser Pfad bleibt nach jedem Update gleich; die Einträge in den MCP-Clients müssen nie angepasst werden. |
| `%LOCALAPPDATA%\AzdoMcp\current\AzdoMcp-Handbuch.pdf` | Dieses Handbuch |
| `%LOCALAPPDATA%\AzdoMcpData\` | Ihre Verbindungen, verschlüsselten PATs, Rechte und das Protokoll. Wird beim ersten Start angelegt und bei Updates und Deinstallation nicht verändert. |

Der Autostart steht unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` und startet den Manager bei der Anmeldung im Infobereich. Sie schalten ihn im Tray-Menü mit «Autostart bei Anmeldung» ein oder aus.

Wird der Manager ein zweites Mal gestartet, holt er das bereits laufende Fenster in den Vordergrund; es läuft immer nur eine Instanz.

## Stille Installation (IT)

Für die Verteilung durch die IT installiert

```powershell
AzdoMcp-win-Setup.exe --silent
```

ohne Fenster. Das Setup muss im Kontext des Benutzers laufen, für den installiert wird (nicht als SYSTEM), weil es in dessen Profil installiert. Der Manager startet dann nicht sofort, sondern erst bei der nächsten Anmeldung über den Autostart.

Verbindungen und PATs richtet jeder Benutzer danach selbst ein; ein PAT gehört immer zu einer Person und lässt sich nicht vorab verteilen.

## Portable-Variante

1. `AzdoMcp-<Version>-portable.zip` in einen eigenen Ordner entpacken, den Sie beschreiben dürfen, z.B. `%USERPROFILE%\Tools\AzdoMcp`.
2. `AzdoMcp.Manager.exe` starten.

Unterschiede zum Setup:

- Keine Updates. Der Fussbereich zeigt «Portable · ohne Admin». Für eine neue Version entpacken Sie die neue ZIP-Datei.
- Kein Startmenü-Eintrag. «Autostart bei Anmeldung» im Tray-Menü funktioniert auch hier und startet den Manager aus dem entpackten Ordner.
- Die Seite «MCP-Clients» trägt den tatsächlichen Pfad von `azdo-mcp.exe` im entpackten Ordner ein. Verschieben Sie den Ordner, tragen Sie die Clients dort erneut ein.
- Das Handbuch `AzdoMcp-Handbuch.pdf` liegt im entpackten Ordner.
- Die Benutzerdaten liegen wie beim Setup unter `%LOCALAPPDATA%\AzdoMcpData\`. Setup und Portable-Variante teilen sich also dieselben Verbindungen.

## SmartScreen

Reguläre Versionen sind digital signiert. Vorabversionen (z.B. `2.5.0-rc.2`) können unsigniert sein. Zeigt Windows dann beim Start «Der Computer wurde durch Windows geschützt», wählen Sie «Weitere Informationen» und danach «Trotzdem ausführen». Tun Sie das nur für Dateien aus dem offiziellen Release, deren Prüfsumme mit `SHA256SUMS.txt` übereinstimmt.

## AppLocker und WDAC

Manche Firmenrichtlinien (AppLocker, Windows Defender Application Control) blockieren Programme unter `%LOCALAPPDATA%`. Dann startet weder der Manager noch der Server. Zwei Wege:

- Die IT gibt den Ordner `%LOCALAPPDATA%\AzdoMcp\` frei, für reguläre Versionen am besten über das Signaturzertifikat statt über den Pfad.
- Sie entpacken die Portable-Variante in einen Ordner, in dem Programme erlaubt sind.

Wichtig für die Freigabe: Beide Programme müssen laufen dürfen, `AzdoMcp.Manager.exe` und `azdo-mcp.exe`. Den Server startet der KI-Client; ist nur der Manager freigegeben, meldet der Client, dass der Server nicht startet.
