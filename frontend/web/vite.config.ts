import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: "https://localhost:7212",
        changeOrigin: true,
        // The dev cert is self-signed, so the proxy skips verification.
        // Dev only - this block is not used by `vite build`.
        secure: false,
      },
    },
  },
  build: {
    sourcemap: false,
    chunkSizeWarningLimit: 300,
    rollupOptions: {
      output: {
        // Vite 8 bundles with Rolldown, not Rollup. Rolldown types
        // `manualChunks` as a function only, so the old object form
        // ({ react: [...] }) fails `tsc -b` - which is why `npm run build`
        // was broken while `npm run dev` was fine (dev does not typecheck).
        codeSplitting: {
          groups: [
            {
              name: "react",
              test: /[\\/]node_modules[\\/](react|react-dom|react-router|react-router-dom)[\\/]/,
            },
          ],
        },
      },
    },
  },
});
