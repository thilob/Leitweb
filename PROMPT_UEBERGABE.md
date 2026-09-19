# Übergabe-Prompt für eine neue Codex-Instanz

Stand der Dokumentation: 19.09.2026. Die datierten Abschnitte weiter unten sind ein Änderungsprotokoll; bei Widersprüchen gelten dieser aktuelle Überblick, `README.md`, Projektdateien und Code.

Du arbeitest im privaten GitHub-Projekt `thilob/Leitweb` auf dem Branch `Dorfpolizei-Well-mit-GIS`. Bitte lies zuerst `ANFORDERUNGEN.md`, `README.md` und den aktuellen Git-Status. Bewahre vorhandene Änderungen und arbeite auf diesem Branch weiter, sofern der Benutzer nichts anderes verlangt.

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

- ASP.NET Core 10 Web API als modularer Monolith
- Entity Framework Core mit PostgreSQL
- Statisches Frontend in `src/Leitweb.Api/wwwroot`
- Keycloak ist als Identitätsdienst vorbereitet
- Docker Compose startet API, PostgreSQL/PostGIS, Keycloak, QGIS Server und das Nginx-OWS-Gateway
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

Beim Start ohne Docker ist die Anwendung dann unter `http://localhost:5000` erreichbar. Beim Compose-Start bestimmen `LEITWEB_PORT`, `KEYCLOAK_PORT` und `QGIS_PORT` aus `.env` die veröffentlichten Ports; `.env.example` verwendet 5100, 8180 und 8190.

## Wichtige Einschränkungen

- Der lokale Standardstack verwendet bewusst Development-Testauthentifizierung und darf deshalb nicht öffentlich exponiert werden. Die beiden anderen Compose-Dateien verwenden den implementierten Keycloak-Login mit Authorization Code Flow und PKCE.
- Der Prototyp ist nicht für reale Polizeidaten freigegeben. Vorher fehlen unter anderem Datenschutz-Folgenabschätzung, Löschkonzept, revisionssicheres Audit, sichere Geheimnisverwaltung, TLS, Mandantenzuordnung, Aktenexport und weitere fachrechtliche Prüfungen.
- Bei einer alten Docker-Datenbank, die noch vor Einführung der EF-Migrationen mit `EnsureCreated` aufgebaut wurde, sollte zuerst ein Backup angelegt und anschließend ein frisches Volume verwendet werden.

## Sinnvolle nächste Schritte

1. Organisationszuordnung serverseitig aus dem Benutzer-Token erzwingen.
2. Audit-Log, Bearbeitungshistorien und Lösch-/Aufbewahrungsregeln ergänzen.
3. Automatisierte API-, UI- und Container-Integrationstests einführen.
4. OpenLayers lokal ausliefern und eine Content-Security-Policy ergänzen.

Arbeite autonom weiter, aber behandle Datenschutz, Zugriffsrechte, Datenlöschung und öffentlich erreichbare Konfigurationen als sicherheitskritisch. Nach Änderungen Build und passende Kernabläufe prüfen sowie den Branch nur auf ausdrücklichen Wunsch committen oder pushen.

## Fortführung: GIS-Stand und lauffähiger Compose-Betrieb (30.08.2026)

Der folgende datierte Abschnitt hält die damalige Umsetzung fest.

### Inzwischen umgesetzt

