# Secrets Scan Report

**Project:** AAR (Autonomous Architecture Reviewer)  
**Scan Date:** 2025-12-09  
**Scan Tool:** Manual + Automated Pattern Matching  

---

## Executive Summary

| Category | Count |
|----------|-------|
| Total Configuration Keys | 67 |
| **SECRET** (Must go to Key Vault) | 14 |
| **NON-SECRET** (Keep in appsettings) | 53 |
| Hardcoded Credentials Found | 1 |
| Environment Variables Requiring Secrets | 10 |

---

## 1. Configuration Keys Inventory

### 1.1 appsettings.json Files

| Key | SourceFile | SampleValue | Classification | Recommendation |
|-----|------------|-------------|----------------|----------------|
| `Logging:LogLevel:Default` | `AAR.Api/appsettings.json:3` | `"Information"` | NON-SECRET | Keep in appsettings |
| `Logging:LogLevel:Microsoft.AspNetCore` | `AAR.Api/appsettings.json:4` | `"Warning"` | NON-SECRET | Keep in appsettings |
| `AllowedHosts` | `AAR.Api/appsettings.json:8` | `"*"` | NON-SECRET | Keep in appsettings |
| `ConnectionStrings:DefaultConnection` | `AAR.Api/appsettings.json:10` | `"Server=localhost;Database=AAR;..."` | **SECRET** | Move to Key Vault - contains server credentials |
| `UseSqlServer` | `AAR.Api/appsettings.json:12` | `true` | NON-SECRET | Keep in appsettings |
| `MassTransit:RegisterConsumers` | `AAR.Api/appsettings.json:14` | `true` | NON-SECRET | Keep in appsettings |
| `Embedding:UseMock` | `AAR.Api/appsettings.json:17` | `true` | NON-SECRET | Keep in appsettings |
| `Storage:BasePath` | `AAR.Api/appsettings.json:20` | `""` | NON-SECRET | Keep in appsettings |
| `Azure:StorageConnectionString` | `AAR.Api/appsettings.json:23` | `""` | **SECRET** | Move to Key Vault - Azure storage credentials |
| `Azure:OpenAI:Endpoint` | `AAR.Api/appsettings.json:25` | `""` | **SECRET** | Move to Key Vault - Azure endpoint with auth |
| `Azure:OpenAI:ApiKey` | `AAR.Api/appsettings.json:26` | `""` | **SECRET** | Move to Key Vault - API key |
| `Azure:OpenAI:DeploymentName` | `AAR.Api/appsettings.json:27` | `"gpt-4"` | NON-SECRET | Keep in appsettings |
| `Cors:AllowedOrigins` | `AAR.Api/appsettings.json:30-33` | `["http://localhost:3000"]` | NON-SECRET | Keep in appsettings |
| `ConnectionStrings:DefaultConnection` | `AAR.Worker/appsettings.json:10` | `"Data Source=aar.db"` | **SECRET** | Move to Key Vault |
| `Azure:OpenAI:Endpoint` | `AAR.Worker/appsettings.json:15` | `""` | **SECRET** | Move to Key Vault |
| `Azure:OpenAI:ApiKey` | `AAR.Worker/appsettings.json:16` | `""` | **SECRET** | Move to Key Vault |
| `Azure:OpenAI:DeploymentName` | `AAR.Worker/appsettings.json:17` | `"gpt-4"` | NON-SECRET | Keep in appsettings |
| `Azure:Storage:ConnectionString` | `AAR.Worker/appsettings.json:20` | `""` | **SECRET** | Move to Key Vault |
| `Azure:Queue:ConnectionString` | `AAR.Worker/appsettings.json:23` | `""` | **SECRET** | Move to Key Vault |
| `BlobStorage:Provider` | `AAR.Worker/appsettings.json:26` | `"FileSystem"` | NON-SECRET | Keep in appsettings |
| `BlobStorage:BasePath` | `AAR.Worker/appsettings.json:27` | `"./storage"` | NON-SECRET | Keep in appsettings |
| `QueueService:Provider` | `AAR.Worker/appsettings.json:30` | `"InMemory"` | NON-SECRET | Keep in appsettings |
| `Tokenizer:Type` | `AAR.Worker/appsettings.json:33` | `"Tiktoken"` | NON-SECRET | Keep in appsettings |
| `Tokenizer:Model` | `AAR.Worker/appsettings.json:34` | `"gpt-4o"` | NON-SECRET | Keep in appsettings |
| `Chunker:MaxTokens` | `AAR.Worker/appsettings.json:37` | `1600` | NON-SECRET | Keep in appsettings |
| `Chunker:OverlapTokens` | `AAR.Worker/appsettings.json:38` | `200` | NON-SECRET | Keep in appsettings |
| `Chunker:MinTokens` | `AAR.Worker/appsettings.json:39` | `100` | NON-SECRET | Keep in appsettings |
| `Embedding:Provider` | `AAR.Worker/appsettings.json:42` | `"AzureOpenAI"` | NON-SECRET | Keep in appsettings |
| `Embedding:DeploymentName` | `AAR.Worker/appsettings.json:43` | `"text-embedding-3-small"` | NON-SECRET | Keep in appsettings |
| `Embedding:Dimensions` | `AAR.Worker/appsettings.json:44` | `1536` | NON-SECRET | Keep in appsettings |
| `Embedding:BatchSize` | `AAR.Worker/appsettings.json:45` | `100` | NON-SECRET | Keep in appsettings |
| `Embedding:UseMock` | `AAR.Worker/appsettings.json:46` | `true` | NON-SECRET | Keep in appsettings |
| `VectorStore:Provider` | `AAR.Worker/appsettings.json:49` | `"InMemory"` | NON-SECRET | Keep in appsettings |
| `VectorStore:CosmosEndpoint` | `AAR.Worker/appsettings.json:50` | `""` | **SECRET** | Move to Key Vault - Cosmos DB endpoint |
| `VectorStore:CosmosKey` | `AAR.Worker/appsettings.json:51` | `""` | **SECRET** | Move to Key Vault - Cosmos DB key |
| `VectorStore:CosmosDatabase` | `AAR.Worker/appsettings.json:52` | `"aar"` | NON-SECRET | Keep in appsettings |
| `VectorStore:CosmosContainer` | `AAR.Worker/appsettings.json:53` | `"chunks"` | NON-SECRET | Keep in appsettings |
| `Retrieval:MaxRetrievedChunks` | `AAR.Worker/appsettings.json:56` | `50` | NON-SECRET | Keep in appsettings |
| `Retrieval:TopK` | `AAR.Worker/appsettings.json:57` | `20` | NON-SECRET | Keep in appsettings |
| `Retrieval:SimilarityThreshold` | `AAR.Worker/appsettings.json:58` | `0.7` | NON-SECRET | Keep in appsettings |
| `Retrieval:MaxContextTokens` | `AAR.Worker/appsettings.json:59` | `12000` | NON-SECRET | Keep in appsettings |
| `ModelRouter:SmallModel` | `AAR.Worker/appsettings.json:63` | `"gpt-4o-mini"` | NON-SECRET | Keep in appsettings |
| `ModelRouter:LargeModel` | `AAR.Worker/appsettings.json:64` | `"gpt-4o"` | NON-SECRET | Keep in appsettings |
| `Cache:EnableChunkCache` | `AAR.Worker/appsettings.json:70` | `true` | NON-SECRET | Keep in appsettings |
| `Cache:EnableEmbeddingCache` | `AAR.Worker/appsettings.json:71` | `true` | NON-SECRET | Keep in appsettings |

