@description('Azure region for all resources')
param location string

@description('Name prefix for resources')
param prefix string

@description('Container Apps environment ID')
param envId string

@description('Storage mount name configured on the environment')
param storageMountName string

@description('MongoDB connection string')
@secure()
param mongoConnectionString string

@description('MongoDB database name')
param mongoDatabaseName string

@description('RabbitMQ connection string (amqps://...)')
@secure()
param rabbitMqConnectionString string

@description('JWT signing key (min 32 chars)')
@secure()
param jwtSigningKey string

@description('JWT issuer')
param jwtIssuer string = 'https://muxity.example.com'

@description('Google OIDC client ID')
param googleClientId string = ''

@description('Microsoft OIDC client ID')
param microsoftClientId string = ''

@description('Container image tag')
param imageTag string = 'latest'

@description('GitHub Container Registry username (repo owner)')
param ghcrOwner string = 'rfsprog'

var storageMountPath = '/data/storage'
var registry = 'ghcr.io'

var commonEnv = [
  { name: 'MongoDB__ConnectionString', secretRef: 'mongo-connection' }
  { name: 'MongoDB__DatabaseName', value: mongoDatabaseName }
  { name: 'Storage__Provider', value: 'Local' }
  { name: 'Storage__Local__BasePath', value: storageMountPath }
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
]

var commonSecrets = [
  { name: 'mongo-connection', value: mongoConnectionString }
  { name: 'jwt-key', value: jwtSigningKey }
]

var commonVolumes = [
  {
    name: 'storage-vol'
    storageType: 'AzureFile'
    storageName: storageMountName
  }
]

var commonVolumeMounts = [
  {
    volumeName: 'storage-vol'
    mountPath: storageMountPath
  }
]

// ── API ──────────────────────────────────────────────────────────────────────
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  properties: {
    environmentId: envId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedHeaders: ['*']
          allowedMethods: ['*']
        }
      }
      secrets: union(commonSecrets, [
        { name: 'rabbitmq-connection', value: rabbitMqConnectionString }
      ])
    }
    template: {
      volumes: commonVolumes
      containers: [
        {
          name: 'api'
          image: '${registry}/${ghcrOwner}/muxity-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: union(commonEnv, [
            { name: 'Jwt__Key', secretRef: 'jwt-key' }
            { name: 'Jwt__Issuer', value: jwtIssuer }
            { name: 'RabbitMq__ConnectionString', secretRef: 'rabbitmq-connection' }
            { name: 'Oidc__Google__ClientId', value: googleClientId }
            { name: 'Oidc__Microsoft__ClientId', value: microsoftClientId }
          ])
          volumeMounts: commonVolumeMounts
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 5
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

// ── Streaming ────────────────────────────────────────────────────────────────
resource streamingApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-streaming'
  location: location
  properties: {
    environmentId: envId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedHeaders: ['*']
          allowedMethods: ['GET', 'HEAD', 'OPTIONS']
        }
      }
      secrets: commonSecrets
    }
    template: {
      volumes: commonVolumes
      containers: [
        {
          name: 'streaming'
          image: '${registry}/${ghcrOwner}/muxity-streaming:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: union(commonEnv, [
            { name: 'Jwt__Key', secretRef: 'jwt-key' }
            { name: 'Jwt__Issuer', value: jwtIssuer }
            { name: 'Cdn__Provider', value: 'Passthrough' }
          ])
          volumeMounts: commonVolumeMounts
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 10
        rules: [
          {
            name: 'http-scale'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// ── Transcoder ───────────────────────────────────────────────────────────────
resource transcoderApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-transcoder'
  location: location
  properties: {
    environmentId: envId
    configuration: {
      secrets: union(commonSecrets, [
        { name: 'rabbitmq-connection', value: rabbitMqConnectionString }
      ])
    }
    template: {
      volumes: commonVolumes
      containers: [
        {
          name: 'transcoder'
          image: '${registry}/${ghcrOwner}/muxity-transcoder:${imageTag}'
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: union(commonEnv, [
            { name: 'RabbitMq__ConnectionString', secretRef: 'rabbitmq-connection' }
            { name: 'Transcoder__HardwareAccel', value: 'Software' }
            { name: 'Transcoder__MaxParallelJobs', value: '1' }
          ])
          volumeMounts: commonVolumeMounts
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [
          {
            name: 'rabbitmq-scale'
            custom: {
              type: 'rabbitmq'
              metadata: {
                queueName: 'transcode_jobs'
                queueLength: '1'
              }
              auth: [
                {
                  secretRef: 'rabbitmq-connection'
                  triggerParameter: 'host'
                }
              ]
            }
          }
        ]
      }
    }
  }
}

output apiHostname string = apiApp.properties.configuration.ingress.fqdn
output streamingHostname string = streamingApp.properties.configuration.ingress.fqdn