- Die Anwendung verwendet .NET 10 statt .NET 6. `global.json` verlangt SDK 10.0.111; der vorgesehene Docker-Build besitzt ein passendes SDK.
- Browser-Anmeldung über Keycloak mit Authorization Code Flow und PKCE ist implementiert. Access- und Refresh-Token werden nur im Speicher gehalten; der Access-Token wird auch für die authentifizierte WebSocket-Verbindung `/ws/updates` verwendet.
- Der lokale Compose-Standard bleibt ein ausdrücklich nicht für die Öffentlichkeit bestimmter Development-Testbetrieb. `/app-config.json` veröffentlicht dafür `useTestAuthentication`. Das Frontend überspringt in diesem Modus Keycloak, lädt die Einsatzlage sofort und verwendet keine fingierten Bearer-Token.
- Der Development-Benutzer besitzt alle fachlichen Permissions sowie `user-admin` und `gis-vollzugriff`, damit Benutzerverwaltung und GIS im lokalen Referenzbetrieb geprüft werden können. Außerhalb dieses Modus bleiben Keycloak und die dort vergebenen Rollen maßgeblich.
- PostgreSQL wurde auf PostGIS umgestellt. GIS-Datenmodelle, Migrationen und rollenbasierte API-Endpunkte für Layer, GeoJSON-Objekte und Kartenprofile sind vorhanden.
- Die Oberfläche enthält eine GIS-Lage auf Basis von OpenLayers. Adressen aus dem lokalen Register können zur Positionierung eines Einsatzortes verwendet werden.
- Der Stack enthält QGIS Server und ein vorgeschaltetes Nginx-Gateway. Das Gateway stellt `/health`, eine neutrale Startantwort unter `/` und den FastCGI-OWS-Pfad `/ows` bereit.
- Der Docker-Build verwendet den Repository-Root und das Root-`Dockerfile`, damit das .NET-10-Projekt mit allen benötigten Dateien gebaut wird.

### Verifizierter lokaler Stack

`docker compose up --build -d` wurde auf Docker Desktop/WSL2 tatsächlich ausgeführt. API, PostGIS, Keycloak, QGIS Server und Nginx starten. Die Datenbank ist healthy, `database-init` endet mit Exit-Code 0, `/health/ready` liefert HTTP 200 und Einsatz-, Einsatzmittel- sowie GIS-Layer-API liefern Daten. Das benannte Volume `dorfpolizei-well-data` blieb bei den Tests erhalten.

Beim Start kann die API einmalig melden, dass PostgreSQL noch startet; die Compose-Restart-Policy lässt sie danach erfolgreich hochfahren. Außerdem meldet das schlanke Runtime-Image eine fehlende optionale `libgssapi_krb5.so.2`; normale PostgreSQL-Verbindungen funktionieren dennoch.

### Noch offen / bekannte Einschränkungen

- Das QGIS-Projekt `deploy/qgis-server/projects/start.qgz` ist eingebunden und veröffentlicht den Orthophoto-Layer `DOP`. WMS-`GetCapabilities` und ein tatsächlicher `GetMap`-Abruf für Wermelskirchen wurden mit HTTP 200 geprüft. Der veröffentlichte Projektpfad ist in allen Compose-Varianten über `QGIS_PROJECT_FILE` konfigurierbar; Standard ist `/projects/start.qgz`. Nginx verdrahtet keinen eigenen Projektpfad. Die API liest über `/api/v1/gis/qgis-layers` dynamisch die WMS-Capabilities des aktiven Projekts; jeder benannte QGIS-Layer erscheint dadurch automatisch als einzeln schaltbarer Layer in der GIS-Lage. `QGIS_PUBLIC_URL` muss vom Browser erreichbar sein.
- OpenLayers wird derzeit von jsDelivr geladen. Für einen vollständig abgeschotteten Betrieb sollte die Bibliothek lokal ausgeliefert und mit einer Content-Security-Policy abgesichert werden.
- Der lokale Development-Modus gewährt absichtlich umfassende Rechte. Er darf nicht öffentlich veröffentlicht werden. Für reale Deployments müssen Testauthentifizierung und Testdaten deaktiviert, TLS erzwungen und Keycloak-Rollen korrekt administriert werden.
- Es fehlen weiterhin automatisierte API-, Browser- und Compose-Integrationstests sowie Auditierung, Löschkonzept, sichere Secret-Verwaltung und verbindliche Mandantenzuordnung aus dem Token.
- Auf dem aktuellen Windows-Host ist lokal nur das .NET-6-SDK installiert. Builds werden deshalb momentan reproduzierbar über das .NET-10-Docker-Build-Image geprüft.