### 1.2 Environment Variables (from .env.example)

| Key | SourceFile | SampleValue | Classification | Recommendation |
|-----|------------|-------------|----------------|----------------|
| `SQL_CONNECTION_STRING` | `.env.example:7` | `"Data Source=aar.db"` | **SECRET** | Store in Key Vault |
| `USE_SQL_SERVER` | `.env.example:10` | `false` | NON-SECRET | Keep in environment |
| `AZURE_OPENAI_ENDPOINT` | `.env.example:13` | `"https://your-resource.openai.azure.com"` | **SECRET** | Store in Key Vault |
| `AZURE_OPENAI_KEY` | `.env.example:14` | `"your-api-key-here"` | **SECRET** | Store in Key Vault |
| `AZURE_OPENAI_DEPLOYMENT` | `.env.example:15` | `"gpt-4"` | NON-SECRET | Keep in environment |
| `AZURE_OPENAI_USE_MOCK` | `.env.example:16` | `true` | NON-SECRET | Keep in environment |
| `AZURE_STORAGE_CONNECTION_STRING` | `.env.example:19` | `"DefaultEndpointsProtocol=..."` | **SECRET** | Store in Key Vault |
| `BLOB_STORAGE_PROVIDER` | `.env.example:22` | `"FileSystem"` | NON-SECRET | Keep in environment |
| `BLOB_STORAGE_BASE_PATH` | `.env.example:23` | `"./storage"` | NON-SECRET | Keep in environment |
| `QUEUE_SERVICE_PROVIDER` | `.env.example:26` | `"InMemory"` | NON-SECRET | Keep in environment |
| `API_KEY` | `.env.example:29` | `"your-api-key-for-authentication"` | **SECRET** | Store in Key Vault |
| `LOG_LEVEL` | `.env.example:32` | `"Information"` | NON-SECRET | Keep in environment |

