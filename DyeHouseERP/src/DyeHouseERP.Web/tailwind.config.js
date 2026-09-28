/**
 * Tailwind configuration for the DyeHouse ERP design system.
 *
 * The palette resolves to CSS variables instead of literal hex values. That is
 * the single most important decision in this file: the existing pages were
 * written against Tailwind's stock `gray-*` / `white` / `slate-*` names, so
 * remapping those NAMES to theme-aware variables makes every one of those
 * existing classes follow the light/dark theme automatically. Without this,
 * dark mode would mean hand-editing `dark:` variants into ~40 pages.
 *
 * IMPORTANT - why `rgb(var(--x-rgb) / <alpha-value>)` and not `var(--x)`:
 * Tailwind can only inject an opacity modifier (`bg-white/60`) into a colour
 * it can parse as channels. With a bare `var(--x)` it silently emits nothing
 * for those classes, so `bg-white/60` would vanish. The `-rgb` channel
 * variables plus `<alpha-value>` keep all 14 opacity-modified classes in the
 * codebase working. Verified with a real Tailwind build, not assumed.
 *
 * The variable values live in src/index.css.
 *
 * @type {import('tailwindcss').Config}
 */

/** `rgb(var(--token-rgb) / <alpha-value>)` - channel form so `/opacity` works. */
const token = (name) => `rgb(var(--${name}-rgb) / <alpha-value>)`;

export default {
  // `class` (not `media`) so the user can force light or dark explicitly,
  // independently of the OS setting.
  darkMode: "class",
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        // --- Surfaces -------------------------------------------------------
        surface: {
          DEFAULT: token("surface"),
          sunken: token("surface-sunken"),
          raised: token("surface-raised"),
          overlay: token("surface-overlay")
        },
        // --- Text -----------------------------------------------------------
        ink: {
          DEFAULT: token("ink"),
          muted: token("ink-muted"),
          subtle: token("ink-subtle"),
          inverse: token("ink-inverse")
        },
        // --- Lines ----------------------------------------------------------
        line: {
          DEFAULT: token("line"),
          strong: token("line-strong")
        },
        // --- Brand ----------------------------------------------------------
        // A calm industrial blue: desaturated enough to sit behind dense data
        // without shouting, dark enough to stay legible on light surfaces.
        brand: {
          50: token("brand-50"),
          100: token("brand-100"),
          200: token("brand-200"),
          300: token("brand-300"),
          400: token("brand-400"),
          500: token("brand-500"),
          600: token("brand-600"),
          700: token("brand-700"),
          800: token("brand-800"),
          900: token("brand-900")
        },
        // --- Status ---------------------------------------------------------
        // Semantic rather than literal, so a "danger" badge is correct in both
        // themes and can never drift to an unreadable tint-on-dark.
        success: { DEFAULT: token("success"), soft: token("success-soft"), ink: token("success-ink") },
        warning: { DEFAULT: token("warning"), soft: token("warning-soft"), ink: token("warning-ink") },
        danger: { DEFAULT: token("danger"), soft: token("danger-soft"), ink: token("danger-ink") },
        info: { DEFAULT: token("info"), soft: token("info-soft"), ink: token("info-ink") },

        // --- Backwards-compatible aliases -----------------------------------
        // The pre-redesign pages use these stock names heavily. Pointing them
        // at variables is what makes the existing 39 pages theme-aware with no
        // per-page edits.
        white: token("surface"),
        black: token("ink"),
        gray: {
          50: token("surface-sunken"),
          100: token("surface-sunken"),
          200: token("line"),
          300: token("line-strong"),
          400: token("ink-subtle"),
          500: token("ink-muted"),
          600: token("ink-muted"),
          700: token("ink"),
          800: token("ink"),
          900: token("ink")
        },
        slate: {
          50: token("surface-sunken"),
          100: token("surface-sunken"),
          200: token("line"),
          300: token("line-strong"),
          400: token("ink-subtle"),
          500: token("ink-muted"),
          600: token("ink-muted"),
          700: token("ink"),
          800: token("ink"),
          900: token("ink")
        },
        // Raw palette kept for the few places that need a literal red/amber
        // (e.g. validation text). Still variable-backed so it themes.
        red: { 50: token("danger-soft"), 100: token("danger-soft"), 600: token("danger"), 700: token("danger-ink") },
        green: { 50: token("success-soft"), 100: token("success-soft"), 600: token("success"), 700: token("success-ink") },
        amber: { 50: token("warning-soft"), 100: token("warning-soft"), 600: token("warning"), 700: token("warning-ink") },
        blue: { 50: token("info-soft"), 100: token("info-soft"), 600: token("info"), 700: token("info-ink") }
      },
      fontFamily: {
        sans: ["Cairo", "system-ui", "sans-serif"]
      },
      borderRadius: {
        sm: "var(--radius-sm)",
        DEFAULT: "var(--radius)",
        md: "var(--radius)",
        lg: "var(--radius-lg)",
        xl: "var(--radius-lg)"
      },
      boxShadow: {
        card: "var(--shadow-card)",
        pop: "var(--shadow-pop)"
      },
      fontSize: {
        "2xs": ["0.6875rem", { lineHeight: "1rem" }] // 11px - the floor for dense tables
      },
      maxWidth: {
        content: "80rem" // replaces the ad-hoc max-w-7xl
      },
      screens: {
        // The shell switches to a drawer at `lg` (1024px). `md` (768px) stays
        // available for tablet-specific layout inside pages.
        xs: "420px"
      },
      keyframes: {
        "fade-in": { from: { opacity: "0" }, to: { opacity: "1" } },
        // Direction-aware: the drawer slides in from the inline-start edge, so
        // it is correct in both RTL and LTR.
        "drawer-in": {
          from: { transform: "translateX(var(--slide-from, 100%))" },
          to: { transform: "translateX(0)" }
        },
        "fade-in-overlay": { from: { opacity: "0" }, to: { opacity: "1" } }
      },
      animation: {
        "fade-in": "fade-in 150ms ease-out",
        "drawer-in": "drawer-in 220ms cubic-bezier(0.32, 0.72, 0, 1)"
      }
    }
  },
  plugins: []
};
