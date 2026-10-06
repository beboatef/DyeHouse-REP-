import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQuery } from "@tanstack/react-query";
import { AuthApi, SettingsApi } from "@/api/client";
import { Button, Input } from "@/components/ui";
import { useTheme, type Theme } from "@/components/ThemeProvider";
import { Factory, User, Lock, AlertCircle, Sun, Moon, Monitor } from "lucide-react";

/**
 * Login screen.
 *
 * Presentation only: the mutation, the credentials, the token storage and the
 * navigation target are untouched. The only additions are the theme control
 * and the layout/typography, both from the design system.
 */
export default function LoginPage() {
  const navigate = useNavigate();
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
    onError: () => setError("اسم المستخدم أو كلمة المرور غير صحيحة")
  });

  const themeOptions: { value: Theme; icon: React.ReactNode; label: string }[] = [
    { value: "light", icon: <Sun size={15} />, label: "فاتح" },
    { value: "dark", icon: <Moon size={15} />, label: "داكن" },
    { value: "system", icon: <Monitor size={15} />, label: "النظام" }
  ];

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-surface-sunken p-4">
      <div className="absolute end-4 top-4 flex items-center gap-1 rounded-lg border border-line-strong bg-surface p-1">
        {themeOptions.map((o) => (
          <button
            key={o.value}
            onClick={() => setTheme(o.value)}
            className={`flex h-8 w-8 items-center justify-center rounded-md transition-colors ${
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

      {/* The login card is outside <Layout>, so it opts into the same entrance
          as every routed page: fade + a subtle rise. */}
      <div className="card card-pad motion-rise w-full max-w-sm">
        <div className="mb-6 text-center">
          <div className="mx-auto mb-3 flex h-14 w-14 items-center justify-center overflow-hidden rounded-xl bg-brand-50 dark:bg-brand-900/50">
            {settings?.logoDataUrl ? (
              <img src={settings.logoDataUrl} alt="" className="h-full w-full object-contain p-1.5" />
            ) : (
              <Factory size={26} className="text-brand-600" />
            )}
          </div>
          <div className="text-lg font-bold text-ink">{settings?.companyNameAr || "DyeHouse ERP"}</div>
          <div className="mt-1 text-xs text-ink-muted">تسجيل الدخول</div>
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
              اسم المستخدم
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
              كلمة المرور
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
            {loginMutation.isPending ? "جارٍ الدخول..." : "دخول"}
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

        <p className="mt-6 text-center text-2xs text-ink-subtle">أدخل اسم المستخدم وكلمة المرور الخاصة بك.</p>
      </div>
    </div>
  );
}
