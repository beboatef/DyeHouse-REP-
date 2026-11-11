/* DyeHouse ERP service worker - STATIC APP SHELL ONLY.
 *
 * CACHING RULE (do not weaken): this worker may cache the static application
 * shell - versioned JS/CSS bundles, fonts and icons. It must NEVER cache:
 *   * /api responses (live ledger and production data),
 *   * authentication cookies or session traffic,
 *   * any ledger document data.
 * API traffic is network-only: requests under /api bypass every cache below
 * and their responses are never put into any cache. Auth cookies ride on
 * those same /api requests, so they are never cached either.
 * src/sw.test.ts reads this file and fails the build if the never-cache
 * list stops matching /api.
 */

const SHELL_CACHE = "dyehouse-shell-v1";

// Prefixes that must never be served from, or written to, any cache.
const NEVER_CACHE_PREFIXES = ["/api"];

/** True for requests that must always hit the network (API, auth, data). */
function isNeverCached(request, url) {
  if (request.method !== "GET") return true;
  if (url.origin !== self.location.origin) return true;
  return NEVER_CACHE_PREFIXES.some(
    (prefix) => url.pathname === prefix || url.pathname.startsWith(prefix + "/")
  );
}

/** Static shell assets: hashed bundles, fonts, icons. Nothing else. */
function isShellAsset(url) {
  return /\.(?:js|css|woff2?|ttf|otf|png|svg|ico|webmanifest)$/.test(url.pathname);
}

self.addEventListener("install", () => {
  // Intentionally no skipWaiting here: the new worker waits until the user
  // accepts the refresh prompt (ServiceWorkerUpdatePrompt sends SKIP_WAITING),
  // so a deployment never swaps the running app out from under the worker.
});

self.addEventListener("message", (event) => {
  if (event.data === "SKIP_WAITING") void self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((key) => key !== SHELL_CACHE).map((key) => caches.delete(key)))
      )
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  // 1. API, auth and all non-GET traffic: network only, never cached.
  if (isNeverCached(event.request, url)) {
    event.respondWith(fetch(event.request));
    return;
  }

  // 2. Static shell assets: cache first (they are content-hashed per build),
  //    refreshed in the background so the next launch is instant.
  if (isShellAsset(url)) {
    event.respondWith(
      caches.open(SHELL_CACHE).then((cache) =>
        cache.match(event.request).then((hit) => {
          const network = fetch(event.request).then((response) => {
            if (response.ok) cache.put(event.request, response.clone());
            return response;
          });
          return hit || network;
        })
      )
    );
    return;
  }

  // 3. Everything else (notably index.html navigations): network only, so a
  //    new deployment is picked up instead of serving a stale shell.
  event.respondWith(fetch(event.request));
});
