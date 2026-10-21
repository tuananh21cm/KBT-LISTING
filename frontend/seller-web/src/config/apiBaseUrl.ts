/** API root including `/api/v1` — set via Vite env (`VITE_API_BASE_URL`). */
export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || '/api/v1').replace(/\/$/, '');
