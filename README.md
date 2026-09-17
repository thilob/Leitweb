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

## Compose-Dateien pro Branch

| Branch | Betrieb | Compose-Datei |
| --- | --- | --- |
| `Dorfpolizei-Well` | lokal mit Docker oder Podman Compose | `docker-compose.yml` |
| `Dorfpolizei-Well` | Dockhand | `docker-compose.dockhand.yml` |
| `Dorfpolizei-Well-mit-GIS` | lokal und vom Stack ohne GIS getrennt | `compose.gis.yml` |
| `Dorfpolizei-Well-mit-GIS` | Dockhand und vom Stack ohne GIS getrennt | `docker-compose.dockhand.yml` |

Im GIS-Branch wählt `docker compose up` ohne `-f` automatisch die ebenfalls vorhandene `docker-compose.yml`. Für einen unabhängigen parallelen GIS-Betrieb ist stattdessen immer `docker compose -f compose.gis.yml up --build -d` zu verwenden. In Dockhand wird in beiden Branches `docker-compose.dockhand.yml` ausgewählt; maßgeblich ist dabei der jeweils konfigurierte Git-Branch.

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

Die Einsatzortsuche verwendet einen Datenbankindex für Straßen- und Hausnummerpräfixe. Überholte Browseranfragen werden beim Weitertippen abgebrochen; andere Suchfehler erscheinen sichtbar in der Oberfläche. API-Aufrufe werden mit HTTP-Methode, Pfad, Status und Laufzeit protokolliert, jedoch ohne Query-String und damit ohne den eingegebenen Einsatzort.

Status und Protokolle:

```sh
docker compose ps
docker compose logs -f api
```

### Laufzeitstatus und Fehlerdiagnose

In der Hauptnavigation zeigt **Laufzeitstatus** die Erreichbarkeit von PostgreSQL, Keycloak, QGIS und der Leitweb-API. Die Prüfung läuft beim Öffnen sowie anschließend alle 30 Sekunden; über **Erneut prüfen** kann sie jederzeit manuell gestartet werden. Weil die Diagnose nicht von einer funktionierenden Anmeldung abhängen darf, ist sie zusätzlich direkt unter `/status` erreichbar.

Die Seite unterscheidet zwei Perspektiven:

| Bereich | Prüfung | Typische erkannte Ursache |
| --- | --- | --- |
| Docker-Netz | PostgreSQL-Verbindung über den EF-Core-Kontext | Datenbank nicht erreichbar, falsche Zugangsdaten oder Migration/Verbindung gestört |
| Docker-Netz | Keycloak-OIDC-Metadaten über `Authentication__MetadataAddress` | Container, Docker-DNS, interner Port oder Realm nicht erreichbar |
| Docker-Netz | QGIS `GetCapabilities` über `Gis__QgisServerUrl` | QGIS Server, Nginx-Gateway oder Projekt nicht erreichbar |
| Browser | `/health/live` der Leitweb-API | Portfreigabe, Reverse Proxy oder API-Container nicht erreichbar |
| Browser | öffentliche Keycloak-OIDC-Metadaten | falsche `KEYCLOAK_PUBLIC_URL`, DNS, TLS, CORS oder `localhost` auf einem entfernten Client |
| Browser | öffentliche QGIS-WMS-Adresse | falsche `QGIS_PUBLIC_URL`, Port, TLS, CORS oder Reverse Proxy |

Jede Kachel zeigt Status, geprüfte URL, Laufzeit, Fehlermeldung und einen nächsten Prüfschritt. Netzwerkfehler in normalen API-Aufrufen verweisen statt des unspezifischen Textes `Failed to fetch` auf diese Seite. Eine intern grüne, im Browser aber rote Prüfung weist meist auf die öffentliche URL, Portweiterleitung, TLS oder CORS hin. Insbesondere dürfen `KEYCLOAK_PUBLIC_URL` und `QGIS_PUBLIC_URL` bei Zugriff von einem anderen Rechner nicht auf `localhost` zeigen.

Die maschinenlesbaren Endpunkte sind:

- `/health/live`: API-Prozess antwortet
- `/health/ready`: API kann die Datenbank erreichen
- `/health/status`: ausführlicher Statusbericht einschließlich interner Dienste und öffentlicher Endpunktkonfiguration

`/health/status` liefert auch bei einzelnen gestörten Abhängigkeiten HTTP 200, damit die Oberfläche den vollständigen Bericht darstellen kann; der Gesamtzustand steht im JSON-Feld `status`. Die Compose-Stacks lassen die API bereits starten, sobald der Keycloak-Container gestartet wurde, und warten nicht auf dessen Healthcheck. Dadurch bleibt `/status` während eines Keycloak-Starts oder -Ausfalls erreichbar. Die Datenbank bleibt wegen der beim API-Start ausgeführten Migrationen eine harte Startabhängigkeit. Das QGIS-Nginx-Gateway setzt für `/ows` einen CORS-Header, damit die öffentliche Browserprüfung und browserbasierte OGC-Zugriffe funktionieren.

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
- Health-Endpunkte unter `/health/live`, `/health/ready` und `/health/status`
- OpenAPI/Swagger für offenen, dokumentierbaren Datenzugriff

