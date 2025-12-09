# AAR Frontend

A modern, production-ready React frontend for the Automated Architecture Review (AAR) system.

## Features

- 🎨 **Modern UI** - Built with MUI 5 and Framer Motion animations
- 🌙 **Dark/Light Mode** - Full theme support with system preference detection
- 📊 **Dashboard** - Real-time metrics and project overview
- 📁 **Project Management** - Create, view, and manage projects
- 🔍 **Report Viewer** - Detailed findings with code snippets
- ⚡ **Real-time Updates** - SignalR integration for live progress
- 🔐 **Authentication** - API key-based authentication
- 📱 **Responsive** - Mobile-friendly design
- ♿ **Accessible** - WCAG 2.1 compliant

## Tech Stack

- **Framework**: React 18 + TypeScript
- **Build Tool**: Vite 5
- **UI Library**: MUI 5 + Emotion
- **State Management**: TanStack React Query 5 + Zustand
- **Real-time**: Microsoft SignalR 8
- **Animations**: Framer Motion 11
- **Charts**: Recharts
- **Testing**: Vitest + Cypress
- **Styling**: Emotion + CSS-in-JS

## Quick Start

### Prerequisites

- **Node.js 18+** (LTS recommended)
- npm 9+
- AAR API server running on `http://localhost:5000` (optional for mock mode)

### Node.js Version Setup

We use Node.js 18 LTS. Here's how to set it up:

**macOS/Linux (using nvm):**
```bash
# Install nvm if not installed
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash

# Install and use Node 18
nvm install 18
nvm use 18

# Verify
node --version  # Should show v18.x.x
```

**Windows (using nvm-windows):**
```powershell
# Install nvm-windows from: https://github.com/coreybutler/nvm-windows/releases
# Then run in PowerShell as Administrator:
nvm install 18
nvm use 18

# Verify
node --version  # Should show v18.x.x
```

**Automatic version switching:**
The project includes `.nvmrc` file. If you have nvm configured for auto-switching:
```bash
cd aar-frontend
nvm use  # Automatically uses version from .nvmrc
```

### Installation

```bash
# Install dependencies
npm ci

# Copy environment template
cp .env.example .env.local

# Start development server
npm run dev
```

The app will be available at `http://localhost:3000`

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `VITE_API_BASE_URL` | Backend API base URL | `http://localhost:5000` |
| `VITE_SIGNALR_HUB_URL` | SignalR hub URL | (derived from API_BASE_URL) |
| `VITE_PUBLIC_PATH` | Public path for subpath deployments | `/` |
| `VITE_MOCK_MODE` | Enable mock mode (no backend required) | `false` |

## Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start development server |
| `npm run build` | Build for production |
| `npm run preview` | Preview production build |
| `npm run lint` | Run ESLint |
| `npm run typecheck` | Run TypeScript type checking |
| `npm run test` | Run unit tests |
| `npm run test:watch` | Run tests in watch mode |
| `npm run test:coverage` | Run tests with coverage |
| `npm run test:e2e` | Run Cypress E2E tests |
| `npm run test:e2e:open` | Open Cypress test runner |

## Project Structure

```
aar-frontend/
├── public/              # Static assets
├── src/
│   ├── api/             # API client and axios instance
│   ├── components/      # Reusable UI components
│   ├── features/        # Feature-based pages
│   │   ├── auth/        # Login page
│   │   ├── dashboard/   # Dashboard page
│   │   ├── logs/        # Logs viewer
│   │   ├── projects/    # Project pages
│   │   └── settings/    # Settings page
│   ├── hooks/           # Custom React hooks
│   ├── test/            # Test utilities
│   ├── theme/           # MUI theme configuration
│   ├── types/           # TypeScript type definitions
│   ├── App.tsx          # Main app with routes
│   └── main.tsx         # Entry point
├── cypress/             # E2E tests
├── Dockerfile           # Docker configuration
├── nginx.conf           # Nginx configuration
└── docker-compose.yml   # Docker Compose config
```

## Docker

### Build and run with Docker

```bash
# Build the image
docker build -t aar-frontend .

# Run the container
docker run -p 3000:80 -e API_URL=http://api:5000 aar-frontend
```

### Run with Docker Compose

```bash
docker-compose up -d
```

## Testing

### Unit Tests (Vitest)

```bash
# Run all tests
npm run test

# Run with coverage
npm run test:coverage

# Watch mode
npm run test:watch
```

### E2E Tests (Cypress)

```bash
# Run in headless mode
npm run test:e2e

# Open interactive runner
npm run test:e2e:open
```

## Authentication

The app uses API key-based authentication. To log in:

1. Navigate to `/login`
2. Enter your API key (format: `aar_xxxxxxxxxxxxxxxxxxxxxxxxxxxx`)
3. The key is stored securely in browser localStorage

**Development API Key**: `aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI`

## API Integration

The frontend communicates with the AAR API:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/projects` | GET | List all projects |
| `/projects` | POST | Create project (zip upload) |
| `/projects/git` | POST | Create project from Git URL |
| `/projects/:id` | GET | Get project details |
| `/projects/:id/analyze` | POST | Start analysis |
| `/projects/:id/report` | GET | Get analysis report |

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests and linting
5. Submit a pull request

## License

MIT License - see LICENSE file for details.
