import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Manual multi-entry MV3 build (no CRXjs) for deterministic output:
// - sidepanel.html -> sidepanel.js (React) — mở cạnh trang web qua chrome.sidePanel
// - background     -> background.js (ES module service worker)
// - content        -> content.js (standalone, no imports so it loads as a classic content script)
// Static assets (manifest.json, icons) live in public/ and are copied to dist/ as-is.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'esnext',
    modulePreload: false,
    rollupOptions: {
      input: {
        sidepanel: 'sidepanel.html',
        background: 'src/background/index.ts',
        content: 'src/content/index.ts',
      },
      output: {
        format: 'es',
        entryFileNames: '[name].js',
        chunkFileNames: 'chunks/[name].js',
        assetFileNames: 'assets/[name].[ext]',
      },
    },
  },
});
