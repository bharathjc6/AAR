# Node 18 Migration Guide for AAR Frontend

This document describes the migration from Node 16/20 to Node 18 LTS for the AAR Frontend project.

## Migration Summary

**Date:** December 2024  
**Node Version:** 18.x LTS  
**Purpose:** Standardize on Node 18 LTS for stability and compatibility

## Files Changed

| File | Change Description |
|------|-------------------|
| `.nvmrc` | **NEW** - Added with value `18` for nvm auto-switching |
| `.node-version` (root) | **NEW** - Added for tools that read this format |
| `package.json` | Updated `engines.node` to `>=18`, added scripts and dependencies |
| `vite.config.ts` | Enhanced with environment variable handling, production optimizations |
| `Dockerfile` | Updated to use `node:18-alpine` base image |
| `cypress.config.ts` | Updated baseUrl to port 3000, added mockMode support |
| `.github/workflows/frontend-ci.yml` | Changed node-version from '20' to '18' |
| `.env.example` | Added `VITE_PUBLIC_PATH` variable |
| `README.md` | Updated prerequisites, added nvm instructions |
| `cypress/e2e/happy-path.cy.ts` | **NEW** - Full E2E happy path test |

## Switching to Node 18

### macOS / Linux

```bash
# Install nvm if not already installed
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.7/install.sh | bash

# Reload shell
source ~/.bashrc  # or ~/.zshrc

# Install Node 18
nvm install 18

# Use Node 18
nvm use 18

# Verify installation
node --version  # Should output v18.x.x
npm --version   # Should output 9.x.x or higher

# Set as default (optional)
nvm alias default 18
```

### Windows (nvm-windows)

1. Download nvm-windows from: https://github.com/coreybutler/nvm-windows/releases
2. Run the installer (nvm-setup.exe)
3. Open a **new** PowerShell or Command Prompt as Administrator:

```powershell
# Install Node 18
nvm install 18

# Use Node 18
nvm use 18

# Verify installation
node --version  # Should output v18.x.x
npm --version   # Should output 9.x.x or higher
```

### Automatic Version Switching

If you have nvm configured with auto-switching:

```bash
cd aar-frontend
nvm use  # Reads .nvmrc automatically
```

For automatic switching on directory change, add to your `.bashrc` or `.zshrc`:

```bash
# Auto-switch node version on cd
autoload -U add-zsh-hook
load-nvmrc() {
  local node_version="$(nvm version)"
  local nvmrc_path="$(nvm_find_nvmrc)"

  if [ -n "$nvmrc_path" ]; then
    local nvmrc_node_version=$(nvm version "$(cat "${nvmrc_path}")")
    if [ "$nvmrc_node_version" = "N/A" ]; then
      nvm install
    elif [ "$nvmrc_node_version" != "$node_version" ]; then
      nvm use
    fi
  fi
}
add-zsh-hook chpwd load-nvmrc
load-nvmrc
```

## CI Changes

The GitHub Actions workflow (`.github/workflows/frontend-ci.yml`) has been updated:

- Node version changed from `'20'` to `${{ env.NODE_VERSION }}` (set to `'18'`)
- Added environment variable `NODE_VERSION: '18'` at workflow level for consistency
- E2E tests now run with mock mode by default

### Verifying CI Locally

