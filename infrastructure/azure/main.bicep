// Azure Bicep template for AAR infrastructure
// Deploy with: az deployment group create -g <resource-group> -f main.bicep

@description('Environment name (dev, staging, prod)')
param environment string = 'dev'

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Unique suffix for resource names')
param uniqueSuffix string = uniqueString(resourceGroup().id)

// Naming convention
var baseName = 'aar-${environment}-${uniqueSuffix}'
var tags = {
  environment: environment
  application: 'AAR'
  managedBy: 'Bicep'
}

// Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: replace('aar${environment}${uniqueSuffix}', '-', '')
  location: location
  tags: tags
  sku: {
    name: environment == 'prod' ? 'Standard_GRS' : 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

// Blob Container for source code
resource blobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storageAccount.name}/default/source-code'
  properties: {
    publicAccess: 'None'
  }
}

// Queue for analysis jobs
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  name: '${storageAccount.name}/default'
}

resource analysisQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${storageAccount.name}/default/analysis-jobs'
  dependsOn: [queueService]
}

// Azure SQL Database
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: '${baseName}-sql'
  location: location
  tags: tags
  properties: {
    administratorLogin: 'aaradmin'
    administratorLoginPassword: 'REPLACE_WITH_SECURE_PASSWORD'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: 'aar-db'
  location: location
  tags: tags
  sku: {
    name: environment == 'prod' ? 'S1' : 'Basic'
    tier: environment == 'prod' ? 'Standard' : 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: environment == 'prod' ? 268435456000 : 2147483648
  }
}

// Allow Azure services firewall rule
resource sqlFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Azure OpenAI
resource openAi 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: '${baseName}-openai'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: '${baseName}-openai'
    publicNetworkAccess: 'Enabled'
  }
}

// GPT-4 deployment
resource gpt4Deployment 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = {
  parent: openAi
  name: 'gpt-4'
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4'
      version: '0613'
    }
  }
  sku: {
    name: 'Standard'
    capacity: 10
  }
}

// Container Apps Environment
resource containerAppEnv 'Microsoft.App/managedEnvironments@2023-11-02-preview' = {
  name: '${baseName}-env'
  location: location
  tags: tags
  properties: {
    zoneRedundant: environment == 'prod'
  }
}

// API Container App
resource apiContainerApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: '${baseName}-api'
  location: location
  tags: tags
  properties: {
    environmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
          allowedHeaders: ['*']
        }
      }
      secrets: [
        {
          name: 'sql-connection'
          value: 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=aar-db;User Id=aaradmin;Password=REPLACE_WITH_SECURE_PASSWORD;'
        }
        {
          name: 'storage-connection'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'openai-key'
          value: openAi.listKeys().key1
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: 'ghcr.io/your-org/aar-api:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: environment }
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'sql-connection' }
            { name: 'UseSqlServer', value: 'true' }
            { name: 'Azure__Storage__ConnectionString', secretRef: 'storage-connection' }
            { name: 'Azure__OpenAI__Endpoint', value: openAi.properties.endpoint }
            { name: 'Azure__OpenAI__ApiKey', secretRef: 'openai-key' }
            { name: 'Azure__OpenAI__DeploymentName', value: 'gpt-4' }
            { name: 'BlobStorage__Provider', value: 'Azure' }
            { name: 'QueueService__Provider', value: 'Azure' }
          ]
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: environment == 'prod' ? 2 : 1
        maxReplicas: environment == 'prod' ? 10 : 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
    }
  }
}

// Worker Container App
resource workerContainerApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: '${baseName}-worker'
  location: location
  tags: tags
  properties: {
    environmentId: containerAppEnv.id
    configuration: {
      secrets: [
        {
          name: 'sql-connection'
          value: 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=aar-db;User Id=aaradmin;Password=REPLACE_WITH_SECURE_PASSWORD;'
        }
        {
          name: 'storage-connection'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'openai-key'
          value: openAi.listKeys().key1
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: 'ghcr.io/your-org/aar-worker:latest'
          resources: {
            cpu: json('1')
            memory: '2Gi'
          }
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: environment }
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'sql-connection' }
            { name: 'UseSqlServer', value: 'true' }
            { name: 'Azure__Storage__ConnectionString', secretRef: 'storage-connection' }
            { name: 'Azure__OpenAI__Endpoint', value: openAi.properties.endpoint }
            { name: 'Azure__OpenAI__ApiKey', secretRef: 'openai-key' }
            { name: 'Azure__OpenAI__DeploymentName', value: 'gpt-4' }
            { name: 'BlobStorage__Provider', value: 'Azure' }
            { name: 'QueueService__Provider', value: 'Azure' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: environment == 'prod' ? 5 : 2
        rules: [
          {
            name: 'queue-scaling'
            azureQueue: {
              queueName: 'analysis-jobs'
              queueLength: 5
              auth: [
                {
                  secretRef: 'storage-connection'
                  triggerParameter: 'connection'
                }
              ]
            }
          }
        ]
      }
    }
  }
}

// Outputs
output apiUrl string = 'https://${apiContainerApp.properties.configuration.ingress.fqdn}'
output storageAccountName string = storageAccount.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output openAiEndpoint string = openAi.properties.endpoint
