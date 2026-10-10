import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { AuthApi, SettingsApi } from "@/api/client";
import { Button, Input } from "@/components/ui";
import BrandLogo from "@/components/BrandLogo";
import { useTheme, type Theme } from "@/components/ThemeProvider";
import { useI18n } from "@/i18n";
import {
  Factory, User, Lock, AlertCircle, Sun, Moon, Monitor, Languages,
  CheckCircle2
} from "lucide-react";

/**
 * Login screen (spec sections 43 + 40).
 *
 * Presentation only: the mutation, the credentials, the token storage and the
 * navigation target are untouched. Every user-facing string now comes from the
 * i18n dictionary in both languages (previously this screen carried hardcoded
 * Arabic literals and had no language switch, so an English user could not
 * switch language before signing in), and the screen carries the same
 * Premium Industrial identity as the shell: a brand panel on the large
 * breakpoint and the form card on the small one.
 */
export default function LoginPage() {
  const navigate = useNavigate();
  const { t, lang, setLang } = useI18n();
  // No default username is pre-filled: the UI must never hint at any real or
  // seeded credential - the user types their own login.
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const { theme, setTheme } = useTheme();

  const { data: settings } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  const loginMutation = useMutation({
    mutationFn: () => AuthApi.login(username, password),
    onSuccess: (result) => {
      localStorage.setItem("dyehouse_token", result.token);
      localStorage.setItem("dyehouse_user", JSON.stringify({ username: result.username, displayName: result.displayName, roles: result.roles }));
      navigate("/");
    },
    onError: () => setError(t("login.error"))
  });

  const themeOptions: { value: Theme; icon: React.ReactNode; label: string }[] = [
    { value: "light", icon: <Sun size={15} />, label: t("theme.light", "Light") },
    { value: "dark", icon: <Moon size={15} />, label: t("theme.dark", "Dark") },
    { value: "system", icon: <Monitor size={15} />, label: t("theme.system", "System") }
  ];

  const features = [t("login.feature1"), t("login.feature2"), t("login.feature3")];

  const companyName =
    (lang === "ar" ? settings?.companyNameAr : settings?.companyNameEn) ||
    settings?.companyNameAr ||
    "DyeHouse ERP";

  return (
    <div className="flex min-h-screen bg-surface-sunken">
      {/* ---- Brand / industrial panel (large screens only) ---- */}
      <aside
        className="relative hidden w-1/2 max-w-2xl flex-col justify-between overflow-hidden border-e border-brand-800/40 bg-brand-700 p-10 text-white lg:flex"
        aria-hidden="true"
      >
        {/* Lightweight industrial grid: one CSS background, no blur, no images. */}
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.14]"
          style={{
            backgroundImage:
              "linear-gradient(rgb(255 255 255 / 0.6) 1px, transparent 1px), linear-gradient(90deg, rgb(255 255 255 / 0.6) 1px, transparent 1px)",
            backgroundSize: "44px 44px"
          }}
        />
        <div className="relative">
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-white/15 ring-1 ring-inset ring-white/25">
              {settings?.logoDataUrl ? (
                <img src={settings.logoDataUrl} alt="" className="h-full w-full object-contain p-1.5" />
              ) : (
                <Factory size={22} />
              )}
            </div>
            <div>
              <div className="text-base font-bold leading-tight">{companyName}</div>
              <div className="mt-0.5 text-xs text-white/70">{t("app.subtitle")}</div>
            </div>
          </div>

          <p className="mt-12 max-w-md text-2xl font-bold leading-snug">{t("login.highlight")}</p>
          <ul className="mt-6 space-y-3">
            {features.map((f) => (
              <li key={f} className="flex items-start gap-2.5 text-sm text-white/85">
                <CheckCircle2 size={17} className="mt-0.5 shrink-0 text-white" />
                <span>{f}</span>
              </li>
            ))}
          </ul>
        </div>
        <p className="relative text-2xs text-white/60">© {new Date().getFullYear()} {companyName}</p>
      </aside>

      {/* ---- Form column ---- */}
      {/* `pt-16` reserves a band for the absolutely positioned language/theme
          controls, so the card can never slide up under them on a short
          viewport and touch the logo. The centring happens in the space below
          that band. */}
      <div className="relative flex flex-1 flex-col items-center justify-center p-4 pt-16 sm:p-8 sm:pt-16">
        {/* Screen-level controls: language + theme, both persisted the same way
            the shell persists them. */}
        <div className="absolute end-4 top-4 flex items-center gap-2">
          <button
            type="button"
            onClick={() => setLang(lang === "ar" ? "en" : "ar")}
            className="btn btn-secondary btn-sm"
            aria-label={t("login.language", "Language")}
            title={t("login.language", "Language")}
          >
            <Languages size={15} />
            <span className="text-2xs font-semibold">{lang === "ar" ? "EN" : "ع"}</span>
          </button>
          <div className="flex items-center overflow-hidden rounded-lg border border-line-strong bg-surface p-0.5">
            {themeOptions.map((o) => (
              <button
                key={o.value}
                onClick={() => setTheme(o.value)}
                className={`flex h-7 w-7 items-center justify-center rounded-md transition-colors ${
                  theme === o.value ? "bg-brand-600 text-white" : "text-ink-muted hover:bg-surface-sunken"
                }`}
                aria-label={o.label}
                aria-pressed={theme === o.value}
                title={o.label}
              >
                {o.icon}
              </button>
            ))}
          </div>
        </div>

        {/* The login card is outside <Layout>, so it opts into the same entrance
            as every routed page: fade + a subtle rise. */}
        <div className="card card-pad motion-rise w-full max-w-sm">
          {/* Product mark, then straight to the sign-in line: the old
              placeholder block (the tinted square with the factory icon, and
              the system-name title under it) was removed on request, so this
              is the only line between the logo and the form. */}
          <div className="mb-6 pt-2 text-center">
            <div className="mb-6 flex justify-center">
              <BrandLogo variant="full" width={228} />
            </div>
            <div className="text-xs text-ink-muted">{t("login.signIn")}</div>
          </div>

          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              setError(null);
              loginMutation.mutate();
            }}
          >
            <div>
              <label htmlFor="login-username" className="field-label">
                {t("login.username")}
              </label>
              <div className="relative">
                <User size={16} className="pointer-events-none absolute inset-y-0 start-3 my-auto text-ink-subtle" />
                <Input
                  id="login-username"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  required
                  autoFocus
                  autoComplete="username"
                  className="ps-9"
                />
              </div>
            </div>

            <div>
              <label htmlFor="login-password" className="field-label">
                {t("login.password")}
              </label>
              <div className="relative">
                <Lock size={16} className="pointer-events-none absolute inset-y-0 start-3 my-auto text-ink-subtle" />
                <Input
                  id="login-password"
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  autoComplete="current-password"
                  className="ps-9"
                />
              </div>
            </div>

            <Button type="submit" className="w-full" size="lg" disabled={loginMutation.isPending}>
              {loginMutation.isPending ? t("login.submitting") : t("login.submit")}
            </Button>

            {error && (
              <p
                role="alert"
                className="motion-rise flex items-center justify-center gap-1.5 rounded-lg bg-danger-soft px-3 py-2 text-center text-sm text-danger-ink"
              >
                <AlertCircle size={15} />
                {error}
              </p>
            )}
          </form>

          <p className="mt-6 text-center text-2xs text-ink-subtle">{t("login.hint")}</p>
        </div>
      </div>
    </div>
  );
}
