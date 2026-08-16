// Infraestrutura do Rivo, por ambiente.
//
// Um só ficheiro, deliberadamente. Repartir em módulos vale a pena quando há
// composições diferentes a partilhar peças; aqui há um ambiente com nove
// recursos, e a indirecção custaria mais do que poupa. Repartir quando
// produção divergir de staging ao ponto de doer.
//
// Nada aqui é criado à mão no portal (Fase 1 do percurso de execução). O que
// existir e não estiver neste ficheiro é deriva, e vai desaparecer no próximo
// deployment.

targetScope = 'resourceGroup'

@description('Nome do ambiente. Entra no nome de todos os recursos.')
@allowed(['dev', 'staging', 'prod'])
param environment string

@description('Região. Por omissão, a do grupo de recursos.')
param location string = resourceGroup().location

@description('Utilizador administrador do PostgreSQL.')
param postgresAdminUser string = 'rivo'

@description('Password do administrador do PostgreSQL. Nunca em ficheiro — vem do pipeline.')
@secure()
param postgresAdminPassword string

@description('Chave de assinatura do JWT. Mínimo 32 caracteres (ADR-013).')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Imagem do contentor a correr. O pipeline substitui-a a cada deployment; o valor por omissão só serve para o primeiro provisionamento, quando o registo ainda está vazio.')
param containerImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'


// Sufixo determinístico. O nome de uma conta de armazenamento tem de ser único
// à escala global e só aceita minúsculas e dígitos — derivar do id do grupo de
// recursos dá um nome estável entre deployments e improvável de colidir.
var suffix = uniqueString(resourceGroup().id)
var prefix = 'rivo-${environment}'

// --- Observabilidade ---------------------------------------------------------
// Primeiro na ordem porque tudo o resto lhe aponta. Ligada desde o primeiro
// deployment, e não depois do primeiro incidente.

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${prefix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    // 30 dias chega para diagnóstico em staging. A retenção longa que a
    // auditoria exige (BR-11, 10 anos) é da base de dados, não dos logs.
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${prefix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// --- Registo de imagens ------------------------------------------------------

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: 'acr${environment}${suffix}'
  location: location
  sku: { name: 'Basic' }
  properties: {
    // A autenticação é por identidade gerida, não por utilizador e password.
    // Deixar o utilizador administrativo activo seria uma credencial partilhada
    // a mais, exactamente o que o Key Vault existe para evitar.
    adminUserEnabled: false
  }
}

// --- Base de dados -----------------------------------------------------------

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-${prefix}-${suffix}'
  location: location
  sku: {
    // Burstable é o suficiente para staging e é o escalão mais barato que
    // suporta a versão 17. Produção precisa de reavaliação, não de cópia.
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    // A mesma versão maior do `docker-compose.yml` e dos testes de integração
    // (ADR-021, ADR-026). Testar contra uma versão e correr noutra é uma classe
    // de defeitos que só aparece em produção.
    version: '17'
    administratorLogin: postgresAdminUser
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      // RPO ≤24h é requisito absorvido do SGAP; 7 dias de retenção dá margem.
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }

  resource database 'databases@2024-08-01' = {
    name: 'rivo'
  }

  // Acesso a partir de serviços Azure. É deliberadamente grosseiro e é dívida
  // conhecida: a resposta certa é integração em rede virtual com endpoint
  // privado, que fecha a base de dados à Internet por completo. Fica para
  // quando produção existir — em staging, o custo da VNet não se justifica.
  resource allowAzure 'firewallRules@2024-08-01' = {
    name: 'AllowAzureServices'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

// --- Armazenamento de documentos ---------------------------------------------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${environment}${suffix}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    // Fecha o K11: `standards/security.md` exige cifra em repouso para anexos,
    // e o armazenamento em sistema de ficheiros guardava-os em claro. Aqui a
    // cifra é do serviço e não exige gestão de chaves na aplicação — que era
    // precisamente a razão pela qual o K11 continuava aberto.
    encryption: {
      services: {
        blob: { enabled: true }
      }
      keySource: 'Microsoft.Storage'
    }
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }

  resource blob 'blobServices@2023-05-01' = {
    name: 'default'

    resource documents 'containers@2023-05-01' = {
      name: 'documents'
      properties: { publicAccess: 'None' }
    }
  }
}

