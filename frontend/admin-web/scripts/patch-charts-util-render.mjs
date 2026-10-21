/**
 * Patch @ant-design/charts-util react/render for React 19.
 * Upstream 0.0.3 races concurrent dynamic import('react-dom/client') and falls
 * back to removed ReactDOM.render. Run via postinstall.
 *
 * npm workspaces hoist deps to frontend/node_modules — search both app and workspace roots.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const rootDir = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(rootDir, '..');
const workspaceRoot = path.resolve(appRoot, '..');

const FIXED_RENDER = `import { createRoot } from 'react-dom/client';

const MARK = '__rc_react_root__';

function modernRender(node, container) {
  if (!container[MARK]) {
    container[MARK] = createRoot(container);
  }
  container[MARK].render(node);
}

export function render(node, container) {
  modernRender(node, container);
}

export async function unmount(container) {
  var _a;
  (_a = container[MARK]) === null || _a === void 0 ? void 0 : _a.unmount();
  delete container[MARK];
}
`;

function walk(dir, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === '.bin') continue;
      walk(full, out);
    } else if (entry.name === 'render.js') {
      const norm = full.replace(/\\/g, '/');
      if (norm.includes('/@ant-design/charts-util/') && norm.endsWith('/react/render.js')) {
        out.push(full);
      }
    }
  }
  return out;
}

const searchRoots = [
  path.join(appRoot, 'node_modules'),
  path.join(workspaceRoot, 'node_modules'),
];

const targets = [...new Set(searchRoots.flatMap((dir) => walk(dir)))];
if (targets.length === 0) {
  console.warn('[patch-charts-util-render] no charts-util render.js found — skip');
  process.exit(0);
}

for (const file of targets) {
  fs.writeFileSync(file, FIXED_RENDER, 'utf8');
  console.log('[patch-charts-util-render] patched', path.relative(workspaceRoot, file));
}