### 1.3 Frontend Environment Variables

| Key | SourceFile | SampleValue | Classification | Recommendation |
|-----|------------|-------------|----------------|----------------|
| `VITE_API_BASE_URL` | `aar-frontend/.env.example:5` | `"http://localhost:5000"` | NON-SECRET | Keep in .env - public URL |
| `VITE_PUBLIC_PATH` | `aar-frontend/.env.example:8` | `"/"` | NON-SECRET | Keep in .env |
| `VITE_SIGNALR_HUB_URL` | `aar-frontend/.env.example:11` | `""` | NON-SECRET | Keep in .env |
| `VITE_MOCK_MODE` | `aar-frontend/.env.example:14` | `false` | NON-SECRET | Keep in .env |
| `VITE_SENTRY_DSN` | `aar-frontend/.env.example:17` | `""` | **SECRET** | Should be backend-only or use Sentry relay |
| `VITE_ANALYTICS_ID` | `aar-frontend/.env.example:20` | `""` | NON-SECRET | Keep in .env - public tracking ID |
| `VITE_APP_VERSION` | `aar-frontend/.env.example:23` | `"1.0.0"` | NON-SECRET | Keep in .env |
| `VITE_FEATURE_*` | `aar-frontend/.env.example:26-28` | `true` | NON-SECRET | Keep in .env |

---

## 2. IConfiguration / GetSection Usage

| Pattern | SourceFile:Line | Section/Key | Classification | Notes |
|---------|-----------------|-------------|----------------|-------|
| `GetConnectionString("DefaultConnection")` | `DependencyInjection.cs:43` | `ConnectionStrings:DefaultConnection` | **SECRET** | Database connection |
| `configuration["Azure:StorageConnectionString"]` | `DependencyInjection.cs:102` | `Azure:StorageConnectionString` | **SECRET** | Azure Storage |
| `configuration["Azure:OpenAI:Endpoint"]` | `DependencyInjection.cs:108` | `Azure:OpenAI:Endpoint` | **SECRET** | OpenAI endpoint |
| `configuration["Azure:OpenAI:ApiKey"]` | `DependencyInjection.cs:110` | `Azure:OpenAI:ApiKey` | **SECRET** | OpenAI API key |
| `configuration["Azure:OpenAI:DeploymentName"]` | `DependencyInjection.cs:112` | `Azure:OpenAI:DeploymentName` | NON-SECRET | Model name |
| `configuration.GetValue<bool>("UseSqlServer")` | `DependencyInjection.cs:49` | `UseSqlServer` | NON-SECRET | Feature flag |
| `configuration.GetValue<bool>("Embedding:UseMock")` | `DependencyInjection.cs:150` | `Embedding:UseMock` | NON-SECRET | Feature flag |
| `GetSection("ScaleLimits")` | `DependencyInjection.cs:85` | Options binding | NON-SECRET | Scale configuration |
| `GetSection("EmbeddingProcessing")` | `DependencyInjection.cs:86` | Options binding | NON-SECRET | Processing config |
| `GetSection("Tokenizer")` | `DependencyInjection.cs:141` | Options binding | NON-SECRET | Tokenizer config |
| `GetSection("Chunker")` | `DependencyInjection.cs:145` | Options binding | NON-SECRET | Chunker config |
| `GetSection("VectorStore")` | `DependencyInjection.cs:182` | Options binding | Contains **SECRETS** | CosmosEndpoint/Key |
| `GetSection(JwtOptions.SectionName)` | `JwtConfiguration.cs:110` | `Jwt` | Contains **SECRET** | SecretKey |
| `GetSection(AzureAdOptions.SectionName)` | `JwtConfiguration.cs:112` | `AzureAd` | Contains **SECRETS** | ClientId, ClientSecret |
| `GetSection(MassTransitOptions.SectionName)` | `MassTransitConfiguration.cs:70` | `MassTransit` | Contains **SECRET** | ServiceBus connection |

