import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") }
  },
  server: {
    port: 5173,
    /*
     * Vite rejects any request whose Host header is not on this list, and the
     * hosted preview is reached through a generated external hostname
     * (`<port>-<workspace>.e2b.app`) rather than through localhost, so without
     * this every preview URL returns "Blocked request. This host ... is not
     * allowed." and the app never loads.
     *
     * Matching rules in Vite 5 (server/hostCheck.ts): an entry matches either
     * exactly, or - when it starts with a dot - that domain and every
     * subdomain. A RegExp entry can never match, so these are plain strings.
     *
     * Scoped to the sandbox preview domains on purpose. `allowedHosts: true`
     * would disable the check entirely, which is a real security regression on
     * any network-reachable deployment; this keeps the protection on and only
     * opens the two host suffixes the preview actually uses.
     */
    allowedHosts: [".e2b.app", ".e2b.dev"],
    proxy: {
      "/api": {
        target: "https://localhost:7100",
        changeOrigin: true,
        secure: false
      }
    }
  },
  // `vite preview` applies the same check, so keep the two in step.
  preview: {
    allowedHosts: [".e2b.app", ".e2b.dev"]
  }
});
