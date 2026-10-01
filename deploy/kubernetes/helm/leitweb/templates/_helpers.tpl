{{- define "leitweb.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "leitweb.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name (include "leitweb.name" .) | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}

{{- define "leitweb.labels" -}}
app.kubernetes.io/name: {{ include "leitweb.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | quote }}
{{- end }}

{{- define "leitweb.selectorLabels" -}}
app.kubernetes.io/name: {{ include "leitweb.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