### Wichtige GIS-Dateien

- `compose.gis.yml`: separater GIS-Compose-Stack
- `deploy/qgis-server/Dockerfile` und `start-qgis-server.sh`: QGIS-Server-Image
- `deploy/qgis-server/nginx.conf`: OWS-Gateway
- `deploy/qgis-server/projects/`: Ablage für versionierte, mit QGIS Desktop geprüfte Projekte
- `src/Leitweb.Api/Controllers/GisController.cs`: GIS-Layer, Features und Profile
- `src/Leitweb.Api/Domain/GisModels.cs`: GIS-Domänenmodell
- `src/Leitweb.Api/Data/Migrations/20260829120000_AddGis.cs`: PostGIS-Erweiterung und GIS-Schema
- `src/Leitweb.Api/wwwroot/app.js`: OpenLayers-Karte, Rollenprüfung und Karteninteraktion

## Fortführung: Adressauflösung und dynamisches QGIS-Projekt (30.08.2026)

### Einsatzorte und Kartenposition

Die frühere Adresssuche verglich den gesamten normalisierten Suchtext mit einer festen Verkettung aus Straße, Hausnummer, Postleitzahl und Gemeinde. Da der amtliche Grundbestand derzeit keine Postleitzahlen enthält, entstanden in der Verkettung doppelte Leerzeichen. Deshalb fand beispielsweise `Well 8 Wermelskirchen` den vorhandenen Datensatz `Well 8, Wermelskirchen` trotz gültiger Koordinaten nicht.

`AddressesController` sucht nun tokenbasiert über alle Adressbestandteile. Kommas, Semikolons, Mehrfachleerzeichen und eine abweichende Reihenfolge verhindern keinen Treffer mehr. Bis zu 500 serverseitig gefilterte Kandidaten werden anschließend separatorunabhängig gerankt; eine exakt passende Hausnummer steht dadurch vor Zusätzen wie `8a`, `8b` und `8c`. Das Frontend normalisiert Einsatzort und Ergebnis auf dieselbe Weise. Für `Well 8 Wermelskirchen` wird `Well 8, Wermelskirchen` mit `51.126360736377165, 7.252051253176141` als erster Treffer geliefert.

### Variables QGIS-Projekt

Das QGIS-Projekt liegt als `deploy/qgis-server/projects/start.qgz` vor und enthält aktuell den Orthophoto-WMS-Layer `DOP`. Der Projektpfad ist in `docker-compose.yml`, `compose.gis.yml` und `docker-compose.dockhand.yml` nicht mehr fest verdrahtet:

```env
QGIS_PROJECT_FILE=/projects/start.qgz
QGIS_PUBLIC_URL=http://SERVER-IP-ODER-DNS-NAME:8090/ows
```

`QGIS_PROJECT_FILE` bezeichnet einen Pfad innerhalb des nach `/projects` gemounteten Verzeichnisses und fällt ohne Angabe auf `/projects/start.qgz` zurück. Das Nginx-Gateway übergibt keinen eigenen `QGIS_PROJECT_FILE`-FastCGI-Parameter mehr; maßgeblich ist ausschließlich die Umgebung des QGIS-Server-Containers. `QGIS_PUBLIC_URL` ist die vom Browser erreichbare WMS-Adresse und muss bei einem entfernten Host ausdrücklich angepasst werden.

### Automatische Layerübernahme

Der geschützte Endpunkt `/api/v1/gis/qgis-layers` ruft serverseitig über `Gis:QgisServerUrl` die WMS-Capabilities des aktiven QGIS-Projekts ab. XML-DTDs und externe Resolver sind dabei deaktiviert. Alle benannten WMS-Layer werden dedupliziert und mit `Gis:QgisPublicUrl` an das Frontend geliefert. Die GIS-Lage kombiniert diese dynamischen QGIS-Layer mit den bereits in der Datenbank verwalteten OGC-Quellen und bietet jeden Layer einzeln zum Ein- und Ausblenden an. Ein Projektwechsel benötigt damit keine manuelle Änderung von Layernamen im Code oder in der Datenbank. Ist QGIS vorübergehend nicht erreichbar, lädt die Oberfläche weiterhin ihre übrigen GIS-Layer und -Objekte.

