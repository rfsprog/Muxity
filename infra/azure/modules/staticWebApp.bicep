@description('Azure region for all resources')
param location string

@description('Name prefix for resources')
param prefix string

@description('GitHub repository URL (e.g. https://github.com/rfsprog/Muxity)')
param repositoryUrl string

@description('GitHub branch to deploy from')
param branch string = 'main'

@description('GitHub personal access token for Static Web Apps')
@secure()
param repositoryToken string

resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' = {
  name: '${prefix}-web'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: repositoryUrl
    branch: branch
    repositoryToken: repositoryToken
    buildProperties: {
      appLocation: 'src/Muxity.Web'
      outputLocation: 'wwwroot'
      appBuildCommand: 'dotnet publish -c Release -o published'
    }
  }
}

output defaultHostname string = staticWebApp.properties.defaultHostname
output deploymentToken string = staticWebApp.listSecrets().properties.apiKey
