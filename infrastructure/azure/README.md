# Azure Infrastructure Deployment

This directory contains Infrastructure as Code (IaC) templates for deploying AAR to Azure.

## Prerequisites

- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) (v2.50+)
- Azure subscription with appropriate permissions
- [Bicep CLI](https://docs.microsoft.com/azure/azure-resource-manager/bicep/install) (or use Azure CLI which includes Bicep)

## Quick Start

### 1. Login to Azure

```bash
az login
az account set --subscription "<subscription-id>"
```

### 2. Create Resource Group

```bash
# Development
az group create --name rg-aar-dev --location eastus

# Production
az group create --name rg-aar-prod --location eastus
```

### 3. Deploy Infrastructure

```bash
# Development deployment
az deployment group create \
  --resource-group rg-aar-dev \
  --template-file main.bicep \
  --parameters environment=dev

# Production deployment
az deployment group create \
  --resource-group rg-aar-prod \
  --template-file main.bicep \
  --parameters environment=prod
```

### 4. Get Deployment Outputs

```bash
az deployment group show \
  --resource-group rg-aar-dev \
  --name main \
  --query properties.outputs
```

## Resources Created

| Resource | Purpose | SKU (Dev) | SKU (Prod) |
|----------|---------|-----------|------------|
| Storage Account | Blob storage & queues | Standard_LRS | Standard_GRS |
| Azure SQL | Database | Basic | S1 |
| Azure OpenAI | AI analysis | S0 | S0 |
| Container Apps Environment | Compute | - | Zone redundant |
| API Container App | REST API | 0.5 CPU, 1GB | Auto-scale |
| Worker Container App | Background jobs | 1 CPU, 2GB | Auto-scale |

## Configuration

### Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `environment` | Environment name | `dev` |
| `location` | Azure region | Resource group location |
| `uniqueSuffix` | Unique identifier | Auto-generated |

### Secrets Management

⚠️ **Important**: Replace placeholder passwords before deploying!

```bash
# Update SQL admin password in main.bicep
# Or use Azure Key Vault for production secrets
```

## Cost Estimation

| Environment | Estimated Monthly Cost |
|-------------|------------------------|
| Development | ~$50-100 |
| Production | ~$200-500 |

*Costs vary based on usage and region.*

## Cleanup

```bash
# Delete all resources
az group delete --name rg-aar-dev --yes --no-wait
```

## Troubleshooting

### Common Issues

1. **Quota exceeded**: Request quota increase or deploy to different region
2. **Name conflicts**: Use unique suffix or check existing resources
3. **Permission errors**: Ensure you have Contributor role on subscription

### Useful Commands

```bash
# View deployment logs
az deployment group show --resource-group rg-aar-dev --name main

# View Container Apps logs
az containerapp logs show --name aar-dev-xxx-api --resource-group rg-aar-dev

# Check Container App status
az containerapp show --name aar-dev-xxx-api --resource-group rg-aar-dev
```