// --- Segredos ----------------------------------------------------------------

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${environment}-${take(suffix, 12)}'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    // RBAC em vez de políticas de acesso: as políticas são um segundo modelo de
    // permissões a manter em paralelo com o do Azure, e divergem.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }

  resource jwtKey 'secrets@2023-07-01' = {
    name: 'jwt-signing-key'
    properties: { value: jwtSigningKey }
  }

  resource connectionString 'secrets@2023-07-01' = {
    name: 'connection-string-rivo'
    properties: {
      value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=rivo;Username=${postgresAdminUser};Password=${postgresAdminPassword};SslMode=Require'
    }
  }
}

// A aplicação lê segredos; não os escreve nem os apaga. `Key Vault Secrets
// User` é exactamente isso — dar `Secrets Officer` seria dar-lhe poder para
// destruir a chave de assinatura.
var secretsUserRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

// A atribuição em si está mais abaixo, junto ao site: a identidade que lê os
// segredos é a do próprio App Service, criada neste mesmo template.

// --- Ambiente de execução ----------------------------------------------------
//
// App Service e não Container Apps (ADR-027). A subscrição permite um só
// ambiente de Container Apps e ele já está ocupado por outro projecto — é
// limite de quota, não escolha de arquitectura. O ADR regista o que se perde e
// quando reabrir.

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${prefix}'
  location: location
  sku: {
    // B1 é o escalão mais barato que suporta contentores Linux e `alwaysOn` —
    // e `alwaysOn` não é opcional aqui, ver abaixo.
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${prefix}-${take(suffix, 8)}'
  location: location
  kind: 'app,linux,container'
  identity: {
    // Identidade atribuída pelo sistema: é ela que lê o Key Vault e puxa a
    // imagem do registo. Nenhuma credencial em configuração.
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|${containerImage}'
      // Sem isto, a aplicação é descarregada quando fica ociosa — e com ela o
      // worker de entrega de notificações, que é um BackgroundService no mesmo
      // processo. A fila deixaria de ser drenada até alguém fazer um pedido.
      alwaysOn: true
      // O registo é acedido pela identidade do site, não por utilizador e
      // password — que é a razão de `adminUserEnabled` estar desligado no ACR.
      acrUseManagedIdentityCreds: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: environment == 'prod' ? 'Production' : 'Staging' }
        { name: 'WEBSITES_PORT', value: '8080' }
        { name: 'DOCKER_REGISTRY_SERVER_URL', value: 'https://${registry.properties.loginServer}' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }

        // Referências ao Key Vault: o valor nunca passa pelo template nem fica
        // legível na configuração do site. É resolvido em runtime pela
        // identidade do site.
        {
          name: 'ConnectionStrings__Rivo'
          value: '@Microsoft.KeyVault(SecretUri=${vault::connectionString.properties.secretUri})'
        }
        {
          name: 'Jwt__SigningKey'
          value: '@Microsoft.KeyVault(SecretUri=${vault::jwtKey.properties.secretUri})'
        }
        { name: 'Jwt__Issuer', value: 'rivo-api' }
        { name: 'Jwt__Audience', value: 'rivo-client' }
        { name: 'Jwt__SessionLifetimeMinutes', value: '60' }

        { name: 'DocumentStorage__AccountName', value: storage.name }
        { name: 'DocumentStorage__Container', value: 'documents' }
      ]
    }
  }
}

// Puxar imagens do registo. `AcrPull` e mais nada — o site consome imagens,
// não as publica; quem publica é o pipeline.
var acrPullRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
)

resource registryAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, api.id, acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: acrPullRole
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Escrever e ler blobs de documentos. `Storage Blob Data Contributor`: a
// aplicação cria e lê anexos, mas não gere a conta de armazenamento.
var blobContributorRole = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
)

resource storageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, api.id, blobContributorRole)
  scope: storage
  properties: {
    roleDefinitionId: blobContributorRole
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Ler segredos. A identidade do site substitui o parâmetro
// `apiIdentityPrincipalId`, que existia para o caso de a identidade ser criada
// fora deste template.
resource siteVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, api.id, secretsUserRole)
  scope: vault
  properties: {
    roleDefinitionId: secretsUserRole
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// --- Saídas ------------------------------------------------------------------
// O que o pipeline precisa de saber. Nenhum segredo sai daqui — só nomes e
// identificadores, que não são sensíveis.

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output apiName string = api.name
output apiUrl string = 'https://${api.properties.defaultHostName}'
output apiPrincipalId string = api.identity.principalId
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output vaultName string = vault.name
output storageAccountName string = storage.name
output insightsConnectionString string = insights.properties.ConnectionString
