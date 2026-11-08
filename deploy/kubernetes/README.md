# Leitweb auf Kubernetes und Rancher

Dieses Verzeichnis bietet zwei gleichwertige Installationswege:

1. `helm/leitweb`: empfohlen für wiederholbare Installationen, Upgrades und Rollbacks.
2. `plain`: normale Kubernetes-YAML-Dateien für `kubectl apply` oder Ranchers **Import YAML**.

Helm ist keine technische Voraussetzung der Anwendung. Ein Helm-Chart ist eine
parametrisierte Vorlage, aus der Helm dieselben Kubernetes-Ressourcen erzeugt,
die unter `plain` bereits ausgeschrieben vorliegen. Nach der Installation läuft
kein Helm-Prozess im Cluster.

## Was installiert wird

| Ressource | Zweck | Warum sie benötigt wird |
| --- | --- | --- |
| PostgreSQL/PostGIS `StatefulSet` und PVC | Leitweb- und Keycloak-Daten sowie GIS-Geometrien | StatefulSet gibt der Datenbank eine stabile Identität; das PVC überlebt Pod-Neustarts. |
| Keycloak `Deployment` | Anmeldung, Rollen und Access-Tokens | Der Realm wird nur bei einer neuen, leeren Keycloak-Datenbank importiert. |
| Leitweb `Deployment` | API, Oberfläche und WebSocket-Endpunkt | Ein Init-Container führt vor jedem Pod-Start idempotente EF-Migrationen aus. Eine PostgreSQL-Advisory-Lock verhindert parallele Migrationen. |
| QGIS Server und Nginx `Deployments` | WMS/WFS und FastCGI-Gateway | QGIS spricht FastCGI; Nginx stellt daraus einen HTTP-Endpunkt mit CORS bereit. |
| `Services` | stabile interne DNS-Namen | Pods dürfen ersetzt werden, ohne dass sich die internen Adressen ändern. |
| `Ingress` | öffentliche HTTPS-Adressen | Führt Browserzugriffe zu Leitweb, Keycloak und QGIS und unterstützt langlebige WebSockets. |
| `ConfigMaps` | Realm, PostgreSQL-Initialisierung, Nginx-Konfiguration | Nicht geheime Konfiguration bleibt getrennt von Images und Zugangsdaten. |
| vorhandenes `Secret` | drei Passwörter/Secrets | Geheimnisse werden absichtlich nicht im Chart oder Git gespeichert. |

## Wichtige Grenzen dieses Standes

- `api.replicas` muss `1` bleiben. Live-Updates werden momentan im Speicher eines
  API-Prozesses verteilt. Das Helm-Chart lehnt höhere Werte ab. Für mehrere
  Replikate wird später ein Redis- oder PostgreSQL-Backplane benötigt.
- Das enthaltene PostgreSQL ist eine einzelne Instanz. Für hohe Verfügbarkeit
  sollte es durch einen verwalteten PostgreSQL/PostGIS-Dienst oder beispielsweise
  CloudNativePG ersetzt werden.
- Ein erneuter Realm-Import verändert keinen bereits vorhandenen Keycloak-Realm.
  Spätere Realm-Änderungen müssen über Keycloak-Administration oder einen
  gesonderten Realm-Migrationsprozess erfolgen.
- Datenbankmigrationen sind vorwärtsgerichtet. `helm rollback` setzt das
  Datenbankschema nicht zurück; vor produktiven Upgrades ist deshalb ein Backup
  erforderlich.

## 1. Voraussetzungen und unterstützte Versionen

Stand 8. November 2026 werden die drei von Kubernetes gepflegten Minor-Versionen
1.35, 1.36 und 1.37 unterstützt. Dieses Chart akzeptiert deshalb Kubernetes
`>=1.35.0` und `<1.38.0`; die CI rendert es gegen den aktuellen Patchstand 1.37.2.
Beim Aufbau eines neuen Clusters sollte immer der neueste Patchstand der
gewählten, noch unterstützten Minor-Version verwendet werden.

