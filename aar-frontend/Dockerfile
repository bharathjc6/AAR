# =============================================================================
# AAR Frontend Dockerfile - Multi-stage build
# Uses Node 18 LTS for building and nginx:stable-alpine for serving
# =============================================================================

# Build stage - Node 18 Alpine for smaller image
FROM node:18-alpine AS builder

# Set working directory
WORKDIR /app

# Install dependencies first (better layer caching)
COPY package*.json ./
RUN npm ci --only=production=false

# Copy source files
COPY . .

# Build arguments for environment configuration
ARG VITE_API_BASE_URL=http://localhost:5000
ARG VITE_PUBLIC_PATH=/
ARG VITE_APP_VERSION=1.0.0

# Set environment variables for build
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV VITE_PUBLIC_PATH=$VITE_PUBLIC_PATH
ENV VITE_APP_VERSION=$VITE_APP_VERSION

# Build the application (includes TypeScript check)
RUN npm run build

# Production stage - nginx for serving static files
FROM nginx:stable-alpine AS production

# Copy custom nginx config with SPA fallback and security headers
COPY nginx.conf /etc/nginx/nginx.conf

# Copy built assets from builder stage
COPY --from=builder /app/dist /usr/share/nginx/html

# Create non-root user for security
RUN addgroup -g 1001 -S appgroup && \
    adduser -u 1001 -S appuser -G appgroup && \
    chown -R appuser:appgroup /usr/share/nginx/html && \
    chown -R appuser:appgroup /var/cache/nginx && \
    chown -R appuser:appgroup /var/log/nginx && \
    touch /var/run/nginx.pid && \
    chown appuser:appgroup /var/run/nginx.pid

# Expose port 80
EXPOSE 80

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost/health || exit 1

# Start nginx in foreground
CMD ["nginx", "-g", "daemon off;"]
