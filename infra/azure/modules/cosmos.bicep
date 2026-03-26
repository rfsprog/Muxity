@description('Azure region for all resources')
param location string

@description('Name prefix for resources')
param prefix string

var accountName = '${prefix}-cosmos'
var databaseName = 'muxity'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-02-15-preview' = {
  name: accountName
  location: location
  kind: 'MongoDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: true
    apiProperties: {
      serverVersion: '7.0'
    }
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      { name: 'EnableMongo' }
      { name: 'DisableRateLimitingResponses' }
    ]
  }
}

resource mongoDatabase 'Microsoft.DocumentDB/databaseAccounts/mongodbDatabases@2024-02-15-preview' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
    options: {
      throughput: 1000
    }
  }
}

output connectionString string = cosmosAccount.listConnectionStrings().connectionStrings[0].connectionString
output databaseName string = databaseName
