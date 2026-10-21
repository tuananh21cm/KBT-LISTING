import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const rootDir = path.dirname(fileURLToPath(import.meta.url));
const workspaceRoot = path.resolve(rootDir, '..');

export default defineConfig({
  plugins: [react()],
  resolve: {
    dedupe: ['react', 'react-dom', 'antd', 'dayjs', '@tanstack/react-query', 'axios'],
    alias: {
      '@drop/types': path.resolve(workspaceRoot, 'packages/types/src'),
      '@drop/utils': path.resolve(workspaceRoot, 'packages/utils/src'),
      '@drop/ui': path.resolve(workspaceRoot, 'packages/ui/src'),
      '@drop/theme': path.resolve(workspaceRoot, 'packages/theme/src'),
      '@drop/api-client': path.resolve(workspaceRoot, 'packages/api-client/src'),
      '@drop/orders': path.resolve(workspaceRoot, 'packages/orders/src'),
      '@drop/receiving-banks': path.resolve(workspaceRoot, 'packages/receiving-banks/src'),
      '@drop/shops': path.resolve(workspaceRoot, 'packages/shops/src'),
      '@drop/shop-finance': path.resolve(workspaceRoot, 'packages/shop-finance/src'),
      '@drop/company-ledger': path.resolve(workspaceRoot, 'packages/company-ledger/src'),
      '@drop/shop-tasks': path.resolve(workspaceRoot, 'packages/shop-tasks/src'),
      '@drop/listings': path.resolve(workspaceRoot, 'packages/listings/src'),
      '@drop/fulfil': path.resolve(workspaceRoot, 'packages/fulfil/src'),
      '@drop/dashboard': path.resolve(workspaceRoot, 'packages/dashboard/src'),
      '@drop/stats': path.resolve(workspaceRoot, 'packages/stats/src'),
      '@drop/fourseller': path.resolve(workspaceRoot, 'packages/fourseller/src'),
      '@drop/supplier-lists': path.resolve(workspaceRoot, 'packages/supplier-lists/src'),
      '@drop/product-opportunities': path.resolve(workspaceRoot, 'packages/product-opportunities/src'),
      '@drop/promotions': path.resolve(workspaceRoot, 'packages/promotions/src'),
      // Hoisted by npm workspaces — pin so Vite optimizeDeps does not look under app/node_modules.
      '@ant-design/charts-util': path.resolve(
        workspaceRoot,
        'node_modules/@ant-design/charts-util',
      ),
    },
  },
  optimizeDeps: {
    include: ['@ant-design/plots', '@ant-design/charts-util'],
  },
  server: {
    port: 5174,
    // Prefer failing loudly over silent fallback to 5175+ (CORS allowlist / bookmarks break).
    strictPort: true,
    allowedHosts: ['4seller-admin.kbt.global'],
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
});
