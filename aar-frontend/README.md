# AAR Frontend

A modern, production-ready React frontend for the Automated Architecture Review (AAR) system.

## Features

- 🎨 **Modern UI** - Built with MUI 6 and Framer Motion animations
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
- **UI Library**: MUI 6 + Emotion
- **State Management**: TanStack React Query 5 + Zustand
- **Real-time**: Microsoft SignalR 8
- **Animations**: Framer Motion 11
- **Charts**: Recharts
- **Testing**: Vitest + Cypress
- **Styling**: Emotion + CSS-in-JS

## Quick Start

### Prerequisites

- Node.js 20+
- npm 10+
- AAR API server running on `http://localhost:5000`

### Installation

```bash
# Install dependencies
npm install

# Copy environment template
cp .env.example .env

# Start development server
npm run dev
```

The app will be available at `http://localhost:5173`

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `VITE_API_URL` | Backend API base URL | `http://localhost:5000/api/v1` |
| `VITE_SIGNALR_HUB_URL` | SignalR hub URL | `http://localhost:5000/hubs/analysis` |

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
