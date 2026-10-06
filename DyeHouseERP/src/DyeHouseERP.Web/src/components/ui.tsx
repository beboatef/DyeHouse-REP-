import { PropsWithChildren, forwardRef } from "react";
import { AnimatedNumber, revealStyle, staggerDelay } from "./motion";

/**
 * Shared UI primitives.
 *
 * These wrap the component classes in index.css rather than restating padding
 * and radius inline, so a change to the design tokens updates all 39 modules
 * at once. The exported names and props are unchanged from the previous
 * version, so existing pages keep working untouched.
 */

export function PageHeader({
  title,
  subtitle,
  action
}: {
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="mb-5 flex flex-col gap-3 sm:mb-6 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <h1 className="page-title">{title}</h1>
        {subtitle && <p className="page-subtitle">{subtitle}</p>}
      </div>
      {action && <div className="flex shrink-0 flex-wrap items-center gap-2">{action}</div>}
    </div>
  );
}

export function Card({
  children,
  className = "",
  revealIndex,
  delay,
  hover = false
}: PropsWithChildren<{
  className?: string;
  /** Position in a staggered group; when set the card fades/rises in. */
  revealIndex?: number;
  /** Explicit reveal delay in ms (overrides `revealIndex`). */
  delay?: number;
  /** Enable the subtle hover elevation (summary/KPI tiles, not data panels). */
  hover?: boolean;
}>) {
  const motion = revealIndex !== undefined || delay !== undefined;
  return (
    <div
      className={`card ${hover ? "card-hover" : ""} ${motion ? "motion-rise" : ""} ${className}`}
      style={motion ? revealStyle(delay ?? staggerDelay(revealIndex ?? 0)) : undefined}
    >
      {children}
    </div>
  );
}

type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm" | "md" | "lg";
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { children, variant = "primary", size = "md", className = "", type = "button", ...props },
  ref
) {
  const variantClass = {
    primary: "btn-primary",
    secondary: "btn-secondary",
    ghost: "btn-ghost",
    danger: "btn-danger"
  }[variant];
  const sizeClass = { sm: "btn-sm", md: "", lg: "btn-lg" }[size];

  return (
    <button ref={ref} type={type} {...props} className={`btn ${variantClass} ${sizeClass} ${className}`}>
      {children}
    </button>
  );
});

export type BadgeTone = "neutral" | "success" | "warning" | "danger" | "info" | "brand";

/**
 * The tone-name aliases the pages already use. They all resolve to the five real
 * tones, so `green` and `success` can never render differently - the table is the
 * single mapping point.
 *
 * `brand` and `amber` were previously missing from this map and silently fell
 * through to `badge-neutral`, so a brand or amber status badge rendered grey.
 */
const BADGE_TONE_CLASS: Record<string, string> = {
  neutral: "badge-neutral",
  success: "badge-success",
  warning: "badge-warning",
  danger: "badge-danger",
  info: "badge-info",
  brand: "badge-brand",
  // legacy aliases
  gray: "badge-neutral",
  green: "badge-success",
  yellow: "badge-warning",
  amber: "badge-warning",
  red: "badge-danger",
  blue: "badge-info"
};

export function Badge({
  children,
  tone = "neutral"
}: PropsWithChildren<{ tone?: BadgeTone | "gray" | "green" | "yellow" | "amber" | "red" | "blue" }>) {
  return <span className={`badge ${BADGE_TONE_CLASS[tone] ?? "badge-neutral"}`}>{children}</span>;
}

/**
 * Page- and form-level message banner. Replaces the ad-hoc
 * `<p className="text-sm text-red-600 mt-3">` that appears ~40 times across the
 * pages, so every error in the app has one shape, one size and one colour.
 */
/**
 * Written as literal strings, not `alert-${variant}`: Tailwind's content scanner
 * is a static text extractor, so a class name assembled at runtime would never
 * be seen and the rule would be purged from the stylesheet. Same reason
 * TILE_CLASS below is literal.
 */
const ALERT_VARIANT_CLASS = {
  info: "alert-info",
  success: "alert-success",
  warning: "alert-warning",
  danger: "alert-danger"
} as const;

