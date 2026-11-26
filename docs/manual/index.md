# AzDO MCP Manager – Benutzerhandbuch

**Azure DevOps für Ihren KI-Assistenten – Sie bestimmen, was er darf.**

Handbuch für Benutzerinnen und Benutzer sowie für die IT. Es beschreibt, wie Sie AzDO MCP Manager installieren, Verbindungen zu Azure DevOps einrichten, Ihren KI-Assistenten (Claude Code, Claude Desktop, OpenCode, VS Code mit GitHub Copilot) anbinden und festlegen, welche Tools der Assistent ohne Rückfrage, nur mit Ihrer Freigabe oder gar nicht ausführen darf. Dazu kommen alle Tools, das Protokoll, die Meldungen und die Kommandozeile.

<!-- Titelseite: Alles oberhalb von «## Inhalt» übernimmt build/ManualBuilder als Titelseite des PDF. Version und Stand ergänzt das Build-Skript. -->

## Inhalt

1. [Überblick](01-ueberblick.md) – Zweck, Funktionsweise, Bestandteile, Begriffe
2. [Voraussetzungen](02-voraussetzungen.md) – Windows, Azure DevOps, PAT-Scopes je Bereich, unterstützte Clients
3. [Installation](03-installation.md) – Setup, stille Installation, Portable-Variante, SmartScreen, AppLocker/WDAC
4. [Verbindungen und Projekte](04-verbindungen-und-projekte.md) – Einrichtungs-Assistent, Projekte erkennen, Kurznamen, Neu einlesen
5. [MCP-Clients](05-mcp-clients.md) – Ein-Klick-Einrichtung, Prompt zum Einrichten, Testfrage
6. [Berechtigungen und Freigaben](06-berechtigungen-und-freigaben.md) – Lesen und Schreiben, Erlaubt / Nachfragen / Gesperrt, Freigabefenster
7. [Tools](07-tools.md) – alle Tools je Gruppe, Parameter `project`, Auswertungen mit Beispielfragen
8. [Protokoll und Daten](08-protokoll-und-daten.md) – Protokoll, Datenordner, was gespeichert wird und was nicht
9. [Meldungen und Fehlerbehebung](09-meldungen-und-fehlerbehebung.md) – Meldungen von Manager und Server mit Ursache und Behebung
10. [Update und Deinstallation](10-update-und-deinstallation.md) – Updates, Status im Fussbereich, Migration, Deinstallation
11. [Referenz](11-referenz.md) – Kommandozeile, Pfade, Dateien

Bilder: Die Bilder zeigen die Oberfläche mit Beispieldaten. Wo noch ein Platzhalter steht, listet [images/README.md](images/README.md) das fehlende Bild.

Schreibweise: Texte, die die Software anzeigt, stehen in «Guillemets» oder als `Code`. Werte in spitzen Klammern wie `<Kurzname>` sind zu ersetzen. Alle Beispiele verwenden Platzhalter: die Collection `https://azdo.example.local/DefaultCollection`, die Person «Max Muster» und die Projekte mit den Kurznamen `SHOP`, `OPS` und `Musterlab`.
