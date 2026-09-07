// Lives outside Seo.tsx because exporting a constant alongside a component
// breaks React Fast Refresh (react-refresh/only-export-components).
export const SITE_URL = import.meta.env.VITE_SITE_URL ?? 'https://ragdocqa.app';