Die mitgelieferten Infrastruktur-Images sind PostgreSQL 18 mit PostGIS 3.7,
Keycloak 26.8.2 und Nginx Stable 1.30.6. Das PostgreSQL-Tag folgt den aktuellen
18.x-Patches. Die API verwendet .NET 10.0.14 LTS, EF Core 10.0.14,
Npgsql 10.0.3 und Swashbuckle 10.3.1. QGIS wird aus dem offiziellen
LTR-Repository gebaut, aktuell
3.44 LTR. Vorab- und RC-Versionen wie .NET/Npgsql 11 werden nicht eingesetzt.

- Kubernetes 1.35 bis 1.37 oder ein entsprechender RKE2/K3s/Rancher-Cluster
- Helm 4 für den empfohlenen Installationsweg (Helm 3 wird nur noch bis
  10. Februar 2027 mit Sicherheitsupdates versorgt)
- eine Standard-StorageClass oder eine explizite StorageClass wie Longhorn
- ein gepflegter Ingress-Controller; die Beispiele verwenden `traefik`
- DNS-Namen für Leitweb, Keycloak und QGIS
- ein TLS-Zertifikat, das alle drei Namen abdeckt
- eine vom Cluster erreichbare Container-Registry

`ingress-nginx` darf für neue Installationen nicht mehr verwendet werden: Das
Projekt wurde im März 2026 eingestellt und erhält keine Sicherheitsupdates.
K3s bringt Traefik standardmäßig mit; neue RKE2-Cluster verwenden ab RKE2 1.36
ebenfalls Traefik. Bei einem anderen Controller muss `ingress.className` auf
eine Ausgabe von `kubectl get ingressclass` gesetzt werden. Die beiden früheren
NGINX-spezifischen Timeout-Annotationen sind nicht nötig: WebSockets funktionieren
mit Traefik ohne diese Annotationen.

### Schnellstart: lauffähiger Einzelknoten mit K3s

Dieser Abschnitt ist nur für einen neuen Server gedacht. Bei einem bereits über
Rancher verwalteten K3s-Cluster wird K3s **nicht erneut installiert**; stattdessen
direkt mit dem folgenden Abschnitt fortfahren.

Für Entwicklung, Abnahme oder eine kleine Einzelknoteninstallation stellt K3s
Storage, Traefik und Kubernetes in einem Schritt bereit. Auf einem frischen
Linux-Server mit freien Ports 80 und 443:

```sh
# renovate: k3s
K3S_VERSION='v1.37.2+k3s1'
curl -sfL https://get.k3s.io | \
  INSTALL_K3S_VERSION="$K3S_VERSION" sh -
sudo k3s kubectl get nodes
sudo k3s kubectl get storageclass
sudo k3s kubectl get ingressclass
```

Danach entweder alle folgenden `kubectl`-Aufrufe als `sudo k3s kubectl`
ausführen oder die Kubeconfig für den normalen Benutzer einrichten. Vor dem
Öffnen ins Internet müssen Firewall, DNS, Backup und TLS eingerichtet sein.
Für hochverfügbare Produktion werden mindestens drei Serverknoten, ein externer
Load-Balancer und die dokumentierte HA-Konfiguration von K3s oder RKE2 benötigt;
ein Einzelknoten ist nicht hochverfügbar.

### Vorhandener K3s-Cluster mit Traefik und Rancher

Die mitgelieferten Werte passen zu dieser Infrastruktur. Vor dem Rollout in
Rancher beziehungsweise mit der aus Rancher heruntergeladenen Kubeconfig einmal
die tatsächlich vorhandenen Klassen prüfen:

```sh
kubectl get nodes
kubectl get ingressclass
kubectl get storageclass
kubectl -n kube-system get pods -l app.kubernetes.io/name=traefik
```

Erwartet werden eine IngressClass `traefik` und eine als Standard markierte
StorageClass, bei K3s üblicherweise `local-path`. `postgres.storageClassName`
bleibt dann leer und Kubernetes verwendet diese Standardklasse. Nur wenn
Longhorn bereits über Rancher installiert und als gewünschter Datenspeicher
konfiguriert ist, wird dort ausdrücklich `longhorn` eingetragen.

Für Leitweb, Keycloak und QGIS werden drei eigene DNS-Namen benötigt. Der bereits
für Rancher verwendete Hostname darf nicht wiederverwendet werden. Alle drei
Namen müssen auf die Adresse zeigen, unter der Traefik erreichbar ist.

