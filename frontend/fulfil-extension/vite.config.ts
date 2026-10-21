import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, build as viteBuild, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

const root = path.dirname(fileURLToPath(import.meta.url));

/**
 * Content scripts Chrome classic (manifest không `type: "module"`).
 * Không được còn `import … from "./chunks/…"` — sẽ lỗi
 * "Cannot use import statement outside a module".
 *
 * Shared modules (vd. sheinOrders) bị Rollup tách chunk khi build chung với
 * background/sidepanel → entry content vẫn là ESM. Build lại từng entry dạng
 * IIFE (inline hết dependency) và ghi đè file trong dist.
 */
const CONTENT_ENTRIES = {
  content: path.resolve(root, 'src/content/index.ts'),
  sheinBridge: path.resolve(root, 'src/content/sheinBridge.ts'),
  capture: path.resolve(root, 'src/content/sheinCapture.ts'),
} as const;

function buildContentScriptsIife(): Plugin {
  let distDir = path.resolve(root, 'dist');

  return {
    name: 'build-content-scripts-iife',
    apply: 'build',
    configResolved(config) {
      distDir = path.resolve(config.root, config.build.outDir);
    },
    async closeBundle() {
      for (const [name, entry] of Object.entries(CONTENT_ENTRIES)) {
        await viteBuild({
          configFile: false,
          root,
          logLevel: 'warn',
          build: {
            emptyOutDir: false,
            outDir: distDir,
            target: 'esnext',
            // Không dùng plugin React — content scripts thuần TS.
            rollupOptions: {
              input: entry,
              output: {
                format: 'iife',
                name: `KbFulfil_${name}`,
                inlineDynamicImports: true,
                entryFileNames: `${name}.js`,
                assetFileNames: 'assets/[name].[ext]',
              },
            },
          },
        });
      }
    },
  };
}

// Manual multi-entry MV3 build (no CRXjs):
// - sidepanel.html -> sidepanel.js (React, ES module)
// - background     -> background.js (ES module service worker)
// - content / sheinBridge / capture -> IIFE classic (plugin closeBundle)
// Static assets (manifest.json, icons) copy từ public/ → dist/
export default defineConfig({
  plugins: [react(), buildContentScriptsIife()],
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'esnext',
    modulePreload: false,
    rollupOptions: {
      input: {
        sidepanel: path.resolve(root, 'sidepanel.html'),
        background: path.resolve(root, 'src/background/index.ts'),
        // Giữ trong graph để `vite build --watch` rebuild khi sửa content/*.
        // Output ESM tạm bị plugin ghi đè bằng IIFE.
        ...CONTENT_ENTRIES,
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
