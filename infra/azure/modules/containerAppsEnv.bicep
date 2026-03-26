@description('Azure region for all resources')
param location string

@description('Name prefix for resources')
param prefix string

@description('Azure Files storage account name')
param storageAccountName string

@description('Azure Files share name')
param fileShareName string

@description('Azure Files storage account key')
@secure()
param storageAccountKey string

var envName = '${prefix}-env'
var storageMountName = 'muxity-storage'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource envStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: containerAppsEnv
  name: storageMountName
  properties: {
    azureFile: {
      accountName: storageAccountName
      accountKey: storageAccountKey
      shareName: fileShareName
      accessMode: 'ReadWrite'
    }
  }
}

output envId string = containerAppsEnv.id
output envName string = containerAppsEnv.name
output storageMountName string = storageMountName
