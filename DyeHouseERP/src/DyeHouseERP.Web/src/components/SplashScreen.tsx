import { useEffect, useMemo, useState } from "react";
import BrandLogo from "@/components/BrandLogo";
import { prefersReducedMotion } from "@/components/motion";
import { useI18n } from "@/i18n";

/**
 * First-load splash: the DyeHouse ERP brand logo plays its animation over a
 * full-screen surface, with a thin progress line underneath, then the whole
 * overlay fades and scales out and unmounts.
 *
 * Decisions worth knowing about:
 *
 * * ONCE PER SESSION, decided before the first paint. `shouldShow` is evaluated
 *   at module load and writes the sessionStorage flag immediately, so the
 *   decision cannot depend on a render or an effect ordering, and a reload
 *   inside the same tab never replays it. It is also mounted outside <Routes>
 *   (see App.tsx), so a client-side navigation cannot remount it either.
 *
 * * NO LAYOUT SHIFT. `.splash` is `position: fixed; inset: 0` (see
 *   src/index.css), so it takes no space in the document flow and the app
 *   underneath does not move when it disappears.
 *
 * * `ready` is the "the app has bootstrapped" signal. It defaults to true
 *   because this app has no blocking bootstrap step: RequireAuth is a
 *   synchronous localStorage read, and the shell's `company-settings` query is
 *   non-blocking (every screen renders its own loading/error state). Gating the
 *   splash on that query would mean holding the whole UI back behind a request
 *   that the UI is designed to render without - particularly bad on a slow or
 *   unreachable API, where the fetch only settles after its retry. The prop
 *   exists so a real bootstrap step can be wired in later without touching the
 *   timing logic here.
 *
 * * MINIMUM HOLD. The artwork takes ~2.4s to finish, but waiting that long
 *   before showing the app is a poor trade on every repeat visit, so the hold
 *   is 1.8s: long enough for the outline, the dye fill, the text and the start
 *   of the wave flow to read. Under reduced motion there is no animation to
 *   show, so the hold drops to 700ms.
 */

const SESSION_KEY = "dyehouse_splash_shown";

const MIN_VISIBLE_MS = 1800;
const MIN_VISIBLE_REDUCED_MS = 700;

/** Must stay in sync with `.splash__leaving` in index.css (--motion-page). */
const EXIT_MS = 300;

/**
 * Read-and-claim the session flag EXACTLY ONCE per document load.
 *
 * This has to be a module-level evaluation rather than a call inside the
 * component: React 18 StrictMode deliberately double-invokes state
 * initializers in development, so a `useState(() => shouldShow())` would read
 * the flag its own first run had just written and decide not to show the
 * splash - the overlay then worked in production but never appeared in dev.
 * Evaluating at module scope runs once, before React renders, and is immune to
 * that. Storage can throw in private mode; failing open (show it once) is the
 * harmless outcome.
 */
const SPLASH_THIS_SESSION: boolean = (() => {
  try {
    if (sessionStorage.getItem(SESSION_KEY)) return false;
    sessionStorage.setItem(SESSION_KEY, "1");
    return true;
  } catch {
    return true;
  }
})();

type Phase = "visible" | "leaving" | "gone";

export default function SplashScreen({ ready = true }: { ready?: boolean }) {
  const { t, isRtl } = useI18n();
  const [phase, setPhase] = useState<Phase>(() => (SPLASH_THIS_SESSION ? "visible" : "gone"));
  const reduced = useMemo(() => prefersReducedMotion(), []);
  const [minElapsed, setMinElapsed] = useState(false);

  // The minimum hold. Never the only gate - the exit also waits for `ready`.
  useEffect(() => {
    const id = window.setTimeout(() => setMinElapsed(true), reduced ? MIN_VISIBLE_REDUCED_MS : MIN_VISIBLE_MS);
    return () => window.clearTimeout(id);
  }, [reduced]);

  // Start leaving as soon as the minimum has elapsed AND the app is ready.
  useEffect(() => {
    if (phase !== "visible" || !minElapsed || !ready) return;
    setPhase("leaving");
  }, [phase, minElapsed, ready]);

  // Then unmount once the fade-out has played.
  //
  // This is deliberately a SEPARATE effect keyed only on the phase: doing both
  // in one effect made the phase change re-run it, so its own cleanup cleared
  // the unmount timer it had just armed and the overlay stayed in the DOM (and
  // "leaving") forever.
  useEffect(() => {
    if (phase !== "leaving") return;
    const id = window.setTimeout(() => setPhase("gone"), EXIT_MS);
    return () => window.clearTimeout(id);
  }, [phase]);

  if (phase === "gone") return null;

  return (
    <div
      className={`splash no-print${phase === "leaving" ? " splash__leaving" : ""}`}
      // A loading surface, announced once - the artwork itself is decorative and
      // the progress line carries no information a screen reader can use.
      role="status"
      aria-label={t("common.loading")}
      // Direction-aware progress sweep (1 = LTR, -1 = RTL).
      style={{ ["--splash-dir" as string]: isRtl ? -1 : 1 }}
    >
      <BrandLogo variant="full" width={260} className="px-6" />
      <div className="splash__bar" aria-hidden="true" />
    </div>
  );
}
