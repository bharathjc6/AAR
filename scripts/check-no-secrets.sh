#!/bin/bash
# =============================================================================
# check-no-secrets.sh
# Scans the codebase for accidentally committed secrets
# Run this script in CI/CD pipelines to prevent secret leaks
# =============================================================================

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "=========================================="
echo " AAR Secret Scanner"
echo "=========================================="

ERRORS=0

# Patterns that indicate potential secrets
# These patterns match common secret formats
SECRET_PATTERNS=(
    # Azure connection strings
    'AccountKey=[A-Za-z0-9+/=]{40,}'
    'SharedAccessKey=[A-Za-z0-9+/=]{40,}'
    'DefaultEndpointsProtocol=https;AccountName='
    
    # API Keys (generic patterns)
    'api[_-]?key["\s]*[:=]["\s]*[A-Za-z0-9]{20,}'
    'apikey["\s]*[:=]["\s]*[A-Za-z0-9]{20,}'
    
    # Azure OpenAI keys
    '[A-Fa-f0-9]{32}'
    
    # JWT secrets (long base64 strings in config context)
    'Secret["\s]*[:=]["\s]*[A-Za-z0-9+/=]{40,}'
    
    # Connection strings with passwords
    'Password=[^;]{8,}'
    'pwd=[^;]{8,}'
    
    # Cosmos DB keys
    'CosmosKey["\s]*[:=]["\s]*[A-Za-z0-9+/=]{40,}'
    
    # Generic secret/token patterns
    'secret[_-]?key["\s]*[:=]["\s]*[A-Za-z0-9]{16,}'
    'access[_-]?token["\s]*[:=]["\s]*[A-Za-z0-9]{16,}'
    'bearer["\s]+[A-Za-z0-9._-]{20,}'
)

# Files to exclude from scanning
EXCLUDE_PATTERNS=(
    '*.dll'
    '*.exe'
    '*.pdb'
    '*.png'
    '*.jpg'
    '*.gif'
    '*.ico'
    '*.woff*'
    '*.ttf'
    'bin/*'
    'obj/*'
    'node_modules/*'
    '.git/*'
    'coverage/*'
    'TestResults/*'
    '*.template'
    'REPORT_SECRETS_SCAN.md'
    'check-no-secrets.sh'
    'MIGRATION_KEYVAULT.md'
)

# Build exclude arguments for grep
EXCLUDE_ARGS=""
for pattern in "${EXCLUDE_PATTERNS[@]}"; do
    EXCLUDE_ARGS="$EXCLUDE_ARGS --exclude=$pattern"
done

EXCLUDE_DIR_ARGS="--exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules --exclude-dir=.git --exclude-dir=coverage --exclude-dir=TestResults --exclude-dir=.vs"

echo ""
echo "Scanning for potential secrets..."
echo ""

# Check for each pattern
for pattern in "${SECRET_PATTERNS[@]}"; do
    echo -n "Checking pattern: ${pattern:0:40}..."
    
    # Use grep to find matches
    MATCHES=$(grep -rniE "$pattern" . $EXCLUDE_ARGS $EXCLUDE_DIR_ARGS 2>/dev/null || true)
    
    if [ -n "$MATCHES" ]; then
        echo -e " ${YELLOW}FOUND${NC}"
        echo "$MATCHES" | head -10
        echo ""
        ((ERRORS++))
    else
        echo -e " ${GREEN}OK${NC}"
    fi
done

echo ""

# Check for specific secret files that shouldn't be committed
echo "Checking for secret files..."

SECRET_FILES=(
    "secrets.local.json"
    ".env"
    ".env.local"
    ".env.production"
    "*.pfx"
    "*.p12"
    "*.key"
)

for file_pattern in "${SECRET_FILES[@]}"; do
    FOUND=$(find . -name "$file_pattern" -not -path "./.git/*" -not -path "./node_modules/*" 2>/dev/null || true)
    if [ -n "$FOUND" ]; then
        echo -e "${RED}FOUND secret file: $FOUND${NC}"
        ((ERRORS++))
    fi
done

echo ""

# Check that appsettings.json files don't contain real secrets
echo "Checking appsettings.json files for embedded secrets..."

APPSETTINGS_FILES=$(find . -name "appsettings*.json" -not -name "*.template" -not -path "./.git/*" -not -path "./node_modules/*" 2>/dev/null || true)

for file in $APPSETTINGS_FILES; do
    # Check for patterns that look like real values (not placeholders)
    if grep -qE '"ApiKey"\s*:\s*"[A-Za-z0-9]{20,}"' "$file" 2>/dev/null; then
        if ! grep -q "FROM_KEY_VAULT" "$file" 2>/dev/null; then
            echo -e "${RED}Potential API key in: $file${NC}"
            ((ERRORS++))
        fi
    fi
    
    if grep -qE 'AccountKey=[A-Za-z0-9+/=]{40,}' "$file" 2>/dev/null; then
        echo -e "${RED}Potential storage account key in: $file${NC}"
        ((ERRORS++))
    fi
done

echo ""
echo "=========================================="

if [ $ERRORS -gt 0 ]; then
    echo -e "${RED}FAILED: Found $ERRORS potential secret(s)${NC}"
    echo ""
    echo "Please review the findings above and ensure:"
    echo "1. Real secrets are stored in Azure Key Vault"
    echo "2. Local development uses user-secrets or secrets.local.json (gitignored)"
    echo "3. appsettings.json contains only placeholders for secrets"
    echo ""
    echo "See docs/MIGRATION_KEYVAULT.md for guidance."
    exit 1
else
    echo -e "${GREEN}PASSED: No secrets detected${NC}"
    exit 0
fi