Wenn im Rancher-Cluster bereits cert-manager mit einem passenden ClusterIssuer
läuft, kann er `leitweb-tls` erzeugen. Dazu im Produktionswertefile beispielsweise
setzen:

```yaml
ingress:
  className: traefik
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
  tls:
    enabled: true
    secretName: leitweb-tls
```

Der Issuer-Name ist clusterabhängig. Ohne cert-manager bleibt es beim manuellen
Anlegen des TLS-Secrets aus Abschnitt 3.

Die Beispiele verwenden:

- `https://leitweb.example.org`
- `https://auth.example.org`
- `https://qgis.example.org/ows`

Diese Namen müssen vor dem Rollout überall ersetzt werden.

## 2. Container-Images bauen

Kubernetes baut keine Images aus einem Git-Repository. Der Workflow
`.github/workflows/container-images.yml` baut deshalb API und QGIS auf Pull
Requests zur Kontrolle und veröffentlicht sie bei Änderungen auf `main`, bei
einem Git-Tag `v*` oder bei manueller Ausführung nach GHCR:

- `ghcr.io/thilob/leitweb-api:latest`
- `ghcr.io/thilob/leitweb-qgis:latest`
- zusätzlich `sha-<commit>` sowie bei einem Release-Tag beispielsweise
  `v2026.11.0` und `2026.11.0`

Die mitgelieferten Standardwerte verwenden `latest` mit `imagePullPolicy:
Always`, damit eine neue Testinstallation sofort das zuletzt veröffentlichte
Image lädt. Für Produktion muss in `values-production.yaml` ein unveränderlicher
Release- oder SHA-Tag eingetragen werden.

Die Produktionsvorlage ist auf den erfolgreich gebauten Stand
`sha-f8fa102` festgelegt. Beide GHCR-Pakete sind öffentlich und können von K3s
ohne Registry-Secret geladen werden. Bei einem späteren Release wird der Tag in
der lokalen `values-production.yaml` auf den nach erfolgreicher CI erzeugten
`sha-<commit>`-Tag aktualisiert.

Falls die Pakete später wieder privat oder in eine andere Registry verschoben
werden, ist das weiter unten beschriebene `registry-credentials`-Secret
erforderlich.

Alternativ lassen sich die beiden Images weiterhin selbst bauen und in eine
eigene Registry laden:

```sh
docker build -f Dockerfile -t registry.example.org/leitweb/leitweb-api:2026.11.0 .
docker build -f deploy/qgis-server/Dockerfile -t registry.example.org/leitweb/leitweb-qgis:2026.11.0 deploy/qgis-server
docker push registry.example.org/leitweb/leitweb-api:2026.11.0
docker push registry.example.org/leitweb/leitweb-qgis:2026.11.0
```

Das QGIS-Image enthält `projects/start.qgz`. Ein geändertes QGIS-Projekt erfordert
daher ein neues QGIS-Image. Für produktive Rollouts sollten unveränderliche Tags
oder Image-Digests statt `latest` verwendet werden.

Bei einer privaten Registry wird zusätzlich ein Pull-Secret benötigt:

```sh
kubectl -n leitweb create secret docker-registry registry-credentials \
  --docker-server=registry.example.org \
  --docker-username=BENUTZER \
  --docker-password=PASSWORT
```

Im Helm-Wertefile wird es dann eingetragen:

```yaml
imagePullSecrets:
  - name: registry-credentials
```

In den Plain-YAML-Dateien muss dasselbe `imagePullSecrets`-Feld in die Pod-Specs
eingefügt werden.

## 3. Namespace, Secrets und TLS vorbereiten

```sh
kubectl create namespace leitweb
kubectl -n leitweb create secret generic leitweb-secrets \
  --from-literal=postgres-password='LANGES-ZUFAELLIGES-PASSWORT' \
  --from-literal=keycloak-admin-password='ANDERES-LANGES-PASSWORT' \
  --from-literal=keycloak-client-secret='DRITTES-LANGES-SECRET'
kubectl -n leitweb create secret tls leitweb-tls \
  --cert=fullchain.pem \
  --key=privkey.pem
```