Für PostgreSQL wird das Schema über versionierte EF-Core-Migrationen verwaltet. `EnsureCreated` wird ausschließlich für die flüchtige lokale In-Memory-Vorschau verwendet.

## Deployment mit Dockhand

Der Stack [`docker-compose.dockhand.yml`](docker-compose.dockhand.yml) kann in Dockhand direkt aus diesem Git-Repository angelegt werden. Als Compose-Pfad wird `docker-compose.dockhand.yml` verwendet. Vor dem Deployment müssen mindestens diese Stack-Variablen gesetzt werden:

- `POSTGRES_PASSWORD`: langes, zufälliges Datenbankpasswort
- `KEYCLOAK_ADMIN_PASSWORD`: separates, langes Keycloak-Administratorpasswort
- `KEYCLOAK_PUBLIC_URL`: vom Browser erreichbare Keycloak-URL, beispielsweise `https://auth.example.org`

Weitere Variablen und lokale Beispielwerte stehen in [`.env.example`](.env.example). Bei Betrieb hinter einem Reverse Proxy sollten `KEYCLOAK_PUBLIC_URL` auf die externe HTTPS-Adresse und `REQUIRE_HTTPS_METADATA=true` gesetzt werden. Der GIS-Dockhand-Stack veröffentlicht Leitweb standardmäßig auf Port `5100`, Keycloak auf Port `8180` und QGIS Server auf Port `8190`; damit kollidiert er nicht mit dem Stack ohne GIS.

Nach dem Aktualisieren des Git-Branches muss das API-Image neu gebaut werden, weil Status-Backend und statische Oberfläche Bestandteil dieses Images sind. In Dockhand ist deshalb ein Redeploy mit aktiviertem Image-Build erforderlich. Mit Compose entspricht dies beispielsweise:

```sh
git pull
docker compose -f compose.gis.yml up --build -d
```

Danach zuerst `http(s)://<Leitweb-Host>/status` aufrufen. Wird Leitweb von einem anderen Rechner geöffnet, müssen `KEYCLOAK_PUBLIC_URL` und `QGIS_PUBLIC_URL` aus genau diesem Browser erreichbar sein.

Status- und Einsatzänderungen werden über die authentifizierte WebSocket-Verbindung `/ws/updates` unmittelbar an alle geöffneten Leitweb-Clients übertragen. Ein vorgeschalteter Reverse Proxy muss deshalb WebSocket-Upgrades (`Upgrade`/`Connection`) an Leitweb weiterreichen. Der Browser baut eine unterbrochene Verbindung automatisch wieder auf.

Das Leitweb-Image wird durch Dockhand aus dem Dockerfile im Repository gebaut. Im GIS-Branch bleiben PostgreSQL-Daten im eigenständigen Volume `leitweb-gis-postgres` erhalten. Beim ersten Start importiert Keycloak den Realm `leitweb`; der Beispielbenutzer lautet `dispatcher` mit dem temporären Passwort `change-me` und muss dieses beim ersten Login ändern. Alle mitgelieferten Zugangsdaten sind ausschließlich für die Ersteinrichtung bestimmt.

### Benutzerverwaltung

Die integrierte Benutzerverwaltung erscheint nur für angemeldete Benutzer mit der Keycloak-Realm-Rolle `user-admin`. Der initiale Benutzer `dispatcher` erhält diese Rolle beim Realm-Import und kann die Benutzerverwaltung nach einer neuen Anmeldung über den gleichnamigen Menüpunkt öffnen. Der API-Endpunkt prüft die Rolle zusätzlich serverseitig. Dort lassen sich Konten mit Kontaktdaten, automatisch erzeugtem temporärem Kennwort und einer optionalen GIS-Zugriffsstufe anlegen. Neue Benutzer erhalten die normalen fachlichen Leitweb-Berechtigungen und müssen das Kennwort beim ersten Login ändern; die Rolle `user-admin` wird nicht weitergegeben.

Für den Zugriff der API auf die Keycloak Admin REST API richtet der Realm-Import den vertraulichen Client `leitweb-user-admin` mit einem eigenen Service-Account automatisch ein. Das Secret wird beim Import aus der Dockhand-Variable `KEYCLOAK_ADMIN_CLIENT_SECRET` eingesetzt und steht nicht in der Importdatei. Der Service-Account erhält ausschließlich folgende Rollen des Clients `realm-management`:

- `manage-users`
- `view-users`
- `view-realm` – wird benötigt, um die auswählbaren GIS-Rollen aufzulösen

Die Realm-Rolle `user-admin` darf nur Benutzern zugewiesen werden, die neue Konten anlegen dürfen. Nach einer Rollenzuweisung ist eine erneute Anmeldung erforderlich. Bei einem bereits vorhandenen Realm wird der Import von Keycloak übersprungen; dort muss der Client einmalig manuell eingerichtet oder der Realm mit den neuen Importdaten neu erstellt werden.

