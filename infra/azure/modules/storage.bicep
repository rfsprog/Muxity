@description('Azure region for all resources')
param location string

@description('Name prefix for resources')
param prefix string

var storageAccountName = '${replace(prefix, '-', '')}st'
var fileShareName = 'muxity-data'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  parent: fileService
  name: fileShareName
  properties: {
    shareQuota: 100
    enabledProtocols: 'SMB'
  }
}

output storageAccountName string = storageAccount.name
output fileShareName string = fileShareName
output storageAccountKey string = storageAccount.listKeys().keys[0].value