Das PostgreSQL-Passwort darf nach dem ersten Start nicht ohne koordinierte
Änderung in PostgreSQL ausgetauscht werden. Das Secret allein zu ändern genügt
nicht.

## 4A. Installation mit Helm

Zuerst `helm/leitweb/values-production.example.yaml` kopieren und mindestens
Registry, Image-Tags, drei öffentliche URLs, Hosts, TLS-Secret und StorageClass
anpassen. Das eigentliche Secret bleibt außerhalb des Wertefiles.

Vor der Installation zeigt `helm template`, welche normalen YAML-Ressourcen das
Chart erzeugen würde:

```sh
helm lint deploy/kubernetes/helm/leitweb
helm template leitweb deploy/kubernetes/helm/leitweb \
  --namespace leitweb \
  -f values-production.yaml > leitweb-rendered.yaml
```

Installation beziehungsweise idempotentes Upgrade:

```sh
helm upgrade --install leitweb deploy/kubernetes/helm/leitweb \
  --namespace leitweb \
  -f values-production.yaml \
  --rollback-on-failure \
  --timeout 20m
```

### Was Helm dabei bewirkt

- Beim **ersten Installieren** erzeugt Helm die ConfigMaps, Services, Workloads,
  das Ingress und – sofern kein vorhandener Claim angegeben wurde – das PVC.
- Bei einem **Upgrade** berechnet Helm die Differenz und aktualisiert nur
  geänderte Ressourcen. Neue Image-Tags lösen beispielsweise ein Rolling Update
  der API aus.
- `--rollback-on-failure` setzt Kubernetes-Ressourcen zurück, wenn Pods innerhalb des Timeouts
  nicht bereit werden. Datenbankmigrationen werden dadurch nicht rückgängig
  gemacht.
- Das PostgreSQL-PVC trägt `helm.sh/resource-policy: keep` und bleibt nach
  `helm uninstall leitweb -n leitweb` absichtlich bestehen.
- Die Chart-Prüfung verhindert mehr als eine API-Replik, solange die
  Live-Update-Verteilung nicht clusterfähig ist.

Das Chart wird immer dann erneut mit `helm upgrade --install` angewandt, wenn
Image-Versionen, URLs, Ressourcenlimits, Storage- oder Ingress-Einstellungen
geändert wurden. Es muss nicht bei normalen Pod-Neustarts erneut ausgeführt werden.

## 4B. Installation ohne Helm, auch über Rancher

Ja, Helm kann vollständig entfallen. Die Dateien in `plain` sind direkt
anwendbare Kubernetes-Manifeste.

1. `01-secret.example.yaml` nach `01-secret.yaml` kopieren und alle drei
   `CHANGE-ME`-Werte ersetzen.
2. In `02-keycloak-realm.yaml` die Leitweb-URL ersetzen.
3. In `10-stack.yaml` Registry/Image-Tags, drei Hosts beziehungsweise URLs,
   `storageClassName` (falls nötig), Ingress-Klasse und TLS-Secret anpassen.
4. Anschließend in dieser Reihenfolge anwenden:

```sh
kubectl apply -f deploy/kubernetes/plain/00-namespace.yaml
kubectl apply -f deploy/kubernetes/plain/01-secret.yaml
kubectl apply -f deploy/kubernetes/plain/02-keycloak-realm.yaml
kubectl apply -f deploy/kubernetes/plain/10-stack.yaml
```

In Rancher wird zuerst der Namespace `leitweb` angelegt. Danach unter dem
betreffenden Cluster über **Import YAML** nacheinander Secret, Realm-ConfigMap
und Stack importieren. Das Namespace-Feld ist in allen Ressourcen bereits gesetzt.

Plain YAML hat dieselben Fähigkeiten wie Helm, aber keine Werteabstraktion und
keine Release-Historie. Bei einem Upgrade werden Image-Tags direkt in
`10-stack.yaml` geändert und dieselbe Datei erneut angewandt. Ein Zurückrollen
bedeutet, die vorherige Git-Version der YAML-Datei erneut anzuwenden.

Das Secret kann in Rancher alternativ über **Resources → Secrets** angelegt
werden. Es muss exakt `leitweb-secrets` heißen und die Schlüssel
`postgres-password`, `keycloak-admin-password` und `keycloak-client-secret`
enthalten.

