{{/*
Expand the name of the chart.
*/}}
{{- define "muxity.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "muxity.fullname" -}}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "muxity.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | trunc 63 | trimSuffix "-" }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/instance:   {{ .Release.Name }}
{{- end }}

{{/*
MongoDB connection string
*/}}
{{- define "muxity.mongoConnection" -}}
{{- if .Values.secrets.mongoConnectionString }}
{{- .Values.secrets.mongoConnectionString }}
{{- else }}
{{- printf "mongodb://%s-mongodb:27017" (include "muxity.fullname" .) }}
{{- end }}
{{- end }}

{{/*
RabbitMQ host
*/}}
{{- define "muxity.rabbitmqHost" -}}
{{- printf "%s-rabbitmq" (include "muxity.fullname" .) }}
{{- end }}
