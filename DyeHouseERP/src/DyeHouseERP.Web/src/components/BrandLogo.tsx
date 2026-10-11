import { useId, type SVGProps } from "react";

/**
 * DyeHouse ERP - the PRODUCT logo.
 *
 * This is the mark for the system itself ("DyeHouse ERP"), not the per-tenant
 * company logo that a user uploads in Settings. That one comes from
 * `settings.logoDataUrl`, lives in the database and is rendered by Layout /
 * LoginPage / PrintPreviewLayout; nothing in this file reads, replaces or
 * reuses it.
 *
 * The SVG is INLINED rather than referenced with <img> for two reasons the
 * brief requires:
 *   * the animation is CSS inside the document, so an <img> would only ever
 *     show a frozen frame;
 *   * the wordmark's ink has to follow the app's own theme toggle, so it reads
 *     `--logo-ink` / `--logo-sub` (declared on `.dh-brand` in src/index.css)
 *     instead of the source SVG's prefers-color-scheme query.
 *
 * The markup is the provided artwork, unchanged: same paths, same gradient
 * stops (#06B6D4 -> #D946EF -> #F59E0B), same type sizes/positions. Only the
 * class names and the two gradient/clip ids were changed - the ids because the
 * logo can legitimately be on screen twice at once (splash over the login
 * card), and duplicate SVG ids make both instances render the wrong gradient.
 * The canonical file is public/dyehouse-erp-logo.svg.
 */

export type BrandLogoVariant = "full" | "icon";

/** The source SVG's own stack: Poppins, then Segoe UI/Arial as fallbacks. */
const FONT = "'Poppins', 'Segoe UI', Arial, sans-serif";

export interface BrandLogoProps {
  /** "full" = droplet + wordmark (520x140). "icon" = droplet only (120x140). */
  variant?: BrandLogoVariant;
  /** Rendered width in px; height follows the variant's aspect ratio. */
  width?: number;
  /**
   * true  - plays the draw/fill/wave/text entrance once (and keeps the wave
   *         flowing, as designed);
   * false - pins the final frame with no animation at all, for placements that
   *         must stay still (the shell header) and for print.
   */
  animated?: boolean;
  className?: string;
  /** Accessible name. Pass "" to mark it purely decorative. */
  label?: string;
}

export default function BrandLogo({
  variant = "full",
  width,
  animated = true,
  className = "",
  label = "DyeHouse ERP"
}: BrandLogoProps) {
  // Unique per instance, so the clip path and gradient of two logos on the same
  // page cannot collide.
  const uid = useId().replace(/[^a-zA-Z0-9]/g, "");
  const clipId = `dh-logo-drop-${uid}`;
  const gradientId = `dh-logo-erp-${uid}`;

  // React's SVG prop types omit `dir` (it is in the HTML types, not the SVG
  // ones), yet it is exactly what keeps the latin wordmark from mirroring in
  // Arabic RTL - so the attribute is applied through a typed spread rather than
  // left out. `.dh-brand` also sets `direction: ltr` in CSS as a second guard.
  const dirAttr = { dir: "ltr" } as unknown as SVGProps<SVGSVGElement>;

  const iconOnly = variant === "icon";
  const w = width ?? (iconOnly ? 40 : 260);
  const h = Math.round((w * 140) / (iconOnly ? 120 : 520));
  const decorative = label === "";

  return (
    <svg
      viewBox={iconOnly ? "0 0 120 140" : "0 0 520 140"}
      width={w}
      height={h}
      {...dirAttr}
      className={`dh-brand${animated ? "" : " dh-brand--static"} ${className}`.trim()}
      {...(decorative
        ? { "aria-hidden": true as const }
        : { role: "img", "aria-label": label })}
    >
      {!decorative && <title>{label}</title>}

      <defs>
        <clipPath id={clipId}>
          <path d="M60 10 C60 10 20 58 20 88 a40 40 0 0 0 80 0 C100 58 60 10 60 10 Z" />
        </clipPath>
        <linearGradient id={gradientId} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stopColor="#06B6D4" />
          <stop offset="0.5" stopColor="#D946EF" />
          <stop offset="1" stopColor="#F59E0B" />
        </linearGradient>
      </defs>

      <g clipPath={`url(#${clipId})`}>
        <g className="dh-brand__fill">
          <g className="dh-brand__wave dh-brand__wave--3">
            <path d="M-80 52 q20 -12 40 0 t40 0 t40 0 t40 0 t40 0 t40 0 t40 0 t40 0 V140 H-80 Z" fill="#06B6D4" />
          </g>
          <g className="dh-brand__wave dh-brand__wave--2">
            <path d="M-80 70 q20 -12 40 0 t40 0 t40 0 t40 0 t40 0 t40 0 t40 0 t40 0 V140 H-80 Z" fill="#D946EF" />
          </g>
          <g className="dh-brand__wave">
            <path d="M-80 88 q20 -12 40 0 t40 0 t40 0 t40 0 t40 0 t40 0 t40 0 t40 0 V140 H-80 Z" fill="#F59E0B" />
          </g>
        </g>
        <ellipse className="dh-brand__shine" cx="42" cy="78" rx="6" ry="16" transform="rotate(18 42 78)" />
      </g>

      <path
        className="dh-brand__outline"
        d="M60 10 C60 10 20 58 20 88 a40 40 0 0 0 80 0 C100 58 60 10 60 10 Z"
      />

      {!iconOnly && (
        <>
          <text
            className="dh-brand__t1"
            x="125"
            y="78"
            fontFamily={FONT}
            fontSize="46"
            fontWeight="700"
            fill="var(--logo-ink)"
          >
            Dye<tspan fontWeight="300">House</tspan>
          </text>
          <text
            className="dh-brand__t2"
            x="127"
            y="112"
            fontFamily={FONT}
            fontSize="30"
            fontWeight="800"
            letterSpacing="8"
            fill={`url(#${gradientId})`}
          >
            ERP
          </text>
          <text
            className="dh-brand__t3"
            x="232"
            y="111"
            fontFamily={FONT}
            fontSize="11"
            letterSpacing="2"
            fill="var(--logo-sub)"
          >
            TEXTILE DYEING &amp; FINISHING
          </text>
        </>
      )}
    </svg>
  );
}
