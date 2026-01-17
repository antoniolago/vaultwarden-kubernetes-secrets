# Build stage
FROM oven/bun:1 AS builder

WORKDIR /app

# Copy package files
COPY bun.lock ./

# Install dependencies
RUN bun install --frozen-lockfile

# Copy source code
COPY . .

# Build the application
RUN bun run build

# Production stage
FROM nginx:alpine

# Create nginx user (101:101) and set up writable directories
RUN touch /var/run/nginx.pid && \
    chown -R 101:101 /var/run/nginx.pid && \
    chown -R 101:101 /var/cache/nginx && \
    chown -R 101:101 /etc/nginx/conf.d

# Copy built assets from builder
COPY --from=builder /app/dist /usr/share/nginx/html

# Copy nginx configuration
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Expose port
EXPOSE 80

# Run as non-root nginx user
USER 101

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
