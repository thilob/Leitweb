# Leitweb-POC auf dem Rancher-K3s-Cluster

Dieser Rollout ist für Kubernetes 1.36 und den Traefik-Endpunkt
`192.168.213.200` vorbereitet. Er verwendet bewusst HTTP und ist nicht als
Produktionskonfiguration gedacht.

## Öffentliche Endpunkte

- Leitweb: `http://leitweb.192.168.213.200.sslip.io`
- Keycloak: `http://auth.192.168.213.200.sslip.io`
- QGIS WMS: `http://qgis.192.168.213.200.sslip.io/ows`

Alle drei Namen werden von `sslip.io` auf `192.168.213.200` aufgelöst. Falls
das lokale Netz externe DNS-Antworten filtert, müssen dieselben Namen lokal im
DNS oder in der Hosts-Datei auf `192.168.213.200` eingetragen werden.

## 1. Cluster in Rancher prüfen

In Rancher den Zielcluster öffnen und über **Kubectl Shell** ausführen:

```sh
kubectl version
kubectl get nodes -o wide
kubectl get ingressclass
kubectl get storageclass
kubectl -n kube-system get pods -l app.kubernetes.io/name=traefik
```

Erwartet werden Kubernetes 1.36, eine `traefik`-IngressClass und eine als
Standard markierte StorageClass. Bei K3s ist das normalerweise `local-path`.
Die Adresse, über die Traefik erreichbar ist, muss `192.168.213.200` sein.

## 2. Kubeconfig herunterladen

In Rancher beim Zielcluster **Download KubeConfig** wählen. Anschließend auf
dem Rechner mit diesem Repository den Kontext prüfen:

```powershell
$env:KUBECONFIG = 'C:\Pfad\zur\rancher-kubeconfig.yaml'
kubectl config current-context
kubectl get nodes
```

Vor dem nächsten Schritt sicherstellen, dass wirklich der POC-Cluster aktiv ist.

Die Kubeconfig muss den API-Server von diesem Rechner aus erreichen und einen
Benutzer beziehungsweise Token enthalten, der mindestens:

- Nodes, IngressClasses und StorageClasses lesen kann,
- den Namespace `leitweb` anlegen und verwalten kann,
- Secrets, ConfigMaps, Services, PVCs, Deployments, StatefulSets und Ingresses
  im Namespace `leitweb` verwalten kann,
- Pods, Events und Logs im Namespace `leitweb` lesen kann.

Für den ersten POC ist die von Rancher für einen Cluster-Administrator erzeugte
Kubeconfig am einfachsten. Später kann sie durch eine enger begrenzte
Service-Account-Konfiguration ersetzt werden. Die Datei enthält Zugangsdaten,
darf nicht ins Repository kopiert oder committed werden und sollte nach dem POC
widerrufen beziehungsweise gelöscht werden.

## 3. Namespace und Secret anlegen

Das Secret bleibt außerhalb von Git. Für den POC können in PowerShell drei
zufällige Werte erzeugt und direkt an Kubernetes übergeben werden:

```powershell
$alphabet = (48..57) + (65..90) + (97..122)
$postgresPassword = -join ($alphabet | Get-Random -Count 40 | ForEach-Object { [char]$_ })
$keycloakAdminPassword = -join ($alphabet | Get-Random -Count 40 | ForEach-Object { [char]$_ })
$keycloakClientSecret = -join ($alphabet | Get-Random -Count 48 | ForEach-Object { [char]$_ })

kubectl create namespace leitweb --dry-run=client -o yaml | kubectl apply -f -
kubectl -n leitweb create secret generic leitweb-secrets `
  --from-literal=postgres-password=$postgresPassword `
  --from-literal=keycloak-admin-password=$keycloakAdminPassword `
  --from-literal=keycloak-client-secret=$keycloakClientSecret `
  --dry-run=client -o yaml | kubectl apply -f -

Remove-Variable postgresPassword, keycloakAdminPassword, keycloakClientSecret
```

Alternativ kann das Secret in Rancher unter **Resources -> Secrets** angelegt
werden. Name und Schlüssel müssen exakt lauten:

- `leitweb-secrets`
- `postgres-password`
- `keycloak-admin-password`
- `keycloak-client-secret`

## 4. Chart prüfen und installieren

Aus dem Wurzelverzeichnis dieses Repositorys:

```powershell
helm lint deploy/kubernetes/helm/leitweb
helm template leitweb deploy/kubernetes/helm/leitweb `
  --namespace leitweb `
  --kube-version 1.36.0 `
  --values deploy/kubernetes/helm/leitweb/values-rancher-poc.yaml

helm upgrade --install leitweb deploy/kubernetes/helm/leitweb `
  --namespace leitweb `
  --values deploy/kubernetes/helm/leitweb/values-rancher-poc.yaml `
  --rollback-on-failure `
  --timeout 20m
```

Das Chart verwendet den Namen `leitweb` für seine Ressourcen. Das erleichtert
die folgenden Diagnosebefehle.

## 5. Rollout beobachten

```powershell
kubectl -n leitweb get pods,pvc,svc,ingress
kubectl -n leitweb rollout status statefulset/leitweb-postgres --timeout=10m
kubectl -n leitweb rollout status deployment/leitweb-keycloak --timeout=10m
kubectl -n leitweb rollout status deployment/leitweb-api --timeout=10m
kubectl -n leitweb rollout status deployment/leitweb-qgis-server --timeout=10m
kubectl -n leitweb rollout status deployment/leitweb-qgis-web --timeout=10m
kubectl -n leitweb logs deployment/leitweb-api -c migrate-database
```

Falls ein Pod nicht startet:

```powershell
kubectl -n leitweb get events --sort-by=.lastTimestamp
kubectl -n leitweb describe pod <POD-NAME>
kubectl -n leitweb logs <POD-NAME> --all-containers
```

## 6. POC testen

1. `http://leitweb.192.168.213.200.sslip.io/health/live`
2. `http://leitweb.192.168.213.200.sslip.io/health/ready`
3. `http://leitweb.192.168.213.200.sslip.io/status`
4. Anmeldung als `dispatcher` mit dem temporären Kennwort `change-me`
5. Kennwort beim ersten Login ändern
6. `http://qgis.192.168.213.200.sslip.io/ows?SERVICE=WMS&REQUEST=GetCapabilities`

## POC entfernen

```powershell
helm uninstall leitweb --namespace leitweb
```

Das PostgreSQL-PVC bleibt dabei absichtlich erhalten. Nur wenn alle POC-Daten
gelöscht werden dürfen:

```powershell
kubectl -n leitweb delete pvc leitweb-postgres
kubectl delete namespace leitweb
```
