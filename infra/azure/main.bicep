@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Short prefix used for all resource names (e.g. muxity-preview)')
param prefix string = 'muxity'

@description('GitHub repository URL')
param repositoryUrl string = 'https://github.com/rfsprog/Muxity'

@description('Branch to deploy Blazor WASM from')
param branch string = 'main'

@description('GitHub PAT for Static Web Apps (repo scope)')
@secure()
param repositoryToken string

@description('RabbitMQ connection string from CloudAMQP (amqps://...)')
@secure()
param rabbitMqConnectionString string

@description('JWT signing key — min 32 random characters')
@secure()
param jwtSigningKey string

@description('Google OAuth client ID (leave empty to disable)')
param googleClientId string = ''

@description('Microsoft OAuth client ID (leave empty to disable)')
param microsoftClientId string = ''

@description('Container image tag to deploy')
param imageTag string = 'latest'

@description('GitHub Container Registry owner (repo owner lowercase)')
param ghcrOwner string = 'rfsprog'

// ── Storage ──────────────────────────────────────────────────────────────────
module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    prefix: prefix
  }
}

// ── Cosmos DB (MongoDB API) ───────────────────────────────────────────────────
module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    location: location
    prefix: prefix
  }
}

// ── Container Apps environment ────────────────────────────────────────────────
module env 'modules/containerAppsEnv.bicep' = {
  name: 'containerAppsEnv'
  params: {
    location: location
    prefix: prefix
    storageAccountName: storage.outputs.storageAccountName
    fileShareName: storage.outputs.fileShareName
    storageAccountKey: storage.outputs.storageAccountKey
  }
}

// ── Container Apps ────────────────────────────────────────────────────────────
module apps 'modules/containerApps.bicep' = {
  name: 'containerApps'
  params: {
    location: location
    prefix: prefix
    envId: env.outputs.envId
    storageMountName: env.outputs.storageMountName
    mongoConnectionString: cosmos.outputs.connectionString
    mongoDatabaseName: cosmos.outputs.databaseName
    rabbitMqConnectionString: rabbitMqConnectionString
    jwtSigningKey: jwtSigningKey
    jwtIssuer: 'https://${apps.outputs.apiHostname}'
    googleClientId: googleClientId
    microsoftClientId: microsoftClientId
    imageTag: imageTag
    ghcrOwner: ghcrOwner
  }
}

// ── Static Web App (Blazor WASM) ──────────────────────────────────────────────
module web 'modules/staticWebApp.bicep' = {
  name: 'staticWebApp'
  params: {
    location: location
    prefix: prefix
    repositoryUrl: repositoryUrl
    branch: branch
    repositoryToken: repositoryToken
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output apiUrl string = 'https://${apps.outputs.apiHostname}'
output streamingUrl string = 'https://${apps.outputs.streamingHostname}'
output webUrl string = 'https://${web.outputs.defaultHostname}'
output staticWebAppDeploymentToken string = web.outputs.deploymentToken
