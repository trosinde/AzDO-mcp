# UI-Entwurf → WPF-Views

Quelle: Design-Canvas „AzDO MCP Manager – UI-Proposal“ (Artboards als `*.dc.html`). Hostname,
Collection- und Personennamen sind durch Platzhalter ersetzt. Die deutschen Texte der Artboards
sind verbindlich; Zahlen in `[ ]` sind Platzhalter und nicht im Code.

| Artboard | Inhalt | WPF-View | ViewModel (Manager.Core) |
|---|---|---|---|
| `Main.dc.html` | 1 · Übersicht (Projekte je Verbindung) | `src/AzdoMcp.Manager/Views/OverviewView.xaml` | `OverviewViewModel` |
| `Verbindung.dc.html` | 2 · Verbindung & PAT (PAT-Scope, Projekte erkennen) | `Views/ConnectionsView.xaml` | `ConnectionsViewModel` |
| `Clients.dc.html` | 3 · MCP-Clients einbinden | `Views/ClientsView.xaml` | `ClientsViewModel` |
| `Tools.dc.html` | 4 · Tools & Rechte (Gruppe → Tool → Verbindung → Projekt) | `Views/PermissionsView.xaml` | `PermissionsViewModel` |
| `Protokoll.dc.html` | 5 · Protokoll | `Views/LogView.xaml` | `LogViewModel` |
| `Tray.dc.html` (Menü) | 6 · Tray-Menü | `Tray/TrayMenuWindow.xaml`, `Tray/TrayIcon.cs` | `MainViewModel` (Tray-Eigenschaften) |
| `Tray.dc.html` (Toast) | 6 · Freigabe-Toast | `Tray/ApprovalWindow.xaml` | `ApprovalRequest` (Core/Ipc) |
| `Erkennung.dc.html` | 7 · Projekt-Erkennung | `Views/ProjectDiscoveryView.xaml` (Schritt „Projekte“ des Wizards) | `SetupWizardViewModel` |
| `NeuEinlesen.dc.html` | 8 · Neu einlesen | `Views/ProjectRescanView.xaml` | `ProjectRescanViewModel` |

Rahmen (Titelleiste, Navigation, Footer „Benutzer-Installation · ohne Admin“, „v… · Updates: …“):
`MainWindow.xaml`. Farben, Pills und Ampel: `Themes/Theme.xaml`.

## Einrichtungs-Wizard (ohne eigenes Artboard)

Auf Wunsch des Owners läuft die Erkennung als Wizard, der jedes ausgewählte Projekt einzeln durchgeht:
**Verbindung** (nur bei neuer Verbindung: URL, PAT, Test mit PAT-Scope wie Artboard 2) → **Projekte**
(Artboard 7) → **Projekt k von n** (Kurzname mit Vorschlag und Vorschau, Standardprojekt, Schreibschalter, Modus je Tool-Gruppe, effektives
Recht rechts; Stil wie Artboards 2 und 4) → **Zusammenfassung** → „Übernehmen (N Projekte)“.
„Neu einlesen“ (Artboard 8) übergibt angehakte neue Projekte an die Projekt-Schritte des Wizards.

## Bewusste Abweichungen vom Entwurf

- **Titelleiste**: Standard-Fensterrahmen von Windows 11 statt nachgebauter Leiste (gleiches Aussehen, Snap-Layouts bleiben).
- **Verbindungen & PATs**: Server-Typ umbrechend, PAT-Buttons unter dem Feld, „Standardprojekt“ als Auswahlliste über alle
  Projekte statt Checkbox „Standard-Verbindung“ (der Standard gilt pro Projekt) – bei 1120 px sonst zu eng.
- **Projekt-Erkennung**: Vorausgewählt werden alle neuen Projekte mit mindestens einem lesbaren Bereich (auch nur Code);
  das Artboard zeigt ein solches Projekt nicht angehakt.
- **MCP-Clients**: Jede Karte hat zusätzlich den Abschnitt „Prompt zum Einrichten“ mit „Prompt kopieren“ und eine
  Testfrage; Status „prüfen“ (gelb), wenn OpenCode einen `servers`-Block unter `mcp` enthält (ohne Artboard).
- **Kurznamen**: „Alias“ heißt in der Oberfläche „Kurzname“; unter „Verbindungen & PATs“ steht unter dem Projekte-Kasten
  je Projekt ein Feld zum Umbenennen (ohne Artboard).
- **Rechte-Anzeige**: Statt einer Ampel pro Bereich (Schreiben / nur Lesen / kein Zugriff) zeigen alle Seiten je Bereich
  „Lesen | Schreiben“ mit ja (grün), nein (rot), n/a bzw. ? (grau, mit Tooltip). Gelb gibt es für Rechte nicht mehr;
  der Status nennt bei „nur Lesen“ den Grund.
- **Tools & Rechte**: Unter jeder Gruppe zusätzlich „Verbindung · alle Tools“ – dort setzt der Wizard Projekt-Modi pro Gruppe;
  „↺“ entfernt eine Ausnahme wieder.
- **Protokoll**: Checkbox „Für diese Sitzung nicht mehr fragen“ entfällt – Freigaben merken ist in v2.0 ausgeschlossen.
- **Übersicht**: Die Clients stehen unter der Verbindungstabelle statt rechts daneben – das Lesen/Schreiben-Raster
  braucht die Breite. Anzahl aktiver Clients als Tooltip der Status-Pill; „Neu einlesen“ statt „N neue · einlesen“
  (die Anzahl neuer Projekte ist erst nach einer Abfrage bekannt).
- **Freigabe**: eigenes Fenster unten rechts statt Windows-Toast (Toasts mit Buttons brauchen eine COM-Registrierung).