---

## 3. Environment.GetEnvironmentVariable Usage

| Variable | SourceFile:Line | Classification | Recommendation |
|----------|-----------------|----------------|----------------|
| `CONNECTION_STRING` | `DependencyInjection.cs:45` | **SECRET** | Load from Key Vault |
| `USE_SQL_SERVER` | `DependencyInjection.cs:50` | NON-SECRET | Keep as env var |
| `AZURE_STORAGE_CONNECTION_STRING` | `DependencyInjection.cs:103,119` | **SECRET** | Load from Key Vault |
| `AZURE_OPENAI_ENDPOINT` | `DependencyInjection.cs:109`, `AzureOpenAiService.cs:45`, `AzureOpenAiEmbeddingService.cs:40` | **SECRET** | Load from Key Vault |
| `AZURE_OPENAI_KEY` | `DependencyInjection.cs:111`, `AzureOpenAiService.cs:46`, `AzureOpenAiEmbeddingService.cs:41` | **SECRET** | Load from Key Vault |
| `AZURE_OPENAI_DEPLOYMENT` | `DependencyInjection.cs:113` | NON-SECRET | Keep as env var |
| `USE_MOCK_EMBEDDING` | `DependencyInjection.cs:151` | NON-SECRET | Keep as env var |
| `AZURE_SERVICEBUS_CONNECTION_STRING` | `MassTransitConfiguration.cs:73,122` | **SECRET** | Load from Key Vault |
| `MASSTRANSIT_REGISTER_CONSUMERS` | `MassTransitConfiguration.cs:191` | NON-SECRET | Keep as env var |
| `JWT_SECRET_KEY` | `JwtConfiguration.cs:115` | **SECRET** | Load from Key Vault |
| `KEYVAULT_URI` | `Program.cs:62`, `JwtConfiguration.cs:119` | NON-SECRET | Key Vault identifier (not secret itself) |
| `AAR_INTEGRATED_MODE` | `Program.cs:79` | NON-SECRET | Keep as env var |
| `ALLOWED_ORIGINS` | `Program.cs:190` | NON-SECRET | Keep as env var |
| `AV_SERVICE_ENDPOINT` | `VirusScanService.cs:123` | NON-SECRET | Service endpoint URL |

---

## 4. Hardcoded Credentials (CRITICAL)

| Finding | SourceFile:Line | Value | Severity | Remediation |
|---------|-----------------|-------|----------|-------------|
| Design-time connection string | `DesignTimeDbContextFactory.cs:19` | `"Server=localhost;Database=AAR;Integrated Security=True;..."` | **MEDIUM** | Acceptable for design-time migrations (uses Windows Auth, no password) |
| Test API key in E2E tests | `project-flow.spec.ts:21` | `"aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI"` | **LOW** | Test key only - add comment marking as test fixture |

**Note:** The design-time factory uses Windows Integrated Security (no password), which is acceptable for local development migrations.

---

## 5. Options Classes with Secret Properties

| Options Class | Property | SourceFile | Classification |
|---------------|----------|------------|----------------|
| `AzureOpenAiOptions` | `Endpoint` | `Services/AzureOpenAiService.cs` | **SECRET** |
| `AzureOpenAiOptions` | `ApiKey` | `Services/AzureOpenAiService.cs` | **SECRET** |
| `AzureOpenAiOptions` | `DeploymentName` | `Services/AzureOpenAiService.cs` | NON-SECRET |
| `AzureBlobStorageOptions` | `ConnectionString` | `Services/AzureBlobStorage.cs` | **SECRET** |
| `EmbeddingOptions` | `Endpoint` | `Interfaces/IEmbeddingService.cs` | **SECRET** |
| `EmbeddingOptions` | `ApiKey` | `Interfaces/IEmbeddingService.cs` | **SECRET** |
| `JwtOptions` | `SecretKey` | `Security/JwtConfiguration.cs` | **SECRET** |
| `AzureAdOptions` | `ClientId` | `Security/JwtConfiguration.cs` | **SECRET** |
| `AzureAdOptions` | `ClientSecret` | `Security/JwtConfiguration.cs` | **SECRET** |
| `MassTransitOptions` | `AzureServiceBusConnectionString` | `Messaging/MassTransitConfiguration.cs` | **SECRET** |
| `VectorStoreOptions` | `CosmosEndpoint` | `Services/VectorStore/CosmosVectorStore.cs` | **SECRET** |
| `VectorStoreOptions` | `CosmosKey` | `Services/VectorStore/CosmosVectorStore.cs` | **SECRET** |
| `CosmosOptions` | `ConnectionString` | `Services/VectorStore/CosmosVectorStore.cs` | **SECRET** |
| `CosmosOptions` | `Endpoint` | `Services/VectorStore/CosmosVectorStore.cs` | **SECRET** |
| `CosmosOptions` | `Key` | `Services/VectorStore/CosmosVectorStore.cs` | **SECRET** |

