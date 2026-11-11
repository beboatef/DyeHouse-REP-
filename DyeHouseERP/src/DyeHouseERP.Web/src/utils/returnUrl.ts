/**
 * Safe post-login return path (Part 2: the scan flow).
 *
 * A QR scan opens /scan/:type/:id while signed out, so the sign-in screen
 * must send the user back to that same URL afterwards. The return value is
 * taken from the `returnUrl` query parameter, which is attacker-controlled
 * input on a shared device - it is accepted ONLY when it is a same-origin
 * application path. Anything else (absolute URL, protocol-relative URL,
 * javascript:, backslashes, a path that does not start with exactly one
 * slash) falls back to the dashboard root, so a crafted link can never turn
 * the login screen into an open redirect off this origin.
 */
export function resolveReturnUrl(raw: string | null | undefined): string {
  if (!raw) return "/";
  let decoded = raw;
  try {
    decoded = decodeURIComponent(raw);
  } catch {
    return "/";
  }
  // Exactly one leading slash, and no backslash anywhere (some browsers
  // treat `\/` as `/`, which would smuggle `//evil` past a naive check).
  if (!decoded.startsWith("/") || decoded.startsWith("//") || decoded.includes("\\")) return "/";
  // No scheme and no host: reject `javascript:`, `https:`, `//host`, ...
  if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(decoded)) return "/";
  if (decoded.includes("?")) {
    const [, query] = decoded.split("?", 2);
    if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(query)) return "/";
  }
  return decoded;
}

/** `/login?returnUrl=...` for the path the user was trying to reach. */
export function loginHref(target: string): string {
  return `/login?returnUrl=${encodeURIComponent(target)}`;
}
