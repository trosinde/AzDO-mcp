## Purpose

Defines the German user manual that ships with every release, how it is kept complete against the software, and the README as the entry point for users.

## ADDED Requirements

### Requirement: User manual in release
Every release SHALL contain the user manual as PDF `AzdoMcp-Handbuch-<v>.pdf` with the release version on its title page. The same PDF SHALL be included as `AzdoMcp-Handbuch.pdf` in the installation folder and in the portable zip. The manual is written in German for users and IT administrators, uses the formal "Sie" and Swiss spelling, and contains only placeholder data such as `https://azdo.example.local/DefaultCollection` and "Max Muster".

#### Scenario: Manual readable without installation
- **WHEN** a user downloads `AzdoMcp-Handbuch-2.5.0-rc.2.pdf` from the release
- **THEN** the PDF opens and its title page shows version 2.5.0-rc.2

#### Scenario: Manual next to the program
- **WHEN** the setup is installed or the portable zip is unpacked
- **THEN** `AzdoMcp-Handbuch.pdf` is in the program folder

### Requirement: Manual builds in CI
CI SHALL build the manual as HTML on every push and pull request and fail if the manual cannot be built; the HTML SHALL be available as build artifact `manual-html`.

#### Scenario: Broken manual fails CI
- **WHEN** a chapter references a missing chapter file or the manual builder fails
- **THEN** the CI run fails

### Requirement: Manual completeness
An automated test SHALL fail when the manual does not describe every tool the server registers, every tool group and permission mode by its display name, the update footer texts and the server commands. Tool names SHALL come from the server's tool registration, not from a list in the test. The failure message SHALL name each missing text and its source.

#### Scenario: Required texts
- **WHEN** the completeness test runs
- **THEN** it checks the permission modes "Erlaubt", "Nachfragen" and "Gesperrt", the footer texts "Updates: aktuell", "Update verfügbar", "Updates: nicht konfiguriert" and "Portable", and the commands `--stdio`, `discover` and `--apply`

#### Scenario: New tool without manual entry
- **WHEN** a tool is added to the server and not described in the tools chapter
- **THEN** the completeness test fails and names the tool and its group

#### Scenario: Complete manual
- **WHEN** every required text is described
- **THEN** the completeness test passes

### Requirement: README as entry point
The README SHALL present the product to users in German: name, one-line claim, overview image, benefits with images, a "Loslegen" section from download to the first test question, and links to the manual, development documentation, security policy and contribution guide. Technical reference content (tool lists, data paths, build steps) SHALL live in the manual or under `docs/`, not in the README. Images SHALL show placeholder data only.

#### Scenario: First visit
- **WHEN** a user opens the repository page
- **THEN** the README shows what the product does, images of the Manager and the steps to install it, without build instructions or tool reference tables

#### Scenario: Links work
- **WHEN** every relative link and image in `README.md` and `docs/README.md` is checked
- **THEN** each target exists in the repository