---

## 6. Secrets Summary - Key Vault Migration List

The following 14 secrets MUST be moved to Azure Key Vault:

| # | Current Key | Suggested Key Vault Secret Name | Current Location |
|---|-------------|--------------------------------|------------------|
| 1 | `ConnectionStrings:DefaultConnection` | `ConnectionStrings--DefaultConnection` | appsettings.json |
| 2 | `Azure:StorageConnectionString` | `Azure--StorageConnectionString` | appsettings.json |
| 3 | `Azure:OpenAI:Endpoint` | `Azure--OpenAI--Endpoint` | appsettings.json |
| 4 | `Azure:OpenAI:ApiKey` | `Azure--OpenAI--ApiKey` | appsettings.json |
| 5 | `Azure:Storage:ConnectionString` | `Azure--Storage--ConnectionString` | appsettings.json |
| 6 | `Azure:Queue:ConnectionString` | `Azure--Queue--ConnectionString` | appsettings.json |
| 7 | `VectorStore:CosmosEndpoint` | `VectorStore--CosmosEndpoint` | appsettings.json |
| 8 | `VectorStore:CosmosKey` | `VectorStore--CosmosKey` | appsettings.json |
| 9 | `Jwt:SecretKey` | `Jwt--SecretKey` | Environment / Config |
| 10 | `AzureAd:ClientId` | `AzureAd--ClientId` | Environment / Config |
| 11 | `AzureAd:ClientSecret` | `AzureAd--ClientSecret` | Environment / Config |
| 12 | `MassTransit:AzureServiceBusConnectionString` | `MassTransit--AzureServiceBusConnectionString` | Environment / Config |
| 13 | `Cosmos:ConnectionString` | `Cosmos--ConnectionString` | Environment / Config |
| 14 | `Cosmos:Key` | `Cosmos--Key` | Environment / Config |

**Note:** Azure Key Vault uses `--` as a delimiter instead of `:` for hierarchical keys.

---

## 7. Security Controls Already in Place

| Control | Status | Location |
|---------|--------|----------|
| Sensitive data redaction in logs | ✅ Implemented | `SensitiveDataRedactor.cs` |
| API key pattern redaction | ✅ Implemented | `SensitiveDataEnricher.cs` |
| Secret pattern detection (for analyzed code) | ✅ Implemented | `SecurityAgent.cs` |
| Key Vault TODO comments | ✅ Present | `Program.cs:62-67`, `JwtConfiguration.cs:119-125` |
| Empty placeholders for secrets in config | ✅ Done | All appsettings.json files |

---

## 8. Recommended Actions

### Immediate (P0)
1. ✅ No hardcoded production secrets found
2. Implement Azure Key Vault integration
3. Add CI secret scanning

### Short-term (P1)
1. Add `secrets.local.json` pattern for local development
2. Update GitHub Actions to use OIDC for Azure
3. Add secret scanning in pre-commit hooks

### Long-term (P2)
1. Rotate all secrets after Key Vault migration
2. Enable Key Vault audit logging
3. Implement secret versioning and rotation policy

---

## Appendix: Regex Patterns Used for Detection

```regex
# Connection strings
Server=.*Password=
AccountKey=
SharedAccessSignature=

# API Keys
[Aa]pi[_-]?[Kk]ey\s*[=:]\s*["'][^"']+["']
aar_[a-zA-Z0-9]{20,}

# Bearer tokens
Bearer\s+[a-zA-Z0-9-_.]+

# Azure patterns
DefaultEndpointsProtocol=
Endpoint=sb://
AccountName=.*AccountKey=

# JWT secrets
[Ss]ecret[_-]?[Kk]ey\s*[=:]\s*["'][^"']+["']

# Generic secrets
[Pp]assword\s*[=:]\s*["'][^"']+["']
[Ss]ecret\s*[=:]\s*["'][^"']+["']
[Tt]oken\s*[=:]\s*["'][^"']+["']
```
