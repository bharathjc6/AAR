import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  // Load env variables based on mode (development, production, etc.)
  const env = loadEnv(mode, process.cwd(), '');
  
  return {
    plugins: [react()],
    // Support VITE_PUBLIC_PATH for deployments to subpaths
    base: env.VITE_PUBLIC_PATH || '/',
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    server: {
      port: 3000,
      proxy: {
        '/api': {
          target: env.VITE_API_BASE_URL || 'http://localhost:5000',
          changeOrigin: true,
        },
        '/hubs': {
          target: env.VITE_API_BASE_URL || 'http://localhost:5000',
          changeOrigin: true,
          ws: true,
        },
      },
    },
    build: {
      outDir: 'dist',
      sourcemap: mode !== 'production', // No sourcemaps in production for security
      rollupOptions: {
        output: {
          manualChunks: {
            vendor: ['react', 'react-dom', 'react-router-dom'],
            mui: ['@mui/material', '@mui/icons-material'],
            query: ['@tanstack/react-query'],
          },
        },
      },
    },
    // Define global constants available at build time
    define: {
      __APP_VERSION__: JSON.stringify(env.npm_package_version || '1.0.0'),
    },
  };
});
