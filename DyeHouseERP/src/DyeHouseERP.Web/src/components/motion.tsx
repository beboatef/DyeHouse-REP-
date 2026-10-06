import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type PropsWithChildren
} from "react";

/**
 * DyeHouse motion system (frontend only).
 *
 * The whole motion language lives in two places and nowhere else:
 *   * the `--motion-*` / easing tokens + `.motion-*` utilities in src/index.css
 *   * this file, for the handful of behaviours CSS cannot express
 *
 * Design rules this module exists to enforce:
 *   * only `opacity` and `transform` are ever animated - never width/height,
 *     so no animation can trigger a layout pass over a dense table;
 *   * durations are short (fast 130ms / normal 220ms / page 300ms) and the
 *     easing is a calm ease-out - no bounce, no elastic;
 *   * `prefers-reduced-motion` is honoured everywhere, including the JS
 *     count-up, which simply renders its final value instead of animating.
 *
 * The numbers below MUST stay in sync with the `--motion-*` tokens in
 * index.css; they are duplicated only because CSS variables cannot be read by
 * a requestAnimationFrame loop without a layout read.
 */

/** Durations in milliseconds, mirroring the `--motion-*` tokens in index.css. */
export const MOTION = {
  fast: 130,
  normal: 220,
  page: 300,
  stagger: 45
} as const;

/**
 * Stagger delay for the item at `index`, clamped at `max` slots so a long list
 * never makes the last item wait noticeably for its turn. A 40-card screen
 * therefore settles in the same time as an 8-card one.
 */
export const staggerDelay = (index: number, step: number = MOTION.stagger, max = 8): number =>
  Math.max(0, Math.min(index, max)) * step;

/** True when the user asked the OS to reduce motion. */
export function prefersReducedMotion(): boolean {
  return typeof window !== "undefined" && typeof window.matchMedia === "function"
    ? window.matchMedia("(prefers-reduced-motion: reduce)").matches
    : false;
}

/** The inline style that carries a reveal's stagger slot to the CSS class. */
export function revealStyle(delay = 0): CSSProperties {
  return { "--reveal-delay": `${delay}ms` } as CSSProperties;
}

/**
 * A staggered entrance wrapper: fade + 8px rise (`.motion-rise`), starting at
 * its `index`-th stagger slot unless an explicit `delay` is given.
 *
 * Use this where an extra element is harmless. Where the child is itself a
 * grid item whose own root must carry the animation (so the grid does not get
 * a nested wrapper), pass `revealIndex` to <Card>/<KpiCard>/<QuickTile>
 * instead - they apply the same class on their own root.
 */
export function Reveal({
  children,
  index = 0,
  delay,
  className = ""
}: PropsWithChildren<{ index?: number; delay?: number; className?: string }>) {
  return (
    <div className={`motion-rise ${className}`} style={revealStyle(delay ?? staggerDelay(index))}>
      {children}
    </div>
  );
}

/**
 * A KPI/stat figure that counts up to its value instead of appearing instantly.
 *
 * Deliberately NOT used on every number in the app - only headline stat cards,
 * where the movement draws the eye to a figure that just became available.
 * Table cells and document figures render their final value directly.
 *
 * Implementation notes:
 *   * one `requestAnimationFrame` loop per animating number, cancelled on
 *     unmount or when the target changes - never a permanently running loop;
 *   * the eased interpolation is a plain ease-out cubic, matching the CSS
 *     `--ease-out-soft` feel;
 *   * under `prefers-reduced-motion` it renders the target immediately and
 *     starts no loop at all.
 */
export function AnimatedNumber({
  value,
  format,
  duration = 700,
  className = ""
}: {
  value: number;
  /** Formats the in-flight fractional value. Defaults to a rounded en-US int. */
  format?: (n: number) => string;
  duration?: number;
  className?: string;
}) {
  const target = Number.isFinite(value) ? value : 0;
  const reduced = useMemo(() => prefersReducedMotion(), []);
  const [display, setDisplay] = useState<number>(() => (reduced ? target : 0));
  // Tracks the value currently painted, so an interrupted count-up resumes from
  // where it actually was instead of jumping back to zero.
  const painted = useRef<number>(reduced ? target : 0);

  useEffect(() => {
    if (reduced || duration <= 0) {
      painted.current = target;
      setDisplay(target);
      return;
    }
    const from = painted.current;
    if (from === target) return;

    let raf = 0;
    const start = performance.now();
    const tick = (now: number) => {
      // Clamp on BOTH sides, not just the upper bound. The first rAF timestamp
      // can predate the `performance.now()` captured above by up to one frame,
      // which made `progress` (and therefore the eased value, and therefore
      // `next`) negative - painting a negative figure such as "-4,526" for the
      // first frame or two of the count-up. Flooring at 0 removes that flash.
      const progress = Math.max(0, Math.min(1, (now - start) / duration));
      const eased = 1 - Math.pow(1 - progress, 3);
      const next = from + (target - from) * eased;
      painted.current = next;
      setDisplay(next);
      if (progress < 1) {
        raf = requestAnimationFrame(tick);
      } else {
        painted.current = target;
        setDisplay(target);
      }
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [target, duration, reduced]);

  const text = format
    ? format(display)
    : new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 }).format(display);

  return <span className={className}>{text}</span>;
}