### Verifikation dieses Stands

- .NET-10-Publish im Docker-Build erfolgreich
- JavaScript mit `node --check` geprüft
- alle drei Compose-Varianten erfolgreich aufgelöst
- variables `QGIS_PROJECT_FILE` mit Standard- und Override-Wert geprüft
- QGIS WMS `GetCapabilities` für `start.qgz`: HTTP 200
- dynamischer API-Layerkatalog: `DOP` mit `http://localhost:8090/ows`
- WMS `GetMap` für einen Ausschnitt bei Wermelskirchen: HTTP 200 und gültiges PNG
- Adressvarianten `Well 8 Wermelskirchen`, `Well 8, Wermelskirchen` und `Wermelskirchen Well 8` liefern denselben priorisierten Datensatz

## Fortführung: Einsatznummern und operative GIS-Darstellung (30.08.2026)

### Automatische Einsatznummer

Die frühere Formularvorgabe `DPW-E-<Jahr>-` konnte unverändert gespeichert werden. Da Einsatznummern pro Organisation eindeutig sind, führte jede weitere Anlage mit demselben Präfix zu PostgreSQL-Fehler `23505` und einem nicht abgefangenen HTTP 500.

Neue Einsatznummern werden nun ausschließlich serverseitig und automatisch im Format `yyyy-MM-dd-HH-mm-ss` vergeben, beispielsweise `2026-08-30-10-51-42`. Maßgeblich ist die Zeitzone `Europe/Berlin`; eine im Browser übermittelte Nummer wird nicht akzeptiert. Existiert die berechnete Nummer bereits, sucht die API sekundenweise die nächste freie Nummer. Eine dennoch mögliche parallele Unique-Kollision wird als verständlicher HTTP-409-Konflikt behandelt. Das Formular zeigt lediglich eine schreibgeschützte Vorschau, und die Create-Antwort enthält die tatsächlich gespeicherte Nummer.

### Aktive Einsatzorte in der GIS-Lage

Offene, disponierte und in Bearbeitung befindliche Einsätze werden über das lokale Adressregister geokodiert und in einer eigenen schaltbaren OpenLayers-Ebene `Aktive Einsatzorte` dargestellt. Jeder Marker besteht aus einem farbigen Kreis und einer Beschriftung mit Einsatznummer und Stichwort. Offen wird rot, disponiert orange und in Bearbeitung blau dargestellt. Abgeschlossene und stornierte Einsätze erscheinen nicht. Beim ersten Öffnen passt sich der Kartenausschnitt an die vorhandenen aktiven Marker an; spätere WebSocket-/Datenaktualisierungen erneuern die Marker, ohne den danach manuell gewählten Ausschnitt zurückzusetzen. WMS- und andere externe Layer werden unterhalb der operativen Marker und editierbaren Fachobjekte einsortiert.

### Idempotentes Löschen von GIS-Objekten

Ein gelöschtes, aber in der OpenLayers-Select-Interaktion noch ausgewähltes Feature konnte optisch in deren separater Overlayebene stehen bleiben. Ein erneuter Löschversuch führte anschließend zu HTTP 404, obwohl die Datenbank bereits leer war. Der DELETE-Endpunkt ist deshalb idempotent und liefert auch für ein bereits fehlendes Feature HTTP 204. Im Frontend wird während des Requests der Button gesperrt, die kanonische Feature-ID verwendet, anschließend die Auswahl-Overlayebene geleert, das Feature aus der Vektorquelle entfernt, die Quelle als geändert markiert und die Karte synchron neu gerendert.

### Verifikation dieses Stands

