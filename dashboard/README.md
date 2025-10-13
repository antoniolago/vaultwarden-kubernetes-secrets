# Vaultwarden K8s Sync Dashboard

Modern React dashboard for monitoring and managing Vaultwarden to Kubernetes secret synchronization.

## Features

- 🔐 **Token Authentication** - Secure login with generated token
- 📊 **Real-time Monitoring** - Live sync statistics and metrics
- 🔑 **Secret Management** - Complete inventory with search
- 📝 **Sync Logs** - Detailed operation history
- 💻 **Resource Monitoring** - CPU/Memory usage tracking
- 🎨 **Modern UI** - Built with MUI Joy design system
- 📱 **Responsive** - Works on mobile, tablet, and desktop

## Tech Stack

- **Framework**: React 18 with TypeScript
- **Build Tool**: Vite
- **Package Manager**: Bun
- **UI Library**: MUI Joy
- **Data Fetching**: TanStack Query (React Query)
- **Charts**: Recharts
- **Icons**: Lucide React
- **Routing**: React Router v6

## Development

### Prerequisites

- Bun 1.0+
- Node.js 20+ (for compatibility)

### Setup

```bash
# Install Bun (if not installed)
curl -fsSL https://bun.sh/install | bash

# Install dependencies
bun install

# Copy environment file
cp .env.example .env

# Configure API URL in .env
VITE_API_URL=http://localhost:8080/api

# Start development server
bun run dev
```

The dashboard will be available at `http://localhost:3000`.

### Build

```bash
# Production build
bun run build

# Preview production build
bun run preview
```

## Docker

### Build Image

```bash
docker build -t vaultwarden-k8s-dashboard .
```

### Run Container

```bash
docker run -p 3000:80 vaultwarden-k8s-dashboard
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `VITE_API_URL` | API endpoint URL | `http://localhost:8080/api` |

### API Authentication

The dashboard requires an authentication token to access the API. Get your token from Kubernetes:

```bash
kubectl get secret {release-name}-auth -n {namespace} \
  -o jsonpath='{.data.token}' | base64 -d
```

## Pages

### 1. Login (`/login`)
- Token-based authentication
- Validates token against API
- Stores token in localStorage

### 2. Dashboard (`/`)
- Overview statistics
- Success rate metrics
- 7-day activity chart
- Namespace distribution
- Recent activity

### 3. Secrets (`/secrets`)
- Complete secret inventory
- Search and filter
- Status indicators
- Namespace grouping
- Error messages

### 4. Sync Logs (`/logs`)
- Operation history
- Detailed metrics (created/updated/skipped/failed)
- Duration tracking
- Error details

### 5. Resources (`/resources`)
- **Real-time CPU usage** with charts
- **Memory tracking** (Working Set, Private, GC)
- **Thread count** monitoring
- **API and Sync service** separate tracking
- **Historical data** visualization
- **Alerts** for high resource usage
- **Optimization tips**

## Project Structure

```
dashboard/
├── src/
│   ├── components/
│   │   └── Layout.tsx         # Main layout with sidebar
│   ├── lib/
│   │   ├── api.ts            # API client
│   │   ├── auth.tsx          # Authentication context
│   │   └── utils.ts          # Helper functions
│   ├── pages/
│   │   ├── Login.tsx         # Login page
│   │   ├── Dashboard.tsx     # Overview page
│   │   ├── Secrets.tsx       # Secrets table
│   │   ├── SyncLogs.tsx      # Logs table
│   │   └── Resources.tsx     # Resource monitoring
│   ├── App.tsx               # Router setup
│   └── main.tsx              # Entry point
├── Dockerfile                # Production build
├── nginx.conf                # Nginx configuration
├── package.json              # Dependencies
├── vite.config.ts            # Vite configuration
└── tsconfig.json             # TypeScript config
```

## API Endpoints Used

### Dashboard
- `GET /api/dashboard/overview` - Statistics
- `GET /api/dashboard/timeline?days=7` - Activity chart
- `GET /api/dashboard/namespaces` - Distribution

### Sync Logs
- `GET /api/synclogs?limit=100` - Recent logs

### Secrets
- `GET /api/secrets` - All secrets
- `GET /api/secrets/active` - Active only

### System Resources
- `GET /api/system/resources` - API service metrics
- `GET /api/system/sync-service-resources` - Sync service metrics

## Development Tips

### Hot Module Replacement
Vite provides instant HMR during development:
```bash
bun run dev
# Edit files and see changes immediately
```

### Type Checking
```bash
# Run TypeScript compiler (no-emit)
bun run build  # Automatically type-checks
```

### Why Bun?
- **3x faster** npm install
- **Built-in TypeScript** support
- **Compatible** with npm packages
- **Fast** development server

### Why MUI Joy?
- **Modern design system** out of the box
- **Accessible** components (ARIA compliant)
- **TypeScript first** with excellent types
- **Customizable** theming
- **Production tested** by thousands

## Deployment

### Kubernetes (via Helm)

```yaml
# values.yaml
dashboard:
  enabled: true
  replicaCount: 2
  image:
    repository: ghcr.io/antoniolago/vaultwarden-k8s-dashboard
    tag: latest
  resources:
    requests:
      cpu: 10m
      memory: 32Mi
    limits:
      cpu: 50m
      memory: 64Mi
```

### Docker Compose

```yaml
dashboard:
  build: ./dashboard
  ports:
    - "3000:80"
  environment:
    - VITE_API_URL=http://api:8080/api
```

## Resource Monitoring

The `/resources` page helps you monitor and optimize performance:

### What It Tracks
- **CPU Usage**: Percentage per core
- **Memory**: Working Set, Private Memory, GC Memory
- **Threads**: Active thread count
- **Uptime**: Process runtime
- **Both services**: API and Sync processes

### Visual Indicators
- 🟢 **Green** (< 70%): Normal usage
- 🟡 **Yellow** (70-90%): Warning level
- 🔴 **Red** (> 90%): Critical - needs attention

### Built-in Recommendations
If CPU is high (100-300%), the UI shows:
- Increase `SYNC__SYNCINTERVALSECONDS`
- Use `SYNC__CONTINUOUSSYNC=false`
- Filter by Organization/Collection ID
- Scale API horizontally

## Troubleshooting

### Dashboard shows "Unauthorized"
- Check your token is correct
- Verify API is running
- Check CORS settings in API

### Charts not loading
- Check browser console for errors
- Verify API endpoints are accessible
- Check TanStack Query devtools

### Build fails
```bash
# Clear cache and reinstall
rm -rf node_modules bun.lockb
bun install
bun run build
```

## Contributing

When adding new features:
1. Follow existing patterns
2. Use TypeScript types
3. Add proper error handling
4. Update this README

## License

Same as main project - see [LICENSE](../LICENSE)