export function Alert({
  variant = "danger",
  icon,
  children,
  className = ""
}: PropsWithChildren<{
  variant?: "info" | "success" | "warning" | "danger";
  icon?: React.ReactNode;
  className?: string;
}>) {
  return (
    <div
      className={`alert ${ALERT_VARIANT_CLASS[variant]} ${className}`}
      role={variant === "danger" ? "alert" : "status"}
    >
      {icon && <span className="mt-0.5 shrink-0">{icon}</span>}
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}

export const Input = forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  function Input({ className = "", ...props }, ref) {
    return <input ref={ref} {...props} className={`input ${className}`} />;
  }
);

export const Select = forwardRef<HTMLSelectElement, React.SelectHTMLAttributes<HTMLSelectElement>>(
  function Select({ className = "", children, ...props }, ref) {
    return (
      <select ref={ref} {...props} className={`select ${className}`}>
        {children}
      </select>
    );
  }
);

export const Textarea = forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  function Textarea({ className = "", ...props }, ref) {
    return <textarea ref={ref} {...props} className={`textarea ${className}`} />;
  }
);

/** Label + control + error, so form spacing is identical everywhere. */
export function Field({
  label,
  required,
  error,
  children
}: PropsWithChildren<{ label: string; required?: boolean; error?: string }>) {
  return (
    <div>
      <label className="field-label">
        {label}
        {required && (
          <span className="ms-1 text-danger" aria-hidden="true">
            *
          </span>
        )}
      </label>
      {children}
      {error && <p className="field-error">{error}</p>}
    </div>
  );
}

/* ===========================================================================
   SHARED SUMMARY / STATE PRIMITIVES
   ---------------------------------------------------------------------------
   These carry the dashboard's approved visual language to every module page.
   They live here rather than in each page for the same reason `.card` and
   `.table` do: one definition, so density and colour cannot drift between the
   40 modules.
   =========================================================================== */

export type Tone = "brand" | "success" | "warning" | "danger" | "info" | "neutral";

export const toneClass: Record<Tone, string> = {
  brand: "bg-brand-50 text-brand-700 dark:bg-brand-900/50 dark:text-brand-200",
  success: "bg-success-soft text-success-ink",
  warning: "bg-warning-soft text-warning-ink",
  danger: "bg-danger-soft text-danger-ink",
  info: "bg-info-soft text-info-ink",
  neutral: "bg-surface-sunken text-ink-muted"
};

/** Thousands-separated integer for on-screen figures (never for stored values). */
const numberFormat = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
export const fmt = (value: number | string) =>
  typeof value === "number" ? numberFormat.format(value) : value;

/**
 * Headline figure card: muted label, large number, optional footnote and a tonal
 * icon badge. The reference design's primary card.
 */
export function KpiCard({
  label, value, footnote, tone = "brand", icon, children,
  revealIndex, delay, countUp = false
}: {
  label: string; value: string | number; footnote?: string;
  tone?: Tone; icon?: React.ReactNode; children?: React.ReactNode;
  /** Position in a staggered group; when set the card fades/rises in. */
  revealIndex?: number;
  /** Explicit reveal delay in ms (overrides `revealIndex`). */
  delay?: number;
  /** Count the figure up from 0 on entry (headline stats only). */
  countUp?: boolean;
}) {
  const motion = revealIndex !== undefined || delay !== undefined;
  return (
    <div
      className={`card card-hover p-4 sm:p-5 ${motion ? "motion-rise" : ""}`}
      style={motion ? revealStyle(delay ?? staggerDelay(revealIndex ?? 0)) : undefined}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="kpi-label truncate">{label}</div>
          <div className="kpi-value ltr-nums">
            {countUp && typeof value === "number" ? (
              <AnimatedNumber value={value} format={(n) => fmt(n)} />
            ) : (
              fmt(value)
            )}
          </div>
        </div>
        {icon && <div className={`kpi-icon ${toneClass[tone]}`}>{icon}</div>}
      </div>
      {footnote && <div className="kpi-footnote truncate">{footnote}</div>}
      {children}
    </div>
  );
}

