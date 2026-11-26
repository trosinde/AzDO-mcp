# Dokumentation

| Dokument | Inhalt | Für wen |
|---|---|---|
| [Benutzerhandbuch](manual/index.md) | Installation, Verbindungen und Projekte, MCP-Clients, Berechtigungen, Tools, Protokoll, Meldungen, Update, Referenz; als PDF in jedem Release | Benutzer und IT |
| [Entwicklung](entwicklung.md) | Voraussetzungen, Build, Tests, Handbuch, Release, Workflows, Projektstruktur, OpenSpec | Entwicklung und Mitwirkende |
| [Beitragen](../CONTRIBUTING.md) | Fork, Branch, Pull Request, Prüfungen vor dem Merge | Mitwirkende |
| [Sicherheit](../SECURITY.md) | Sicherheitslücken vertraulich melden | alle |

Weitere Quellen:

- [UI-Entwürfe](ui-proposal/README.md) – verbindliche Vorlage für die Oberflächen und ihre deutschen Texte.
- [Bilder des Handbuchs](manual/images/README.md) – woher die Bilder stammen und wie sie neu gerendert werden.
- [Konzept Release und Handbuch](konzept/release-und-handbuch.md) – Stand bei Erstellung. Wo es von den Specs
  abweicht, gelten die Specs.
- [OpenSpec](../openspec/) – Specs (`specs/`) und Changes (`changes/`) mit Anforderungen, Entscheiden und Aufgaben.

## Bilder aktualisieren

Die Bilder in `manual/images/` und `assets/` sind aus den HTML-Entwürfen in `ui-proposal/` gerendert
(`node tools/render-images.mjs`). Später werden sie durch Bildschirmfotos der echten Software ersetzt.
Dateinamen bleiben gleich, damit die Verweise stimmen.
