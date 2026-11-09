import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";

/*
 * Framework packages that every route needs, pulled into one "vendor" chunk.
 *
 * Why this list and not "everything in node_modules": Recharts, lucide-react
 * and the page-level libraries are only needed by SOME routes, and Rollup's
 * default splitting already keeps them in the per-page chunk that uses them
 * (see src/App.tsx). Naming every dependency here would hoist those into a
 * chunk that index.html preloads, so the dashboard would pay for the payroll
 * icons and the delivery page would pay for Recharts - the opposite of what
 * the route-level split is for.
 *
 * The trailing separator is load-bearing: without it `react` also matches
 * `react-smooth`, `react-is` and `react-transition-group`, which belong to
 * Recharts and would be dragged out of the dashboard chunk.
 */
const FRAMEWORK_VENDOR =
  /[\\/]node_modules[\\/](?:react|react-dom|scheduler|react-router|react-router-dom|@remix-run|@tanstack|axios)[\\/]/;

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
  },
  build: {
    /*
     * Vendor chunking on top of the per-route split. React, the router, React
     * Query and axios are needed by every route, so keeping them in the entry
     * made one 535 kB file that exceeded Rollup's warning limit and was
     * re-hashed (and re-downloaded) on every app-code change. Split out, they
     * cache independently of the app and every emitted file lands well under
     * the 500 kB line. Rollup still decides the rest, so a dependency shared
     * by only a few pages stays with those pages.
     */
    rollupOptions: {
      output: {
        manualChunks(id: string) {
          return FRAMEWORK_VENDOR.test(id) ? "vendor" : undefined;
        }
      }
    }
  }
});
