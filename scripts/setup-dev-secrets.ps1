<# 
.SYNOPSIS
    Sets up development secrets for the AAR application using .NET user-secrets.

.DESCRIPTION
    This script initializes user-secrets for both AAR.Api and AAR.Worker projects
    and optionally populates them with values from a secrets.local.json file.

.EXAMPLE
    .\setup-dev-secrets.ps1
    
.EXAMPLE
    .\setup-dev-secrets.ps1 -FromFile .\secrets.local.json

.NOTES
    Author: AAR Team
    Requires: .NET SDK 10.0+
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$FromFile = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$Interactive
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================="
Write-Host " AAR Development Secrets Setup"
Write-Host "=========================================="
Write-Host ""

# Navigate to solution root
$solutionRoot = Split-Path -Parent $PSScriptRoot
Set-Location $solutionRoot

# Projects that need user-secrets
$projects = @(
    "src\AAR.Api\AAR.Api.csproj",
    "src\AAR.Worker\AAR.Worker.csproj"
)

# Initialize user-secrets for each project
foreach ($project in $projects) {
    $projectPath = Join-Path $solutionRoot $project
    
    if (Test-Path $projectPath) {
        Write-Host "Initializing user-secrets for $project..."
        
        # Check if UserSecretsId already exists
        $csprojContent = Get-Content $projectPath -Raw
        if ($csprojContent -notmatch "UserSecretsId") {
            # Initialize user-secrets (this adds UserSecretsId to csproj)
            dotnet user-secrets init --project $projectPath
        } else {
            Write-Host "  User-secrets already initialized."
        }
    } else {
        Write-Warning "Project not found: $projectPath"
    }
}

Write-Host ""

# If a secrets file is provided, load secrets from it
if ($FromFile -ne "" -and (Test-Path $FromFile)) {
    Write-Host "Loading secrets from $FromFile..."
    
    $secrets = Get-Content $FromFile -Raw | ConvertFrom-Json
    
    # Function to flatten JSON into key:value pairs
    function Flatten-Json {
        param(
            [Parameter(Mandatory=$true)]
            $Object,
            [string]$Prefix = ""
        )
        
        $result = @{}
        
        foreach ($property in $Object.PSObject.Properties) {
            $key = if ($Prefix) { "$Prefix`:$($property.Name)" } else { $property.Name }
            
            if ($property.Value -is [PSCustomObject]) {
                $nested = Flatten-Json -Object $property.Value -Prefix $key
                foreach ($nestedKey in $nested.Keys) {
                    $result[$nestedKey] = $nested[$nestedKey]
                }
            } elseif ($property.Value -is [array]) {
                for ($i = 0; $i -lt $property.Value.Count; $i++) {
                    $arrayKey = "$key`:$i"
                    if ($property.Value[$i] -is [PSCustomObject]) {
                        $nested = Flatten-Json -Object $property.Value[$i] -Prefix $arrayKey
                        foreach ($nestedKey in $nested.Keys) {
                            $result[$nestedKey] = $nested[$nestedKey]
                        }
                    } else {
                        $result[$arrayKey] = $property.Value[$i]
                    }
                }
            } else {
                # Skip comment fields and empty values
                if (-not $property.Name.StartsWith("_") -and $property.Value -ne $null -and $property.Value -ne "") {
                    $result[$key] = $property.Value.ToString()
                }
            }
        }
        
        return $result
    }
    
    $flatSecrets = Flatten-Json -Object $secrets
    
    foreach ($project in $projects) {
        $projectPath = Join-Path $solutionRoot $project
        
        if (Test-Path $projectPath) {
            Write-Host ""
            Write-Host "Setting secrets for $project..."
            
            foreach ($key in $flatSecrets.Keys) {
                $value = $flatSecrets[$key]
                if ($value -and $value -ne "") {
                    Write-Host "  Setting: $key"
                    dotnet user-secrets set "$key" "$value" --project $projectPath | Out-Null
                }
            }
        }
    }
    
    Write-Host ""
    Write-Host "Secrets loaded successfully!"
    
} elseif ($Interactive) {
    Write-Host "Interactive mode - please enter values for required secrets."
    Write-Host "Press Enter to skip a secret (use default/empty value)."
    Write-Host ""
    
    $secretsToSet = @(
        @{ Key = "ConnectionStrings:DefaultConnection"; Description = "SQL Server connection string"; Default = "Server=localhost;Database=AAR;Integrated Security=True;TrustServerCertificate=True" },
        @{ Key = "Azure:OpenAI:Endpoint"; Description = "Azure OpenAI endpoint URL"; Default = "" },
        @{ Key = "Azure:OpenAI:ApiKey"; Description = "Azure OpenAI API key"; Default = "" },
        @{ Key = "Azure:StorageConnectionString"; Description = "Azure Storage connection string"; Default = "" },
        @{ Key = "Jwt:Secret"; Description = "JWT signing secret (min 32 chars)"; Default = "" },
        @{ Key = "VectorStore:CosmosEndpoint"; Description = "Cosmos DB endpoint"; Default = "" },
        @{ Key = "VectorStore:CosmosKey"; Description = "Cosmos DB key"; Default = "" }
    )
    
    foreach ($project in $projects) {
        $projectPath = Join-Path $solutionRoot $project
        
        if (Test-Path $projectPath) {
            Write-Host ""
            Write-Host "Setting secrets for $project..."
            
            foreach ($secret in $secretsToSet) {
                $prompt = "$($secret.Description) [$($secret.Key)]"
                if ($secret.Default) {
                    $prompt += " (default: $($secret.Default))"
                }
                $prompt += ": "
                
                $value = Read-Host $prompt
                
                if ([string]::IsNullOrWhiteSpace($value) -and $secret.Default) {
                    $value = $secret.Default
                }
                
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    dotnet user-secrets set "$($secret.Key)" "$value" --project $projectPath | Out-Null
                    Write-Host "  Set: $($secret.Key)"
                }
            }
        }
    }
} else {
    Write-Host "No secrets file provided and not in interactive mode."
    Write-Host ""
    Write-Host "Usage options:"
    Write-Host "  1. Copy secrets.local.json.template to secrets.local.json,"
    Write-Host "     fill in values, then run:"
    Write-Host "     .\scripts\setup-dev-secrets.ps1 -FromFile .\secrets.local.json"
    Write-Host ""
    Write-Host "  2. Run in interactive mode:"
    Write-Host "     .\scripts\setup-dev-secrets.ps1 -Interactive"
    Write-Host ""
    Write-Host "  3. Manually set secrets using dotnet user-secrets:"
    Write-Host "     dotnet user-secrets set 'Azure:OpenAI:ApiKey' 'your-key' --project src\AAR.Api"
}

Write-Host ""
Write-Host "=========================================="
Write-Host " Setup Complete"
Write-Host "=========================================="
Write-Host ""
Write-Host "To verify secrets are set, run:"
Write-Host "  dotnet user-secrets list --project src\AAR.Api\AAR.Api.csproj"
Write-Host ""
Write-Host "See docs/MIGRATION_KEYVAULT.md for more information."
