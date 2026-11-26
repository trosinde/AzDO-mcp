# Voraussetzungen

## Windows

| Punkt | Vorgabe |
|---|---|
| Betriebssystem | Windows 10 oder Windows 11, 64 Bit (x64) |
| .NET | Nicht nötig. Beide Programme bringen ihre Laufzeit mit. |
| Rechte | Ein normales Benutzerkonto genügt. Installation und Betrieb brauchen keine Admin-Rechte und lösen keine UAC-Abfrage aus. |
| Installationsort | Im Profil des angemeldeten Benutzers: Programm unter `%LOCALAPPDATA%\AzdoMcp\`, Daten unter `%LOCALAPPDATA%\AzdoMcpData\`. Die Portable-Variante läuft aus jedem Ordner, den Sie beschreiben dürfen. |
| Firmenrichtlinien | Blockieren AppLocker oder WDAC Programme unter `%LOCALAPPDATA%`, braucht es eine Freigabe der IT oder die Portable-Variante (siehe [Installation](03-installation.md)). |
| Netzwerk | Zugriff per HTTPS auf Ihre Collection. Für Freigaben öffnet der Manager keinen Netzwerk-Port; Server und Manager verständigen sich lokal. |

Jeder Windows-Benutzer hat seine eigenen Verbindungen und PATs. Ein PAT, der unter einem Benutzer gespeichert wurde, lässt sich unter einem anderen Benutzer nicht entschlüsseln.

## Azure DevOps

| Punkt | Vorgabe |
|---|---|
| Variante | Azure DevOps Services (Cloud) oder Azure DevOps Server (on-prem) |
| Version on-prem | Der Server nutzt die REST-API-Version 6.0. Azure DevOps Server braucht daher mindestens Version 2020. |
| Adresse | Die Collection-URL, z.B. `https://azdo.example.local/DefaultCollection`. Bei Azure DevOps Services ist es die Adresse Ihrer Organisation. Die Adresse muss mit `https://` beginnen. |
| Zugang | Ein Personal Access Token (PAT) Ihres eigenen Kontos, z.B. von «Max Muster». Ein PAT gilt für alle Projekte einer Collection. |
| Projektrechte | Der Server kann in einem Projekt nie mehr als Sie selbst. Fehlt Ihnen in Azure DevOps das Recht, Work Items zu bearbeiten, kann auch der Assistent dort nicht schreiben. |

## PAT erstellen und Scopes je Bereich

Einen PAT erstellen Sie in Azure DevOps unter den Benutzereinstellungen bei «Personal access tokens». Der Knopf «PAT erstellen ↗» im Manager öffnet diese Seite für die eingegebene Collection direkt im Browser.

Wählen Sie beim Erstellen «Custom defined» und nur die Scopes, die Sie brauchen. AzDO MCP Manager unterscheidet fünf Bereiche:

| Bereich im Manager | Scope im PAT | Tool-Gruppen | Pflicht? |
|---|---|---|---|
| Work Items | Work Items: Read, zum Schreiben Read & write | «Work Items lesen», «Work Items schreiben» | Empfohlen. Read & write nur, wenn der Assistent Work Items anlegen oder ändern soll. |
| Code | Code: Read | «Git» | Empfohlen |
| Pipelines | Build: Read, für Releases zusätzlich Release: Read | «Pipelines» | Nur für Builds und Releases |
| Test | Test Management: Read | «Test» | Empfohlen |
| Iterationen | Work Items: Read | «Planung» | Mit dem Work-Items-Scope abgedeckt |

Der Einrichtungs-Assistent nennt als Empfehlung: «Work Items (Read & write), Code (Read), Test Management (Read)».

Fehlt ein Scope, ist das kein Fehler: Der Verbindungstest zeigt den Bereich mit «nein», und die Tools dieses Bereichs sind für alle Projekte der Verbindung gesperrt. Sie können den PAT später ersetzen (siehe [Verbindungen und Projekte](04-verbindungen-und-projekte.md)).

Hinweise zum PAT:

- Notieren Sie das Ablaufdatum. Der Manager erinnert Sie vor dem Ablauf («PAT gültig bis», «Erinnerung»).
- Der Manager speichert den PAT verschlüsselt (Windows DPAPI, nur Ihr Windows-Benutzer). Nach dem Speichern lässt er sich nicht mehr anzeigen, nur ersetzen.
- Geben Sie den PAT nie in einen KI-Client, einen Prompt oder eine Client-Konfiguration ein. Das ist nicht nötig; der Server holt ihn selbst aus dem verschlüsselten Speicher.

> **Hinweis:** Schreiben prüft der Manager nur für Work Items, und zwar mit einer Probe, die nichts anlegt. Ob Sie in Code, Pipelines, Test und Iterationen schreiben dürften, lässt sich ohne echtes Schreiben nicht prüfen; der Manager zeigt dort «n/a». Das schränkt nichts ein, weil der Server in diesen Bereichen ohnehin nur liest.

## Unterstützte MCP-Clients

| Client | Einrichtung im Manager | Konfiguration |
|---|---|---|
| Claude Code | Befehl kopieren oder direkt ausführen | über den Befehl `claude mcp add` (Benutzerebene) |
| Claude Desktop | Eintrag mit einem Klick | `%APPDATA%\Claude\claude_desktop_config.json` |
| OpenCode | Eintrag mit einem Klick | `%USERPROFILE%\.config\opencode\opencode.jsonc` (oder `opencode.json`, wenn nur diese Datei existiert) |
| VS Code / GitHub Copilot | Eintrag mit einem Klick, für den Benutzer oder einen Workspace | `%APPDATA%\Code\User\mcp.json` oder `.vscode\mcp.json` im Workspace |

Der Client muss installiert sein. Für Claude Code muss der Befehl `claude` im Suchpfad liegen, damit «Ausführen» funktioniert; sonst kopieren Sie den Befehl. Für VS Code nutzen Sie GitHub Copilot im Agent-Modus. Details in [MCP-Clients](05-mcp-clients.md).

Andere MCP-Clients, die lokale Server über stdio starten, funktionieren in der Regel auch. Tragen Sie dort von Hand den Befehl `%LOCALAPPDATA%\AzdoMcp\current\azdo-mcp.exe` mit dem Argument `--stdio` ein.