- .NET-10-Publish im Docker-Build erfolgreich
- JavaScript-Syntax und Diff geprüft
- API-Readiness nach Containerneustart: HTTP 200
- Create-API verlangt keine vom Client vorgegebene Einsatznummer mehr
- Löschen einer bereits fehlenden GIS-GUID: HTTP 204
- GIS-FeatureCollection nach dem gemeldeten Löschvorgang leer; ausgeliefertes Frontend enthält Auswahlbereinigung und synchrones Redraw

## Fortführung: Navigation vom GIS-Einsatzmarker (30.08.2026)

Die Marker aktiver Einsätze dienen nun auch als direkter Einstieg in die Einsatzbearbeitung. Ein Klick auf den Kreis oder das zugehörige Label wechselt von der GIS-Lage in die Einsatzansicht und lädt dort den anhand seiner internen ID eindeutig zugeordneten Einsatz. Dabei wird die bereits ermittelte Kartenposition nicht erneut zur Übernahme angeboten.

Die Trefferprüfung ist auf die Ebene `Aktive Einsatzorte` beschränkt und verwendet eine zusätzliche Pixeltoleranz, damit insbesondere die Beschriftungen zuverlässig anklickbar sind. Über einem klickbaren Einsatzmarker zeigt ein Handzeiger die mögliche Navigation an. Editierbare GIS-Flächen und externe QGIS-/WMS-Layer bleiben von diesem Verhalten unberührt.

### Verifikation dieses Stands

- JavaScript-Syntax und Diff geprüft
- API-Image erfolgreich neu gebaut und Container neu gestartet
- API-Readiness nach Containerneustart: HTTP 200
- Ausgeliefertes `app.js` enthält den Click-Handler einschließlich Marker-ID und Treffertoleranz

## Fortführung: Laufzeitstatus und „Failed to fetch“-Diagnose (17.09.2026)

### Ziel und Bedienung

Die Anwendung besitzt jetzt eine Laufzeitstatus-Seite. Sie ist in der Hauptnavigation als **Laufzeitstatus** erreichbar und kann unabhängig von Keycloak direkt unter `/status` geöffnet werden. Beim direkten Aufruf wird keine Anmeldung gestartet; dadurch bleibt die Diagnose auch bei einer falschen oder ausgefallenen öffentlichen Keycloak-Adresse nutzbar. Die Ansicht prüft beim Öffnen, auf Knopfdruck und automatisch alle 30 Sekunden.

Normale Frontend-API-Aufrufe zeigen bei einem Netzwerkfehler nicht mehr nur `Failed to fetch`, sondern nennen den betroffenen API-Pfad und verweisen auf den Laufzeitstatus.

### Serverseitige Prüfungen

`src/Leitweb.Api/Diagnostics/RuntimeStatusService.cs` führt drei voneinander unabhängige Prüfungen mit einer Zeitgrenze von sechs Sekunden aus:

- PostgreSQL/PostGIS über `Database.CanConnectAsync`
- Keycloak über die interne OIDC-Metadatenadresse `Authentication:MetadataAddress`; ohne expliziten Wert wird die Adresse aus `Authentication:Authority` gebildet
- QGIS über WMS `GetCapabilities` an `Gis:QgisServerUrl`

`GET /health/status` liefert einen JSON-Bericht mit Gesamtzustand, Prüfzeitpunkt, Umgebung, Status je Dienst, geprüfter URL, Laufzeit, Fehlerdetail, Handlungshinweis sowie den konfigurierten öffentlichen Keycloak- und QGIS-Adressen. Der Endpunkt liefert absichtlich HTTP 200, auch wenn der Bericht `degraded` ist. So kann das Frontend alle Teilergebnisse anzeigen. Die bestehenden Endpunkte `/health/live` und `/health/ready` bleiben unverändert für Liveness und Readiness bestehen.

### Browserprüfungen

Das Frontend prüft zusätzlich aus Sicht des tatsächlich verwendeten Browsers:

- Leitweb über `/health/live`
- Keycloak über die öffentliche OIDC-Metadatenadresse
- QGIS über die öffentliche WMS-`GetCapabilities`-Adresse

