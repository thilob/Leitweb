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

## Deployment mit Dockhand

Der Stack [`docker-compose.dockhand.yml`](docker-compose.dockhand.yml) kann in Dockhand direkt aus diesem Git-Repository angelegt werden. Als Compose-Pfad wird `docker-compose.dockhand.yml` verwendet. Vor dem Deployment müssen mindestens diese Stack-Variablen gesetzt werden:

- `POSTGRES_PASSWORD`: langes, zufälliges Datenbankpasswort
- `KEYCLOAK_ADMIN_PASSWORD`: separates, langes Keycloak-Administratorpasswort
- `KEYCLOAK_PUBLIC_URL`: vom Browser erreichbare Keycloak-URL, beispielsweise `https://auth.example.org`

Weitere Variablen und lokale Beispielwerte stehen in [`.env.example`](.env.example). Bei Betrieb hinter einem Reverse Proxy sollten `KEYCLOAK_PUBLIC_URL` auf die externe HTTPS-Adresse und `REQUIRE_HTTPS_METADATA=true` gesetzt werden. Leitweb ist standardmäßig auf Port `5000`, Keycloak auf Port `8080` veröffentlicht.

Status- und Einsatzänderungen werden über die authentifizierte WebSocket-Verbindung `/ws/updates` unmittelbar an alle geöffneten Leitweb-Clients übertragen. Ein vorgeschalteter Reverse Proxy muss deshalb WebSocket-Upgrades (`Upgrade`/`Connection`) an Leitweb weiterreichen. Der Browser baut eine unterbrochene Verbindung automatisch wieder auf.

Das Leitweb-Image wird durch Dockhand aus dem Dockerfile im Repository gebaut. PostgreSQL-Daten bleiben im benannten Volume `dorfpolizei-well-data` erhalten. Beim ersten Start importiert Keycloak den Realm `leitweb`; der Beispielbenutzer lautet `dispatcher` mit dem temporären Passwort `change-me` und muss dieses beim ersten Login ändern. Alle mitgelieferten Zugangsdaten sind ausschließlich für die Ersteinrichtung bestimmt.

### Benutzerverwaltung

Die einfache Benutzeranlage erscheint nur für angemeldete Benutzer mit der Keycloak-Realm-Rolle `user-admin`. Der API-Endpunkt prüft diese Rolle zusätzlich serverseitig. Neue Benutzer erhalten die normalen fachlichen Leitweb-Berechtigungen und ein beim ersten Login zu änderndes temporäres Kennwort; die Rolle `user-admin` wird nicht weitergegeben.

Für den Zugriff der API auf die Keycloak Admin REST API wird im Realm `leitweb` einmalig ein eigener Service-Account eingerichtet:

1. Client `leitweb-user-admin` anlegen, Client-Authentifizierung und Service-Accounts aktivieren.
2. Dem Service-Account unter den Client-Rollen von `realm-management` die Rollen `manage-users`, `view-users` und `view-realm` zuweisen. `view-realm` wird benötigt, um die für Benutzer auswählbaren GIS-Rollen aufzulösen.
3. Das Client-Secret als Dockhand-Variable `KEYCLOAK_ADMIN_CLIENT_SECRET` hinterlegen.
4. Die Realm-Rolle `user-admin` nur den Benutzern zuweisen, die neue Konten anlegen dürfen. Nach einer Rollenzuweisung ist eine erneute Anmeldung erforderlich.

Benutzer mit `user-admin` können in Leitweb außerdem jedem Keycloak-Benutzer genau eine der folgenden hierarchischen GIS-Zugriffsstufen zuweisen:

- `gis-sehen`: freigegebene GIS-Karten und Layer anzeigen
- `gis-objekte-aendern`: enthält `gis-sehen` und erlaubt das Anlegen und Bearbeiten von GIS-Objekten
- `gis-vollzugriff`: enthält beide vorherigen Rollen und ist für die spätere Verwaltung von Quellen, Layern und Kartenprofilen vorgesehen

Bei einem neu importierten Realm werden diese Rollen aus `deploy/keycloak/leitweb-realm.json` angelegt. Keycloak aktualisiert einen bereits vorhandenen Realm beim Containerneustart nicht aus der Importdatei. In einem bestehenden Realm müssen die drei Rollen deshalb einmal mit denselben Namen und Composite-Beziehungen angelegt werden. Danach erscheinen sie ohne weitere Anwendungskonfiguration in der Benutzerverwaltung.

Zusätzlich muss unter `Realm settings` → `User profile` ein Attribut mit dem Namen `permissions` angelegt werden. Es wird als mehrwertig konfiguriert; Benutzer und Administratoren dürfen es sehen, aber nur Administratoren bearbeiten. Keycloak 26 ignoriert unbekannte Attribute standardmäßig. Ohne diese User-Profile-Definition würde ein Benutzer zwar angelegt, sein Access-Token enthielte aber keine fachlichen Berechtigungen und die API antwortete mit HTTP 403. Leitweb prüft deshalb nach der Anlage, ob Keycloak die Berechtigungen gespeichert hat, und entfernt einen andernfalls unbrauchbaren neuen Datensatz wieder.

Das Service-Account-Secret gehört ausschließlich in Dockhand beziehungsweise eine lokale `.env` und darf nicht in Git gespeichert werden.
