{{- define "vaultwarden-kubernetes-secrets.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "vaultwarden-kubernetes-secrets.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "vaultwarden-kubernetes-secrets.serviceAccountName" -}}
{{- if .Values.serviceAccount.name -}}
{{- .Values.serviceAccount.name -}}
{{- else -}}
{{ include "vaultwarden-kubernetes-secrets.fullname" . }}
{{- end -}}
{{- end -}}

{{/*
Common labels
*/}}
{{- define "vaultwarden-kubernetes-secrets.labels" -}}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ include "vaultwarden-kubernetes-secrets.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{/*
Selector labels
*/}}
{{- define "vaultwarden-kubernetes-secrets.selectorLabels" -}}
app.kubernetes.io/name: {{ include "vaultwarden-kubernetes-secrets.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/*
API component name
*/}}
{{- define "vaultwarden-kubernetes-secrets.api.fullname" -}}
{{- printf "%s-api" (include "vaultwarden-kubernetes-secrets.fullname" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Dashboard component name
*/}}
{{- define "vaultwarden-kubernetes-secrets.dashboard.fullname" -}}
{{- printf "%s-dashboard" (include "vaultwarden-kubernetes-secrets.fullname" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
