# Azure Key Vault Migration Guide

This document describes how secrets are managed in the AAR application and provides instructions for local development, CI/CD, and production deployment.

## Table of Contents

1. [Overview](#overview)
2. [Secret Classification](#secret-classification)
3. [Local Development Setup](#local-development-setup)
4. [Production Setup](#production-setup)
5. [CI/CD Configuration](#cicd-configuration)
6. [Security Best Practices](#security-best-practices)
7. [Troubleshooting](#troubleshooting)

---

## Overview

AAR uses a **hybrid configuration approach**:

| Environment | Secret Source | Configuration Source |
|-------------|---------------|----------------------|
| Local Dev   | User Secrets / `secrets.local.json` | `appsettings.Development.json` |
| CI/CD       | GitHub Secrets (OIDC) | `appsettings.json` |
| Production  | Azure Key Vault (Managed Identity) | `appsettings.json` |

### Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                        Configuration Pipeline                     │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────┐    ┌─────────────────┐    ┌───────────────┐ │
│  │ appsettings.json│ -> │ Environment Vars│ -> │  Key Vault    │ │
│  │ (non-secrets)   │    │ (overrides)     │    │  (secrets)    │ │
│  └─────────────────┘    └─────────────────┘    └───────────────┘ │
│           │                      │                      │        │
│           v                      v                      v        │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │                    IConfiguration                          │  │
│  │   (Unified configuration - secrets override placeholders)  │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Secret Classification

Based on our security audit, configuration values are classified as:

### Secrets (Stored in Key Vault)

These values MUST come from Key Vault or user-secrets:

| Configuration Key | Key Vault Secret Name | Description |
|-------------------|----------------------|-------------|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings--DefaultConnection` | SQL Server connection string |
| `Azure:OpenAI:Endpoint` | `Azure--OpenAI--Endpoint` | Azure OpenAI endpoint URL |
| `Azure:OpenAI:ApiKey` | `Azure--OpenAI--ApiKey` | Azure OpenAI API key |
| `Azure:StorageConnectionString` | `Azure--StorageConnectionString` | Azure Blob Storage connection string |
| `Azure:Storage:ConnectionString` | `Azure--Storage--ConnectionString` | Alternative storage connection string |
| `Azure:Queue:ConnectionString` | `Azure--Queue--ConnectionString` | Service Bus connection string |
| `VectorStore:CosmosEndpoint` | `VectorStore--CosmosEndpoint` | Cosmos DB endpoint |
| `VectorStore:CosmosKey` | `VectorStore--CosmosKey` | Cosmos DB primary key |
| `Jwt:Secret` | `Jwt--Secret` | JWT signing key (min 32 chars) |
| `MassTransit:ServiceBusConnectionString` | `MassTransit--ServiceBusConnectionString` | Service Bus connection |
| `Embedding:ApiKey` | `Embedding--ApiKey` | Embedding service API key |

> **Key Vault Naming Convention**: Configuration keys use `:` as separator. Key Vault uses `--` as separator.
> Example: `Azure:OpenAI:ApiKey` → `Azure--OpenAI--ApiKey`

### Non-Secrets (Stay in appsettings.json)

These values are safe to commit:

- `Logging:*` - Log levels
- `AllowedHosts` - Host restrictions
- `UseSqlServer` - Boolean flags
- `Cors:AllowedOrigins` - CORS origins
- `Embedding:UseMock` - Feature flags
- `ModelRouter:*` - Model configuration
- `Cache:*` - Cache settings
- `Retrieval:*` - Retrieval settings
- `Chunker:*` - Chunker settings

---

## Local Development Setup

### Option 1: .NET User Secrets (Recommended)

User secrets are stored outside your project directory, making accidental commits impossible.

```powershell
# Navigate to solution root
cd C:\Projects\AAR

# Run the setup script
.\scripts\setup-dev-secrets.ps1 -Interactive

# Or, use a pre-filled template
Copy-Item secrets.local.json.template secrets.local.json
# Edit secrets.local.json with your values
.\scripts\setup-dev-secrets.ps1 -FromFile .\secrets.local.json
```

**Manual user-secrets setup:**

```powershell
# Initialize user-secrets for each project
dotnet user-secrets init --project src\AAR.Api\AAR.Api.csproj
dotnet user-secrets init --project src\AAR.Worker\AAR.Worker.csproj

# Set individual secrets
dotnet user-secrets set "Azure:OpenAI:ApiKey" "your-api-key" --project src\AAR.Api
dotnet user-secrets set "Azure:OpenAI:Endpoint" "https://your-openai.openai.azure.com/" --project src\AAR.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=AAR;..." --project src\AAR.Api

# View secrets
dotnet user-secrets list --project src\AAR.Api
```

### Option 2: secrets.local.json

For scenarios where user-secrets aren't practical:

1. Copy the template:
   ```powershell
   Copy-Item secrets.local.json.template secrets.local.json
   ```

2. Edit `secrets.local.json` with your values

3. Ensure it's gitignored (already configured in `.gitignore`)

### Configuration Priority

The configuration system loads values in this order (later overrides earlier):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. Environment variables
4. User secrets (Development only)
5. `secrets.local.json` (Development only)
6. Azure Key Vault (Production only)

---

## Production Setup

### Prerequisites

1. **Azure Key Vault** created in your Azure subscription
2. **Managed Identity** enabled on your App Service/Container Apps
3. **Key Vault Access Policy** granting the Managed Identity `Get` and `List` permissions

### Step 1: Create Azure Key Vault

```bash
# Create resource group
az group create --name rg-aar-prod --location eastus2

# Create Key Vault
az keyvault create \
  --name kv-aar-prod \
  --resource-group rg-aar-prod \
  --location eastus2 \
  --enable-rbac-authorization true
```

### Step 2: Enable Managed Identity

```bash
# Enable system-assigned managed identity on App Service
az webapp identity assign \
  --resource-group rg-aar-prod \
  --name aar-api-prod

# Get the principal ID
PRINCIPAL_ID=$(az webapp identity show \
  --resource-group rg-aar-prod \
  --name aar-api-prod \
  --query principalId -o tsv)
```

### Step 3: Grant Key Vault Access

```bash
# Get Key Vault resource ID
KV_ID=$(az keyvault show \
  --name kv-aar-prod \
  --resource-group rg-aar-prod \
  --query id -o tsv)

# Assign "Key Vault Secrets User" role
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee $PRINCIPAL_ID \
  --scope $KV_ID
```

### Step 4: Add Secrets to Key Vault

```bash
# Add secrets (note the -- separator)
az keyvault secret set \
  --vault-name kv-aar-prod \
  --name "ConnectionStrings--DefaultConnection" \
  --value "Server=tcp:aar-sql.database.windows.net..."

az keyvault secret set \
  --vault-name kv-aar-prod \
  --name "Azure--OpenAI--ApiKey" \
  --value "your-openai-api-key"

az keyvault secret set \
  --vault-name kv-aar-prod \
  --name "Azure--OpenAI--Endpoint" \
  --value "https://aar-openai.openai.azure.com/"

# Repeat for all secrets listed in the classification table
```

### Step 5: Configure Application

Set these app settings (NOT secrets) in your App Service:

```bash
az webapp config appsettings set \
  --resource-group rg-aar-prod \
  --name aar-api-prod \
  --settings \
    KeyVault__VaultUri="https://kv-aar-prod.vault.azure.net/" \
    KeyVault__UseKeyVault="true" \
    KeyVault__UseMockKeyVault="false" \
    ASPNETCORE_ENVIRONMENT="Production"
```

---

## CI/CD Configuration

### GitHub Actions with OIDC

We use **OpenID Connect (OIDC)** for secure, secretless authentication with Azure.

#### Step 1: Create Azure AD App Registration

```bash
# Create app registration
az ad app create --display-name "aar-github-actions"

# Get the app ID
APP_ID=$(az ad app list --display-name "aar-github-actions" --query "[0].appId" -o tsv)

# Create federated credential for GitHub
az ad app federated-credential create \
  --id $APP_ID \
  --parameters '{
    "name": "github-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:YOUR_ORG/AAR:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Create service principal
az ad sp create --id $APP_ID

# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Assign Contributor role
az role assignment create \
  --role "Contributor" \
  --assignee $APP_ID \
  --scope "/subscriptions/$SUBSCRIPTION_ID"
```

#### Step 2: Configure GitHub Secrets

Add these secrets to your GitHub repository:

| Secret Name | Value |
|-------------|-------|
| `AZURE_CLIENT_ID` | App registration client ID |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |

> **Note**: No service principal secret is needed! OIDC uses token exchange.

#### Step 3: Workflow Configuration

The CI/CD workflow (`.github/workflows/ci-cd.yml`) is already configured to:

1. Run secret scanning before build
2. Use OIDC authentication for Azure login
3. Deploy without passing secrets in workflows

---

## Security Best Practices

### Do's ✅

- **Use Managed Identity** for Azure resources (no credentials in code)
- **Use OIDC** for GitHub Actions (no long-lived secrets)
- **Rotate secrets** regularly using Key Vault's rotation feature
- **Enable soft delete** on Key Vault for recovery
- **Use RBAC** for Key Vault access (not access policies)
- **Log secret access** using Azure Monitor
- **Use separate Key Vaults** per environment (dev/staging/prod)

### Don'ts ❌

- **Never commit secrets** to git (use `.gitignore`)
- **Never log secrets** (use `SensitiveDataRedactor`)
- **Never pass secrets** in URLs or query strings
- **Never use shared secrets** across environments
- **Never store secrets** in appsettings.json

### Secret Rotation

To rotate a secret:

1. Add the new secret value to Key Vault with a new version
2. The application will automatically pick up the new value (if `ReloadOnChange: true`)
3. No application restart required

```bash
# Update a secret (creates new version)
az keyvault secret set \
  --vault-name kv-aar-prod \
  --name "Azure--OpenAI--ApiKey" \
  --value "new-api-key-value"
```

---

## Troubleshooting

### Application Fails to Start

**Error**: `KeyVaultConfigurationException: Unable to connect to Key Vault`

**Solutions**:
1. Verify Managed Identity is enabled: `az webapp identity show --resource-group rg-aar-prod --name aar-api-prod`
2. Verify Key Vault access: `az keyvault secret list --vault-name kv-aar-prod`
3. Check that `KeyVault:VaultUri` is set correctly
4. Ensure Managed Identity has "Key Vault Secrets User" role

### Secrets Not Loading

**Error**: Configuration values are null or empty

**Solutions**:
1. Verify secret names match expected format (`Azure--OpenAI--ApiKey`)
2. Check Key Vault contains the secret: `az keyvault secret show --vault-name kv-aar-prod --name "Azure--OpenAI--ApiKey"`
3. Ensure `KeyVault:UseKeyVault` is `true`

### Local Development Issues

**Error**: Secrets not available locally

**Solutions**:
1. Run `dotnet user-secrets list --project src\AAR.Api` to verify secrets are set
2. Ensure `KeyVault:UseMockKeyVault` is `true` in `appsettings.Development.json`
3. Check `secrets.local.json` exists and contains values

### CI/CD OIDC Errors

**Error**: `AADSTS700024: Client assertion is not within its valid time range`

**Solutions**:
1. Ensure GitHub repository name matches federated credential subject
2. Verify the workflow is running from the correct branch
3. Check Azure AD app registration has correct federated credentials

---

## Quick Reference

### Key Vault Secret Names

```
ConnectionStrings--DefaultConnection
Azure--OpenAI--ApiKey
Azure--OpenAI--Endpoint
Azure--StorageConnectionString
Azure--Storage--ConnectionString
Azure--Queue--ConnectionString
VectorStore--CosmosEndpoint
VectorStore--CosmosKey
Jwt--Secret
MassTransit--ServiceBusConnectionString
Embedding--ApiKey
```

### Useful Commands

```powershell
# List all secrets in Key Vault
az keyvault secret list --vault-name kv-aar-prod --output table

# View a specific secret
az keyvault secret show --vault-name kv-aar-prod --name "Azure--OpenAI--ApiKey"

# List local user secrets
dotnet user-secrets list --project src\AAR.Api

# Check for secrets in codebase
./scripts/check-no-secrets.sh
```

### Configuration Keys

```json
{
  "KeyVault": {
    "VaultUri": "https://kv-aar-prod.vault.azure.net/",
    "UseKeyVault": true,
    "UseMockKeyVault": false,
    "LocalSecretsPath": "secrets.local.json",
    "SecretPrefix": "",
    "TimeoutSeconds": 30,
    "ThrowOnUnavailable": false,
    "PreloadSecrets": [],
    "ReloadOnChange": true
  }
}
```

---

## Related Documentation

- [Azure Key Vault Documentation](https://docs.microsoft.com/en-us/azure/key-vault/)
- [.NET Secret Management](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [GitHub OIDC with Azure](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- [AAR Architecture](./ARCHITECTURE.md)
- [Secrets Scan Report](../REPORT_SECRETS_SCAN.md)
