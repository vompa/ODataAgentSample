// Azure-Infrastruktur fuer ODataAgentSample (Resource-Group-Scope).
//
// Zweistufig, weil die Container App ein Image braucht, das erst nach dem Anlegen der Registry existiert:
//   1. deployApp=false  -> Log Analytics, Container Registry, Managed Identity (AcrPull), Container-Apps-Umgebung
//   2. Image bauen und in die Registry schieben
//   3. deployApp=true   -> Container App mit dem gebauten Image
// Der Workflow .github/workflows/deploy-azure.yml fuehrt diese Schritte aus.

targetScope = 'resourceGroup'

@description('Praefix fuer die Ressourcennamen.')
param name string = 'odata-agent'

@description('Region. Standard: die Region der Resource Group.')
param location string = resourceGroup().location

@description('Soll die Container App angelegt werden? Erst nach dem Push des Images auf true setzen.')
param deployApp bool = false

@description('Vollstaendiger Image-Name inkl. Tag, z. B. <registry>.azurecr.io/odata-agent-sample:<sha>. Pflicht bei deployApp=true.')
param image string = ''

@description('SHA-256 (64 Hex-Zeichen) des Reader-API-Keys. Erzeugbar mit: dotnet run --project src/ODataAgentSample -- hash-key <key>')
@secure()
param readerKeySha256 string = ''

@description('SHA-256 (64 Hex-Zeichen) des Writer-API-Keys. Ohne Wert gibt es keinen Writer-Zugang.')
@secure()
param writerKeySha256 string = ''

@description('Maximale Replikate. Muss 1 bleiben, solange SQLite im Container liegt: jede Replik haette sonst ihre eigene Datenbank.')
@minValue(1)
@maxValue(1)
param maxReplicas int = 1

@description('Anfragen pro Minute und Client (Rate Limit der Anwendung).')
param requestsPerMinute int = 60

var suffix = uniqueString(resourceGroup().id)
var registryName = 'acr${suffix}' // global eindeutig, nur Kleinbuchstaben und Ziffern
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var tags = { app: 'odata-agent-sample' }

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${name}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // Kostenbremse: hoechstens 0,5 GB pro Tag aufnehmen
    workspaceCapping: { dailyQuotaGb: json('0.5') }
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false // Zugriff nur ueber Entra ID / Managed Identity, kein gemeinsames Passwort
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${name}-id'
  location: location
  tags: tags
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, acrPullRoleId)
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${name}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = if (deployApp) {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [acrPull]
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
      secrets: concat(
        [{ name: 'reader-key-sha256', value: readerKeySha256 }],
        empty(writerKeySha256) ? [] : [{ name: 'writer-key-sha256', value: writerKeySha256 }]
      )
    }
    template: {
      containers: [
        {
          name: 'odata-agent-sample'
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(
            [
              { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
              { name: 'RateLimit__RequestsPerMinute', value: string(requestsPerMinute) }
              // Der Ingress ist in Container Apps der einzige Zugangsweg: Schema und Client-IP aus X-Forwarded-* uebernehmen
              { name: 'ForwardedHeaders__Enabled', value: 'true' }
              // Swagger-Oberflaeche und OpenAPI-Dokument (beschreiben nur die Schnittstelle, Daten bleiben hinter dem API-Key)
              { name: 'OpenApi__Enabled', value: 'true' }
              { name: 'Authentication__ApiKeys__0__Name', value: 'reader' }
              { name: 'Authentication__ApiKeys__0__KeySha256', secretRef: 'reader-key-sha256' }
              { name: 'Authentication__ApiKeys__0__Role', value: 'reader' }
            ],
            empty(writerKeySha256) ? [] : [
              { name: 'Authentication__ApiKeys__1__Name', value: 'writer' }
              { name: 'Authentication__ApiKeys__1__KeySha256', secretRef: 'writer-key-sha256' }
              { name: 'Authentication__ApiKeys__1__Role', value: 'writer' }
            ]
          )
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      // Scale-to-zero: ohne Anfragen entstehen praktisch keine Kosten. Die erste Anfrage danach dauert einige Sekunden.
      scale: { minReplicas: 0, maxReplicas: maxReplicas }
    }
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output appFqdn string = deployApp ? app!.properties.configuration.ingress.fqdn : ''
