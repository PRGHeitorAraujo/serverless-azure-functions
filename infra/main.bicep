// Infraestrutura do projeto Serverless no Azure
// Function App (Flex Consumption, .NET 8 isolated) + Storage + Service Bus + Azure SQL (serverless) + App Insights
//
// Deploy:
//   az group create -n rg-serverless-dio -l brazilsouth
//   az deployment group create -g rg-serverless-dio -f infra/main.bicep \
//     -p sqlAdminLogin=sqladmin sqlAdminPassword='<senha-forte>'

targetScope = 'resourceGroup'

@description('Prefixo curto usado no nome dos recursos (letras minúsculas e números).')
@maxLength(10)
param namePrefix string = 'dioserv'

@description('Região. Flex Consumption ainda não está em todas as regiões — veja o README.')
param location string = resourceGroup().location

@description('Login do administrador do Azure SQL.')
param sqlAdminLogin string

@secure()
@description('Senha do administrador do Azure SQL.')
param sqlAdminPassword string

@description('URL para onde as mensagens da fila serão encaminhadas.')
param webhookUrl string = ''

@description('Nome da fila do Service Bus.')
param queueName string = 'orders'

var suffix = uniqueString(resourceGroup().id)
var storageName = toLower('${namePrefix}st${suffix}')
var functionAppName = '${namePrefix}-func-${suffix}'
var deploymentContainerName = 'app-package'

// ---------- Observabilidade ----------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${namePrefix}-log-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-appi-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------- Storage (AzureWebJobsStorage, pacote de deploy e containers do Blob Trigger) ----------
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: take(storageName, 24)
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [
  for c in [deploymentContainerName, 'uploads', 'processed']: {
    parent: blobService
    name: c
  }
]

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'

// ---------- Service Bus ----------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: '${namePrefix}-sb-${suffix}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: serviceBus
  name: queueName
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    deadLetteringOnMessageExpiration: true
  }
}

resource sbAuthRule 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2021-11-01' existing = {
  parent: serviceBus
  name: 'RootManageSharedAccessKey'
}

// ---------- Azure SQL (serverless, com auto-pause para economizar) ----------
resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: '${namePrefix}-sql-${suffix}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: 'ServerlessDb'
  location: location
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    // Use a oferta gratuita do Azure SQL se disponível na sua assinatura:
    // useFreeLimit: true, freeLimitExhaustionBehavior: 'AutoPause'
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDb.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

// ---------- Function App (Flex Consumption) ----------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-plan-${suffix}'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'StorageConnection', value: storageConnectionString }
        { name: 'TimerSchedule', value: '0 */5 * * * *' }
        { name: 'SqlConnectionString', value: sqlConnectionString }
        { name: 'ToDoBaseUrl', value: 'https://${functionAppName}.azurewebsites.net/api/todos' }
        { name: 'ServiceBusConnection', value: sbAuthRule.listKeys().primaryConnectionString }
        { name: 'ServiceBusQueueName', value: queue.name }
        { name: 'WebhookUrl', value: webhookUrl }
      ]
    }
  }
}

// A identidade da Function App precisa ler o pacote de deploy no container.
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource deploymentStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, storageBlobDataOwnerRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storage.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output serviceBusNamespace string = serviceBus.name
