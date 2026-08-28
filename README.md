# Dorfpolizei Well

Erstes MVP einer containerisierten Leitstellen-Webanwendung. Der aktuelle Schnitt stellt eine versionierte REST-API für Einsätze und Einsatzmittel bereit. PostgreSQL übernimmt die strukturierte Datenhaltung, Keycloak die Anmeldung und Berechtigungs-Claims.

Die integrierte Oberfläche unter `/` bietet eine polizeiliche Einsatzlage mit Statusführung, auswählbaren Einsatzanlässen, Einsatzmittel-Stammdaten und Disposition. Aus einem Einsatz kann eine eindeutige digitale Fallakte angelegt werden. Darin werden Beteiligte nach Rollen, Asservate, typische polizeiliche Schreiben und deren Abverfügungen an vorgesetzte Stellen oder andere Behörden geführt. Sie benötigt keine separate Frontend-Buildkette.

## Fallbearbeitung

- Beteiligte Rollen wie beschuldigte, tatverdächtige, geschädigte, anzeigende oder bezeugende Person
- Asservatennummer, Beschreibung, Aufbewahrungsort und Status
- Kurzbericht, Strafanzeige, Einsatzbericht, Vernehmung, Sicherstellungsprotokoll, Übersendungs- und Abschlussbericht
- Vorbelegte Dokumententwürfe aus Einsatz und Fallakte
- Abverfügungen unter anderem an Staatsanwaltschaft, Gerichte, Kriminalpolizei, Ordnungsamt und Jugendamt

Die Anwendung bildet einen technischen Prototyp ab. Vor einer Verarbeitung realer Polizeidaten sind insbesondere Datenschutz-Folgenabschätzung, Löschkonzept, revisionssichere Auditierung, Aktenexport, qualifizierte Signatur und die jeweiligen landesrechtlichen Vorgaben umzusetzen.

## Lokales Adressregister

Die Einsatzortsuche verwendet einen lokalen Datenbankbestand und führt während der Bedienung keine Onlineabfragen aus. Der mitgelieferte Grundbestand umfasst amtliche Gebäudereferenzen für Wermelskirchen sowie Remscheid, Hückeswagen, Wipperfürth, Kürten, Odenthal, Burscheid und Solingen. Quelle: Geobasis NRW, Datenlizenz Deutschland – Zero – Version 2.0, abgerufen am 28.08.2026.

Der Grundbestand kann bei Bedarf aktualisiert werden:

```powershell
./tools/import-addresses.ps1
```

Beim Aufbau einer leeren Datenbank wird `Data/addresses.tsv` einmalig importiert. Bestehende Datenbanken bleiben dabei unverändert; für spätere Aktualisierungen ist eine explizite Import-/Austauschmigration vorgesehen.

## Start

Voraussetzung ist Docker mit Compose oder Podman Compose. Vor dem ersten Start wird eine lokale Konfigurationsdatei angelegt:

```sh
cp .env.example .env
```

Unter PowerShell:

```powershell
Copy-Item .env.example .env
```

In `.env` müssen mindestens die beiden Beispielpasswörter ersetzt werden. Bei einem anderen Rechnernamen oder einer Server-IP wird außerdem `PUBLIC_HOSTNAME` angepasst. Danach:

```sh
docker compose up --build -d
docker compose ps
```

Danach sind erreichbar:

- Anwendung: http://localhost:5000
- Swagger im Entwicklungsbetrieb: http://localhost:5000/swagger
- Keycloak: http://localhost:8080

Der Compose-Standard ist ein einfach nutzbarer lokaler, persistenter Testbetrieb. Die Oberfläche verwendet dabei den automatisch berechtigten Testbenutzer. Keycloak und dessen Datenbank werden bereits persistent betrieben, sind aber erst nach Ergänzung eines Browser-OIDC-Logins der produktive Authentifizierungsweg. Dieser Modus darf deshalb nicht unverändert öffentlich erreichbar gemacht werden.

### Persistenz

Anwendungsdaten und Keycloak liegen in zwei getrennten PostgreSQL-Datenbanken im benannten Docker-Volume `dorfpolizei-well-data`. `docker compose down` beendet und entfernt die Container, erhält aber alle Daten. Ein erneutes `docker compose up -d` verwendet denselben Stand.

Das Schema wird beim API-Start automatisch über versionierte Entity-Framework-Migrationen aktualisiert. Grunddaten und das lokale Adressregister werden nur in leere Tabellen importiert.

Status und Protokolle:

```sh
docker compose ps
docker compose logs -f api
```

Sicherung aller Anwendungs- und Keycloak-Daten:

```sh
docker compose exec -T database pg_dumpall -U leitweb > dorfpolizei-backup.sql
```

Wiederherstellung in eine leere Compose-Datenbank:

```sh
docker compose exec -T database psql -U leitweb -d postgres < dorfpolizei-backup.sql
```

Nur wenn wirklich alle persistenten Daten gelöscht werden sollen:

```sh
docker compose down -v
```

`down -v` entfernt das Volume einschließlich aller Einsätze, Fälle, Personen, Asservate, Schreiben, Benutzer- und Keycloak-Daten unwiderruflich.

### Start ohne Docker

Für eine schnelle lokale Vorschau ist ein ausdrücklich auf `Development` begrenzter Modus enthalten. Er verwendet eine flüchtige In-Memory-Datenbank, Beispieldaten und einen automatisch berechtigten Testbenutzer:

```sh
dotnet run --project src/Leitweb.Api/Leitweb.Api.csproj --urls http://localhost:5000
```

Beispiel-Organisations-ID: `11111111-1111-1111-1111-111111111111`. Nach einem Neustart werden die flüchtigen Daten neu angelegt. Der Compose-Betrieb verwendet dagegen PostgreSQL und behält die Daten dauerhaft im Volume.

Unter Rocky Linux und RHEL ist der Keycloak-Mount mit dem SELinux-Label `Z` versehen. Die Compose-Datei ist damit auch für Podman vorbereitet, muss aber noch in einer Linux-CI tatsächlich als Integrationstest ausgeführt werden.

## API und RBAC

Die Endpunkte liegen unter `/api/v1`. Autorisierung erfolgt über den mehrfach vorkommenden JWT-Claim `permission`:

- `incident.read`, `incident.create`, `incident.update`
- `resource.read`, `resource.manage`

Einsätze und Einsatzmittel tragen eine `organizationId`. Dieser erste Stand filtert Listen danach; eine verbindliche serverseitige Zuordnung des angemeldeten Benutzers zu Organisationen ist der nächste Sicherheitsschritt.

## Architekturentscheidungen

- Modularer Monolith als einfacher Ausgangspunkt
- Zustandslose API und externe PostgreSQL-Datenbank, daher später gut nach Kubernetes übertragbar
- Health-Endpunkte unter `/health/live` und `/health/ready`
- OpenAPI/Swagger für offenen, dokumentierbaren Datenzugriff

Für PostgreSQL wird das Schema über versionierte EF-Core-Migrationen verwaltet. `EnsureCreated` wird ausschließlich für die flüchtige lokale In-Memory-Vorschau verwendet.