/**
 * The four tile colours as complete literal class names.
 *
 * Tailwind's content scanner is a static text extractor, so a runtime-built
 * `quick-tile-${index}` would never be seen and the colour classes would be
 * purged from the stylesheet. Naming them literally keeps them in the build.
 */
export const TILE_CLASS = {
  1: "quick-tile-1",
  2: "quick-tile-2",
  3: "quick-tile-3",
  4: "quick-tile-4"
} as const;

/** Solid coloured quick-stat tile from the reference strip. */
export function QuickTile({ index, icon, label, value, revealIndex, delay }: {
  index: 1 | 2 | 3 | 4; icon: React.ReactNode; label: string; value: string | number;
  /** Position in a staggered group; when set the tile fades/rises in. */
  revealIndex?: number;
  /** Explicit reveal delay in ms (overrides `revealIndex`). */
  delay?: number;
}) {
  const motion = revealIndex !== undefined || delay !== undefined;
  return (
    <div
      className={`quick-tile ${TILE_CLASS[index]} ${motion ? "motion-rise" : ""}`}
      style={motion ? revealStyle(delay ?? staggerDelay(revealIndex ?? 0)) : undefined}
    >
      <div className="quick-tile-icon">{icon}</div>
      <div className="min-w-0">
        <div className="quick-tile-label truncate">{label}</div>
        <div className="quick-tile-value ltr-nums truncate">{fmt(value)}</div>
      </div>
    </div>
  );
}

/** Table shell that scrolls horizontally inside itself, never the page. */
export function TableWrap({ children, className = "" }: PropsWithChildren<{ className?: string }>) {
  return <div className={`table-wrap ${className}`}>{children}</div>;
}

/**
 * Pulls the API's problem response into a readable sentence. The API returns
 * RFC7807-shaped payloads (title/detail), so a raw `error.message` would hide
 * the actual business reason behind "Request failed with status code 422".
 */
export function errorMessage(error: unknown, fallback: string): string {
  const data = (error as { response?: { data?: { title?: string; detail?: string; message?: string } } })
    ?.response?.data;
  return data?.detail ?? data?.title ?? data?.message ?? fallback;
}

/** Full-card empty/placeholder block, for regions rather than table bodies. */
export function EmptyState({
  title, text, icon, action
}: {
  title: string; text?: string; icon?: React.ReactNode; action?: React.ReactNode;
}) {
  return (
    <div className="empty-state motion-rise">
      {icon && <div className="text-ink-subtle">{icon}</div>}
      <div className="empty-state-title">{title}</div>
      {text && <div className="empty-state-text">{text}</div>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}

/**
 * One <tr> that expresses loading / error / empty for a table body, so every
 * list in the app reports those three states the same way instead of each page
 * inventing its own wording and colour.
 */
export function TableStateRow({
  colSpan, loading, error, isEmpty, emptyText, loadingText = "..."
}: {
  colSpan: number; loading?: boolean; error?: unknown;
  isEmpty?: boolean; emptyText?: string; loadingText?: string;
}) {
  if (loading) {
    // A shimmer placeholder instead of bare text: it keeps the table's final
    // height guessable and reads as "data is on its way" rather than "empty".
    // One transform-only sweep - deliberately not a per-cell pulse, which on a
    // dense grid looks like a flicker.
    return (
      <tr>
        <td colSpan={colSpan} className="px-4 py-6">
          <div className="mx-auto flex max-w-md flex-col items-center gap-2" role="status">
            <span className="skeleton block h-3.5 w-full" />
            <span className="skeleton block h-3.5 w-2/3" />
            <span className="sr-only">{loadingText}</span>
          </div>
        </td>
      </tr>
    );
  }

  if (error) {
    return (
      <tr>
        <td colSpan={colSpan} className="px-4 py-6 text-center text-danger">
          {errorMessage(error, "—")}
        </td>
      </tr>
    );
  }

  if (isEmpty) {
    return (
      <tr>
        <td colSpan={colSpan} className="px-4 py-8 text-center text-ink-subtle">
          {emptyText}
        </td>
      </tr>
    );
  }

  return null;
}