Benutzer mit `user-admin` können in Leitweb außerdem jedem Keycloak-Benutzer genau eine der folgenden hierarchischen GIS-Zugriffsstufen zuweisen:

- `gis-sehen`: freigegebene GIS-Karten und Layer anzeigen
- `gis-objekte-aendern`: enthält `gis-sehen` und erlaubt das Anlegen und Bearbeiten von GIS-Objekten
- `gis-vollzugriff`: enthält beide vorherigen Rollen und ist für die spätere Verwaltung von Quellen, Layern und Kartenprofilen vorgesehen

Bei einem neu importierten Realm werden diese Rollen aus `deploy/keycloak/leitweb-realm.json` angelegt. Keycloak aktualisiert einen bereits vorhandenen Realm beim Containerneustart nicht aus der Importdatei. In einem bestehenden Realm müssen die drei Rollen deshalb einmal mit denselben Namen und Composite-Beziehungen angelegt werden. Danach erscheinen sie ohne weitere Anwendungskonfiguration in der Benutzerverwaltung.

Der Realm-Import richtet unter `Realm settings` → `User profile` außerdem das mehrwertige Attribut `permissions` ein. In einem bereits vorhandenen Realm muss dieses Attribut einmal manuell ergänzt werden; Benutzer und Administratoren dürfen es sehen, aber nur Administratoren bearbeiten. Keycloak 26 ignoriert unbekannte Attribute standardmäßig. Ohne diese User-Profile-Definition würde ein Benutzer zwar angelegt, sein Access-Token enthielte aber keine fachlichen Berechtigungen und die API antwortete mit HTTP 403. Leitweb prüft deshalb nach der Anlage, ob Keycloak die Berechtigungen gespeichert hat, und entfernt einen andernfalls unbrauchbaren neuen Datensatz wieder.

Das Service-Account-Secret gehört ausschließlich in Dockhand beziehungsweise eine lokale `.env` und darf nicht in Git gespeichert werden.

## GIS-Compose-Stack

Für einen neuen, vom bisherigen lokalen Stack getrennten GIS-Betrieb steht [`compose.gis.yml`](compose.gis.yml) bereit. Der Stack verwendet den Compose-Projektnamen `dorfpolizei-well-gis` und das eigene Volume `dorfpolizei-well-gis-data`. Er enthält Leitweb auf .NET 10, PostGIS, Keycloak, QGIS Server und ein Nginx-Gateway für dessen OGC-Endpunkt.

```bash
cp .env.example .env
# Passwörter und KEYCLOAK_PUBLIC_URL in .env ersetzen
docker compose -f compose.gis.yml config
docker compose -f compose.gis.yml up --build -d
docker compose -f compose.gis.yml ps
```

Mit den Werten aus `.env.example` sind anschließend Leitweb unter `http://localhost:5100`, Keycloak unter `http://localhost:8180` und QGIS Server unter `http://localhost:8190/ows` erreichbar. Für einen entfernten Host müssen `KEYCLOAK_PUBLIC_URL` sowie gegebenenfalls die veröffentlichten Ports vor dem ersten Realm-Import korrekt gesetzt sein.

QGIS-Projekte liegen unter `deploy/qgis-server/projects` und werden schreibgeschützt nach `/projects` in den Server eingebunden. Das veröffentlichte Standardprojekt ist `start.qgz`; es stellt den Orthophoto-Layer `DOP` als WMS bereit. Eigene, zuvor mit QGIS Desktop geprüfte Projekte können dort versioniert abgelegt werden. Die Auswahl erfolgt über die Compose-/`.env`-Variable `QGIS_PROJECT_FILE`, beispielsweise `QGIS_PROJECT_FILE=/projects/meine-lage.qgz`. Ohne Angabe wird `/projects/start.qgz` verwendet. Das Nginx-Gateway übernimmt keinen festen Projektpfad, sodass die Container-Variable maßgeblich bleibt. `QGIS_PUBLIC_URL` bezeichnet den vom Browser erreichbaren OWS-Endpunkt. Leitweb liest die WMS-Capabilities des jeweils aktiven Projekts automatisch ein und bietet alle darin benannten Layer einzeln in der GIS-Lage zum Ein- und Ausblenden an. Datenbankzustand, Projekte und Containerkonfiguration sind getrennt, sodass dieselben Artefakte später in PersistentVolume, ConfigMap und Deployments eines Kubernetes-Stacks überführt werden können.

Vor einem öffentlichen Betrieb gehören Leitweb, Keycloak und QGIS Server hinter einen TLS-fähigen Reverse Proxy. Der QGIS-Port veröffentlicht den OGC-Endpunkt derzeit direkt und besitzt keine eigene Keycloak-Prüfung; fachliche Schreibzugriffe erfolgen deshalb ausschließlich über die rollenbasierte Leitweb-API.
