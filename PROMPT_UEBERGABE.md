# Übergabe-Prompt für eine neue Codex-Instanz

Du arbeitest im privaten GitHub-Projekt `thilob/Leitweb` auf dem Branch `Dorfpolizei-Well`. Bitte lies zuerst `ANFORDERUNGEN.md`, `README.md` und den aktuellen Git-Status. Bewahre vorhandene Änderungen und arbeite auf diesem Branch weiter, sofern der Benutzer nichts anderes verlangt.

## Ziel des Projekts

Wir entwickeln den technischen Prototyp „Dorfpolizei Well“: eine containerisierte Webanwendung für polizeiliche Einsatzführung und Fallbearbeitung. Sie soll lokal mit Docker Compose persistent betrieben und später grundsätzlich nach Linux/Rocky/RHEL beziehungsweise Kubernetes überführt werden können.

## Aktueller Funktionsumfang

- Polizeilich gebrandete, responsive Oberfläche ohne separate Frontend-Buildkette
- Einsatzanlage mit polizeitypischen Einsatzanlässen
- Lokale Einsatzortsuche aus amtlichen Gebäudereferenzen
- Einsatz- und Statusführung
- Zehn vorbereitete Einsatzmittel mit Rufnamen `Well 11/21`, `Well 11/31` bis `Well 11/38` und `Well 11/81`
- Statusübersicht für alle Einsatzmittel
- Umwandlung eines Einsatzes in eine Fallakte
- Änderbarer Fallstatus und Filterung der Fallliste
- Personen in unterschiedlichen Beteiligtenrollen
- Asservatenverwaltung
- Typische Schreiben wie Kurzbericht, Strafanzeige, Einsatzbericht, Zeugenvernehmung, Sicherstellungsprotokoll, Übersendungs- und Abschlussbericht
- Abverfügung von Schreiben an vorgesetzte Stellen und beteiligte Behörden
- Claim-basiertes RBAC
- Swagger/OpenAPI und Health-Endpunkte

## Architektur

- ASP.NET Core 6 Web API als modularer Monolith
- Entity Framework Core mit PostgreSQL
- Statisches Frontend in `src/Leitweb.Api/wwwroot`
- Keycloak ist als Identitätsdienst vorbereitet
- Docker Compose startet API, PostgreSQL und Keycloak
- Anwendungs- und Keycloak-Daten liegen in getrennten PostgreSQL-Datenbanken, aber im gemeinsamen benannten Volume `dorfpolizei-well-data`
- PostgreSQL-Schemaänderungen laufen über versionierte EF-Core-Migrationen
- Der lokale Start ohne Docker verwendet eine flüchtige In-Memory-Datenbank

## Wichtige Dateien

- `docker-compose.yml`: persistente lokale Compose-Umgebung
- `.env.example`: Vorlage für lokale Passwörter, Ports und Hostnamen
- `README.md`: Start, Persistenz, Backup und Wiederherstellung
- `src/Leitweb.Api/Program.cs`: Konfiguration, Authentifizierung, Migration und Grunddatenimport
- `src/Leitweb.Api/Data/LeitwebDbContext.cs`: Datenmodell und Indizes
- `src/Leitweb.Api/Data/Migrations/`: PostgreSQL-Migrationen
- `src/Leitweb.Api/Data/addresses.tsv`: rund 100.000 eindeutige lokale Gebäudereferenzen
- `tools/import-addresses.ps1`: reproduzierbarer Import von Geobasis NRW
- `src/Leitweb.Api/Controllers/CasesController.cs`: Fallakten, Personen, Asservate, Schreiben und Abverfügungen
- `src/Leitweb.Api/wwwroot/app.js`: aktuelle Oberflächenlogik

## Adressdaten

Der Bestand umfasst Wermelskirchen sowie die direkt angrenzenden Kommunen Remscheid, Hückeswagen, Wipperfürth, Kürten, Odenthal, Burscheid und Solingen. Quelle ist die amtliche Gebäudereferenz-API von Geobasis NRW unter Datenlizenz Deutschland – Zero – Version 2.0. Die Anwendung importiert die TSV-Datei nur in eine leere Adresstabelle und führt bei der normalen Suche keine Onlineabfragen aus.

## Start

Persistenter Compose-Betrieb:

```powershell
Copy-Item .env.example .env
# Passwörter in .env ersetzen
docker compose up --build -d
docker compose ps
```

Lokale Vorschau ohne Docker:

```powershell
dotnet run --project src/Leitweb.Api/Leitweb.Api.csproj --urls http://localhost:5000
```

Die Anwendung ist dann unter `http://localhost:5000` erreichbar.

## Wichtige Einschränkungen

- Docker war auf dem bisherigen Entwicklungsrechner nicht installiert. Build, Migrationsskript und YAML-Syntax wurden geprüft, aber der vollständige Compose-Containerlauf muss auf einem Docker-/Podman-Rechner noch als Smoke-Test ausgeführt werden.
- Compose verwendet aktuell bewusst die Development-Testauthentifizierung, damit die Oberfläche sofort benutzbar ist. Keycloak und seine Datenbank sind persistent vorbereitet, aber ein echter Browser-OIDC-/PKCE-Login fehlt noch. Der Compose-Standard darf deshalb nicht öffentlich exponiert werden.
- .NET 6 ist nicht mehr im Support. Eine Aktualisierung auf eine unterstützte LTS-Version ist erforderlich.
- Der Prototyp ist nicht für reale Polizeidaten freigegeben. Vorher fehlen unter anderem Datenschutz-Folgenabschätzung, Löschkonzept, revisionssicheres Audit, sichere Geheimnisverwaltung, TLS, Mandantenzuordnung, Aktenexport und weitere fachrechtliche Prüfungen.
- Bei einer alten Docker-Datenbank, die noch vor Einführung der EF-Migrationen mit `EnsureCreated` aufgebaut wurde, sollte zuerst ein Backup angelegt und anschließend ein frisches Volume verwendet werden.

## Sinnvolle nächste Schritte

1. Compose auf einem Docker-/Podman-System vollständig starten und API, PostgreSQL, Keycloak, Neustartpersistenz und Backup/Restore testen.
2. Auf eine unterstützte .NET-LTS-Version migrieren.
3. Keycloak-Login im Browser mit Authorization Code Flow und PKCE integrieren und die Testauthentifizierung im Compose-Betrieb deaktivieren.
4. Organisationszuordnung serverseitig aus dem Benutzer-Token erzwingen.
5. Audit-Log, Bearbeitungshistorien und Lösch-/Aufbewahrungsregeln ergänzen.
6. Automatisierte API-, UI- und Container-Integrationstests einführen.

Arbeite autonom weiter, aber behandle Datenschutz, Zugriffsrechte, Datenlöschung und öffentlich erreichbare Konfigurationen als sicherheitskritisch. Nach Änderungen Build und passende Kernabläufe prüfen sowie den Branch nur auf ausdrücklichen Wunsch committen oder pushen.
