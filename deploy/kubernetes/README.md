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

## 1. Voraussetzungen

- Kubernetes ab Version 1.28 oder ein entsprechender RKE2/K3s/Rancher-Cluster
- eine Standard-StorageClass oder eine explizite StorageClass wie Longhorn
- ein Ingress-Controller, üblicherweise `ingress-nginx`
- DNS-Namen für Leitweb, Keycloak und QGIS
- ein TLS-Zertifikat, das alle drei Namen abdeckt
- eine vom Cluster erreichbare Container-Registry

Die Beispiele verwenden:

- `https://leitweb.example.org`
- `https://auth.example.org`
- `https://qgis.example.org/ows`

Diese Namen müssen vor dem Rollout überall ersetzt werden.

## 2. Container-Images bauen

Kubernetes baut keine Images aus einem Git-Repository. Die beiden Images müssen
deshalb zuerst in eine Registry geladen werden:

```sh
docker build -f Dockerfile -t registry.example.org/leitweb/leitweb-api:2026.10.1 .
docker build -f deploy/qgis-server/Dockerfile -t registry.example.org/leitweb/leitweb-qgis:2026.10.1 deploy/qgis-server
docker push registry.example.org/leitweb/leitweb-api:2026.10.1
docker push registry.example.org/leitweb/leitweb-qgis:2026.10.1
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
  --atomic \
  --timeout 20m
```

### Was Helm dabei bewirkt

- Beim **ersten Installieren** erzeugt Helm die ConfigMaps, Services, Workloads,
  das Ingress und – sofern kein vorhandener Claim angegeben wurde – das PVC.
- Bei einem **Upgrade** berechnet Helm die Differenz und aktualisiert nur
  geänderte Ressourcen. Neue Image-Tags lösen beispielsweise ein Rolling Update
  der API aus.
- `--atomic` setzt Kubernetes-Ressourcen zurück, wenn Pods innerhalb des Timeouts
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

`helm uninstall` erhält das vom Chart erzeugte PVC. Bei Plain YAML darf
`kubectl delete -f 10-stack.yaml` nicht unbedacht verwendet werden, weil darin
auch das PVC steht. Das Löschen des PVC entfernt Leitweb- und Keycloak-Daten.

Für einen produktiven Betrieb sollte die Sicherung automatisiert, verschlüsselt
und regelmäßig durch eine Testwiederherstellung geprüft werden.