Es erkennt und erläutert insbesondere öffentliche URLs mit `localhost` bei entferntem Zugriff, HTTP-Unterressourcen auf einer HTTPS-Seite, ungültige URLs, Timeouts sowie nicht unterscheidbare Netzwerk-/DNS-/TLS-/CORS-Fehler. Der Vergleich ist diagnostisch wichtig: Ist ein Dienst intern erreichbar, aber aus dem Browser nicht, liegt die Ursache üblicherweise bei `KEYCLOAK_PUBLIC_URL` beziehungsweise `QGIS_PUBLIC_URL`, DNS, Reverse Proxy, Portfreigabe, TLS oder CORS.

### Compose- und QGIS-Anpassungen

In `docker-compose.yml`, `compose.gis.yml` und `docker-compose.dockhand.yml` wartet die API bei Keycloak nur noch auf `service_started` statt `service_healthy`. Die API benötigt Keycloak nicht zum eigenen Start und kann daher `/status` bereits während eines langen Keycloak-Starts oder bei einem Identity-Ausfall ausliefern. Die Datenbank bleibt wegen Migration und Initialdaten weiterhin mit `service_healthy` eine harte Startabhängigkeit.

Das QGIS-Nginx-Gateway liefert für `/ows` jetzt `Access-Control-Allow-Origin: *`. Der OGC-Endpunkt war bereits ohne Authentifizierung öffentlich; der Header ermöglicht nun zusätzlich die Browserprüfung und browserbasierte WMS-/WFS-Abrufe ohne CORS-Blockade.

`/app-config.json` enthält ergänzend `qgisPublicUrl`. Es werden keine Passwörter oder Client-Secrets im Statusbericht oder in der Browserkonfiguration ausgegeben.

### Betrieb und Fehlersuche

Nach einem Pull muss das Leitweb-Image neu gebaut beziehungsweise in Dockhand mit Build neu ausgerollt werden. Für den getrennten GIS-Stack:

```sh
git pull
docker compose -f compose.gis.yml up --build -d
```

Anschließend `/status` aufrufen. Bei Zugriff von einem anderen Rechner dürfen öffentliche Adressen nicht `localhost` verwenden. Lokale `.env`-Dateien sind nicht Teil der versionierten Dokumentation; produktive Dockhand-Werte werden außerhalb von Git verwaltet.

### Dateien dieses Änderungspakets

- `src/Leitweb.Api/Diagnostics/RuntimeStatusService.cs`: interne Laufzeitprüfungen und Berichtstypen
- `src/Leitweb.Api/Program.cs`: DI-Registrierung, `/status`, `/health/status` und öffentliche QGIS-Konfiguration
- `src/Leitweb.Api/wwwroot/index.html`: Statusansicht und Navigation
- `src/Leitweb.Api/wwwroot/app.js`: Browserprüfungen, automatische Aktualisierung und verständlichere Fetch-Fehler
- `src/Leitweb.Api/wwwroot/app.css`: Statuskarten und responsive Darstellung
- `deploy/qgis-server/nginx.conf`: CORS für `/ows`
- alle drei Compose-Dateien: entkoppelter API-Start von Keycloak-Readiness
- `README.md` und diese Übergabe: Betriebs- und Diagnoseanleitung

### Verifikation und verbleibender Smoke-Test

- `node --check src/Leitweb.Api/wwwroot/app.js` erfolgreich
- `git diff --check` erfolgreich
- Ein lokaler .NET-Build war auf diesem Windows-Host nicht möglich, weil nur .NET 6 installiert ist, das Projekt aber SDK 10.0.111 verlangt.
- Ein lokaler Compose-/Container-Smoke-Test war nicht möglich, weil Docker auf diesem Host nicht installiert ist.
- Nach dem GitHub-Redeploy müssen deshalb `/status`, `/health/status`, die drei internen Prüfungen und die drei Browserprüfungen einmal in der Zielinstanz kontrolliert werden.