## 5. Rollout prüfen

```sh
kubectl -n leitweb get pods,pvc,svc,ingress
kubectl -n leitweb rollout status statefulset/leitweb-postgres --timeout=10m
kubectl -n leitweb rollout status deployment/leitweb-keycloak --timeout=10m
kubectl -n leitweb rollout status deployment/leitweb-api --timeout=10m
kubectl -n leitweb logs deployment/leitweb-api -c migrate-database
```

Beim Helm-Release heißen Ressourcen standardmäßig `leitweb-leitweb-*`; mit
`fullnameOverride: leitweb` entsprechen sie den Namen der Plain-Manifeste.

Danach prüfen:

1. `https://leitweb.example.org/health/live`
2. `https://leitweb.example.org/health/ready`
3. `https://leitweb.example.org/status`
4. Anmeldung als `dispatcher` mit dem temporären Kennwort `change-me`
5. sofortige Kennwortänderung
6. QGIS GetCapabilities unter
   `https://qgis.example.org/ows?SERVICE=WMS&REQUEST=GetCapabilities`

## Backup, Entfernen und Wiederherstellung

Vor jedem Upgrade:

```sh
kubectl -n leitweb exec statefulset/leitweb-postgres -- \
  pg_dumpall -U leitweb > leitweb-backup.sql
```

Wer bereits ein PVC mit PostgreSQL 16 beziehungsweise dem früher verwendeten
PostGIS-3.5-Image besitzt, darf nicht lediglich auf `18-3.7` umtaggen. Für
PostgreSQL 18 änderte sich außerdem der Container-Volume-Pfad von
`/var/lib/postgresql/data` auf `/var/lib/postgresql`. Für diesen Major- und
PostGIS-Sprung ist ein `pg_dump`/`pg_restore` in ein neu erzeugtes
PostgreSQL-18-PVC der sichere Weg. Diese Migrationswarnung betrifft neue, leere
Installationen nicht.

`helm uninstall` erhält das vom Chart erzeugte PVC. Bei Plain YAML darf
`kubectl delete -f 10-stack.yaml` nicht unbedacht verwendet werden, weil darin
auch das PVC steht. Das Löschen des PVC entfernt Leitweb- und Keycloak-Daten.

Für einen produktiven Betrieb sollte die Sicherung automatisiert, verschlüsselt
und regelmäßig durch eine Testwiederherstellung geprüft werden.

## Versionsprüfung und Updates

Vor dieser Überarbeitung gab es keine automatische Versionsprüfung. Die Datei
`/renovate.json` konfiguriert jetzt eine wöchentliche Prüfung am Montagmorgen.
Renovate erfasst Container-Images in Helm-Werten, Kubernetes-YAML,
Compose-Dateien und Dockerfiles sowie NuGet-Pakete und GitHub Actions. Die
Kubernetes-Version, gegen die das Chart in CI gerendert wird, ist ebenfalls als
Renovate-Abhängigkeit markiert. Neue Versionen erzeugen ein Dependency-Dashboard
und Update-PRs; Datenbank-Major-Upgrades sind absichtlich gesperrt.

Damit der Zeitplan tatsächlich läuft, muss für dieses Repository einmalig die
[Renovate GitHub App](https://github.com/apps/renovate) aktiviert werden. Ohne
App liest GitHub die Konfiguration nicht von selbst. Die Workflow-Datei
`.github/workflows/kubernetes-validate.yml` prüft jeden Kubernetes-PR mit
`helm lint`, rendert Standard- sowie Produktionswerte und validiert alle
gerenderten und ausgeschriebenen Manifeste mit Kubeconform gegen die aktuell
verfolgte Kubernetes-Version.

Updates werden nicht unbeaufsichtigt in einen laufenden Cluster installiert.
Nach erfolgreicher CI und Prüfung der Release-/Migrationshinweise wird der PR
zusammengeführt, ein Backup erstellt und anschließend erneut
`helm upgrade --install` ausgeführt. RKE2/K3s-Control-Plane- und Node-Upgrades
werden getrennt über Rancher beziehungsweise den System Upgrade Controller in
einem Wartungsfenster verwaltet; sie werden nicht durch dieses Anwendungs-Chart
verändert.

