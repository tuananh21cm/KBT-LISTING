import React from 'react';
import { createRoot } from 'react-dom/client';
import { STORAGE_KEYS } from '../shared/config';
import { applyDocumentTheme, normalizeTheme } from '../shared/theme';
import { App } from './App';
import './styles.css';

void chrome.storage.local.get(STORAGE_KEYS.theme).then((raw) => {
  applyDocumentTheme(normalizeTheme(raw[STORAGE_KEYS.theme] as string | undefined));
});

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