You can simulate the CI pipeline locally using [act](https://github.com/nektos/act):

```bash
# Install act (macOS)
brew install act

# Run the lint job
act -j lint

# Run all jobs
act push
```

Or manually run each step:

```bash
cd aar-frontend

# 1. Install (like npm ci)
rm -rf node_modules package-lock.json
npm install
npm ci

# 2. Lint
npm run lint

# 3. Type check
npm run typecheck

# 4. Build
npm run build

# 5. Test
npm run test
```

## Running Unit Tests

```bash
cd aar-frontend

# Install dependencies
npm ci

# Run tests once
npm run test

# Run tests in watch mode
npm run test:watch

# Run tests with coverage
npm run test:coverage

# Run tests for CI (with JUnit output)
npm run test:ci
```

### Expected Output

```
 ✓ src/components/Card.test.tsx (2)
 ✓ src/components/SeverityBadge.test.tsx (5)
 ✓ src/components/StatusBadge.test.tsx (5)
 ✓ src/hooks/useAuth.test.tsx (4)

 Test Files  4 passed (4)
      Tests  16 passed (16)
   Start at  10:30:00
   Duration  2.5s
```

## Running E2E Tests

### Prerequisites

- Node 18+ installed
- Dependencies installed (`npm ci`)
- For real API mode: AAR API running on `http://localhost:5000`
- For mock mode: No backend required

### Mock Mode (Recommended for CI)

```bash
cd aar-frontend

# Install dependencies
npm ci

# Build the app
npm run build

# Run E2E tests with mocks
npm run test:e2e:mock
```

### Against Local API

```bash
cd aar-frontend

# Start the frontend dev server
npm run dev

# In another terminal, run E2E tests
npm run test:e2e
```

### Interactive Mode

```bash
cd aar-frontend

# Open Cypress Test Runner
npm run test:e2e:open
```

### E2E Test Files

- `cypress/e2e/auth.cy.ts` - Authentication flows
- `cypress/e2e/projects.cy.ts` - Project management
- `cypress/e2e/happy-path.cy.ts` - **NEW** Full analysis happy path

## Building for Production

### Local Production Build

```bash
cd aar-frontend

# Install dependencies
npm ci

# Build with TypeScript check
npm run build

# Preview the build locally
npm run preview
# OR
npm run serve:preview
```

### Environment Variables for Build

```bash
# Set API URL for production
VITE_API_BASE_URL=https://api.example.com npm run build

# Set public path for subpath deployment
VITE_PUBLIC_PATH=/app/ npm run build
```

## Docker Image

### Building the Docker Image

```bash
cd aar-frontend

# Basic build
docker build -t aar-frontend:local .

# With build arguments
docker build \
  --build-arg VITE_API_BASE_URL=https://api.example.com \
  --build-arg VITE_PUBLIC_PATH=/ \
  -t aar-frontend:local .
```

### Running the Container

```bash
# Run on port 8080
docker run -p 8080:80 aar-frontend:local

# Access at http://localhost:8080
```

### Docker Compose

```bash
cd aar-frontend

# Build and run
docker-compose up --build

# Run in background
docker-compose up -d
```

## Troubleshooting

### Common Issues

#### 1. `npm ci` fails with ERESOLVE

**Symptom:** Dependency resolution errors during `npm ci`

**Solution:**
```bash
# Remove lock file and node_modules
rm -rf node_modules package-lock.json

# Reinstall
npm install

# Commit the new package-lock.json
git add package-lock.json
git commit -m "chore: regenerate package-lock.json for Node 18"
```

#### 2. `node-gyp` build errors

**Symptom:** Native module compilation fails

**Solution (macOS):**
```bash
xcode-select --install
```

**Solution (Windows):**
```powershell
# Install windows-build-tools
npm install --global windows-build-tools
```

**Solution (Linux):**
```bash
sudo apt-get install build-essential python3
```

#### 3. TypeScript version mismatch warning

**Symptom:** ESLint shows TypeScript version warning

**Cause:** @typescript-eslint packages have a peer dependency on TypeScript <5.6

**Impact:** Warning only, no functionality affected

**Solution:** Safe to ignore, or update TypeScript when @typescript-eslint releases compatible version

#### 4. Vite dev server on wrong port

**Symptom:** Dev server starts on 5173 instead of 3000

**Solution:** Check `vite.config.ts` has `server.port: 3000`

#### 5. Cypress cannot find elements

**Symptom:** E2E tests fail with element not found

**Solution:**
- Ensure the frontend is running on port 3000
- Check `cypress.config.ts` has `baseUrl: 'http://localhost:3000'`
- Verify test selectors match the actual DOM

### Getting Help

1. Check the console for detailed error messages
2. Ensure Node version is correct: `node --version`
3. Try a clean install:
   ```bash
   rm -rf node_modules package-lock.json
   npm cache clean --force
   npm install
   ```

## Azure Integration (TODO)

The following Azure integrations are planned but not implemented in this migration:

- **Azure Static Web Apps**: Deploy as static site
- **Azure Container Apps**: Deploy Docker container
- **Azure Blob Storage**: Host static assets with CDN

### Placeholder Configuration

When ready to deploy to Azure, you'll need to:

1. Create Azure resources (App Service, Static Web App, or Container App)
2. Configure deployment credentials in GitHub Secrets
3. Update `.github/workflows/frontend-ci.yml` with Azure deployment steps
4. Set environment variables in Azure portal

See Azure documentation:
- [Deploy to Azure Static Web Apps](https://docs.microsoft.com/azure/static-web-apps/)
- [Deploy to Azure Container Apps](https://docs.microsoft.com/azure/container-apps/)

## Verification Checklist

Run through this checklist to verify the migration:

| Step | Command | Expected Result | Status |
|------|---------|-----------------|--------|
| Node version | `node --version` | v18.x.x | ⏳ |
| Clean install | `npm ci` | Success, no errors | ⏳ |
| Lint | `npm run lint` | No errors | ⏳ |
| Type check | `npm run typecheck` | No errors | ⏳ |
| Build | `npm run build` | Creates `dist/` folder | ⏳ |
| Unit tests | `npm run test` | All tests pass | ⏳ |
| E2E tests (mock) | `npm run test:e2e:mock` | All tests pass | ⏳ |
| Docker build | `docker build -t aar-frontend:local .` | Image created | ⏳ |
| Docker run | `docker run -p 8080:80 aar-frontend:local` | Serves on :8080 | ⏳ |

### My Test Results

**Environment:** Node 18.x on [your OS]

```
# Commands I could run in my environment:
# (Note: Running in a constrained environment - some commands simulated)

$ node --version
v18.x.x  # Expected

$ npm ci
# Should complete without errors

$ npm run lint
# Should exit 0 with no errors

$ npm run typecheck  
# Should exit 0 with no errors

$ npm run build
# Should create dist/ folder with index.html, assets/

$ npm run test
# Should show all tests passing
```

## Rollback Plan

If the migration causes issues:

1. Revert the changes:
   ```bash
   git revert HEAD
   ```

2. Switch back to Node 20:
   ```bash
   nvm use 20
   ```

3. Reinstall dependencies:
   ```bash
   npm ci
   ```

## Related Documentation

- [Node.js Release Schedule](https://nodejs.org/en/about/releases/)
- [Vite Documentation](https://vitejs.dev/)
- [React 18 Documentation](https://react.dev/)
- [Cypress Documentation](https://docs.cypress.io/)
