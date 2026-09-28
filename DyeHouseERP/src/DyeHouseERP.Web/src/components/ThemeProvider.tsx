import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

/**
 * Light / Dark / System theme (spec section 6).
 *
 * The `dark` class is applied to <html> because tailwind.config.js sets
 * `darkMode: "class"` - the OS preference is only consulted in "system" mode,
 * so an explicit user choice is never silently overridden. The choice is
 * persisted in localStorage under the same key style as the language switch.
 *
 * There is a deliberate flash-prevention detail: the initial class is applied
 * by an inline script in index.html before React mounts, so a dark-mode user
 * does not get a white flash on every reload.
 */

export type Theme = "light" | "dark" | "system";

interface ThemeContextValue {
  theme: Theme;
  /** The theme actually in effect - "system" resolves to one of the two. */
  resolved: "light" | "dark";
  setTheme: (theme: Theme) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);
const STORAGE_KEY = "dyehouse_theme";

const readStored = (): Theme => {
  if (typeof localStorage === "undefined") return "light";
  const v = localStorage.getItem(STORAGE_KEY);
  return v === "light" || v === "dark" || v === "system" ? v : "light";
};

const systemPrefersDark = () =>
  typeof window !== "undefined" && typeof window.matchMedia === "function"
    ? window.matchMedia("(prefers-color-scheme: dark)").matches
    : false;

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readStored);
  const [resolved, setResolved] = useState<"light" | "dark">(() =>
    readStored() === "system" ? (systemPrefersDark() ? "dark" : "light") : (readStored() as "light" | "dark")
  );

  // Recompute when the user picks a mode, and keep following the OS while in
  // "system" mode.
  useEffect(() => {
    const next = theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme;
    setResolved(next);
    document.documentElement.classList.toggle("dark", next === "dark");
    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // private mode - the toggle still works for this session
    }
  }, [theme]);

  useEffect(() => {
    if (theme !== "system" || typeof window === "undefined" || !window.matchMedia) return;
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => {
      const next = mq.matches ? "dark" : "light";
      setResolved(next);
      document.documentElement.classList.toggle("dark", next === "dark");
    };
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, [theme]);

  const setTheme = useCallback((next: Theme) => setThemeState(next), []);

  const value = useMemo<ThemeContextValue>(() => ({ theme, resolved, setTheme }), [theme, resolved, setTheme]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used inside <ThemeProvider> (see src/main.tsx).");
  return ctx;
}
