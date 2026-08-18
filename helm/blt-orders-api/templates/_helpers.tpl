{{- define "blt-orders-api.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "blt-orders-api.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name (include "blt-orders-api.name" .) | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}

{{- define "blt-orders-api.labels" -}}
helm.sh/chart: {{ include "blt-orders-api.chart" . }}
app.kubernetes.io/name: {{ include "blt-orders-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/part-of: blt
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "blt-orders-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "blt-orders-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "blt-orders-api.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "blt-orders-api.dbSecretName" -}}
{{- if .Values.database.existingSecret }}
{{- .Values.database.existingSecret }}
{{- else }}
{{- printf "%s-db" (include "blt-orders-api.fullname" .) }}
{{- end }}
{{- end }}
