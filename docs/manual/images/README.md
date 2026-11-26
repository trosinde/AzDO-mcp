# Bilder des Handbuchs

Die Bilder in diesem Ordner sind aus den UI-Entwürfen in [`docs/ui-proposal/`](../../ui-proposal/) gerendert
(Skript [`tools/render-images.mjs`](../../../tools/render-images.mjs), 1720 px breit, Anzeige 860 px). Sie
enthalten nur Platzhalterdaten (Projekte SHOP, OPS, Musterlab, «Max Muster», `azdo.example.local`).

Sobald die Oberflächen fertig sind, werden die Bilder durch Bildschirmfotos der echten Software ersetzt.
**Die Dateinamen bleiben gleich**, damit die Verweise in den Kapiteln stimmen. Bildschirmfotos dürfen ebenfalls
nur Platzhalterdaten zeigen – keine echten Server, Projekte, Personen oder PATs.

| Bild | Quelle (Artboard) | Inhalt |
|---|---|---|
| `main.png` | `Main.dc.html` | Übersicht: Server-Status, Verbindungen mit Projekten, Clients |
| `verbindung.png` | `Verbindung.dc.html` | Verbindungen & PATs: Collection, PAT, PAT-Scope, Projekte erkennen |
| `erkennung.png` | `Erkennung.dc.html` | Projekte erkennen: gefundene Projekte mit effektivem Recht |
| `neu-einlesen.png` | `NeuEinlesen.dc.html` | Neu einlesen: neue, nicht mehr sichtbare und geänderte Projekte |
| `tools.png` | `Tools.dc.html` | Tools & Rechte: Modi je Gruppe, Tool, Verbindung und Projekt |
| `clients.png` | `Clients.dc.html` | MCP-Clients einbinden |
| `protokoll.png` | `Protokoll.dc.html` | Protokoll mit offener Freigabe |
| `tray.png` | `Tray.dc.html` | Tray-Menü und Freigabefenster |

Fehlende Bilder: keine – alle Bilder liessen sich rendern.

## Bekannte Abweichungen der Entwürfe von der Software

Die Entwürfe zeigen an einigen Stellen einen älteren Stand (siehe
[`docs/ui-proposal/README.md`](../../ui-proposal/README.md), «Bewusste Abweichungen vom Entwurf»). Die
Handbuchtexte beschreiben die Software, nicht den Entwurf:

- Rechte erscheinen in der Software je Bereich als «Lesen | Schreiben» mit ja, nein, n/a und ?, nicht als Ampel.
- `protokoll.png` zeigt die Checkbox «Für diese Sitzung nicht mehr fragen», die es in der Software nicht gibt.
- `main.png` zeigt die Clients rechts neben den Verbindungen; in der Software stehen sie darunter.
- Die Freigabe erscheint als eigenes Fenster unten rechts, nicht als Windows-Benachrichtigung.
- Der Footer zeigt die Version des Entwurfs (v2.0.0).

## Bilder neu rendern

```bash
npm install --prefix tools
node tools/render-images.mjs
```

Das Skript nutzt Playwright mit Chromium (`npx --prefix tools playwright install chromium`, falls der Browser
fehlt) und schreibt zusätzlich die README-Bilder nach [`docs/assets/`](../../assets/): `hero.png` (Übersicht),
`feature-1-erkennung.png`, `feature-2-rechte.png` (Tools & Rechte mit dem Freigabefenster aus dem Tray-Entwurf),
`feature-3-clients.png` und `feature-4-protokoll.png`.
