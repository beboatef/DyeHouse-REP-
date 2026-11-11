import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation } from "@tanstack/react-query";
import { AuthApi } from "@/api/client";
import { Button, Card, Input, PageHeader } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Forced/voluntary password change (Part 2).
 *
 * Reached automatically: login routes here when `mustChangePassword` is set, and the API
 * client sends any flagged session here on the gate's 403. The client-side policy check
 * mirrors the server's PasswordPolicy with the SAME messages (password.*) - the server
 * re-checks everything, including history, which only it can see.
 */
function policyError(username: string, next: string, t: (k: string) => string): string | null {
  if (next.length < 12) return t("password.tooShort");
  if (next.length > 200) return t("password.tooLong");
  if (username && next.toLowerCase().includes(username.trim().toLowerCase())) return t("password.containsUsername");
  return null;
}

export default function ChangePasswordPage() {
  const navigate = useNavigate();
  const { t } = useI18n();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);

  const username = (() => {
    try {
      return (JSON.parse(localStorage.getItem("dyehouse_user") ?? "{}") as { username?: string }).username ?? "";
    } catch {
      return "";
    }
  })();

  const mutation = useMutation({
    mutationFn: () => AuthApi.changeOwnPassword({ currentPassword, newPassword }),
    onSuccess: () => navigate("/", { replace: true }),
    onError: (err: unknown) => {
      const data = (err as { response?: { data?: { errors?: Record<string, string[]>; title?: string } } })?.response?.data;
      const first = data?.errors ? Object.values(data.errors).flat()[0] : undefined;
      setError(first ?? data?.title ?? t("login.error"));
    }
  });

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (newPassword !== confirm) return setError(t("changepw.mismatch"));
    if (newPassword === currentPassword) return setError(t("changepw.sameAsCurrent"));
    const policy = policyError(username, newPassword, t);
    if (policy) return setError(policy);
    setError(null);
    mutation.mutate();
  };

  return (
    <>
      <PageHeader title={t("changepw.title")} subtitle={t("changepw.required")} />
      <Card className="card-pad max-w-lg">
        <form className="space-y-4" onSubmit={submit}>
          <div>
            <label className="field-label">{t("changepw.current")}</label>
            <Input type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} required autoComplete="current-password" />
          </div>
          <div>
            <label className="field-label">{t("changepw.new")}</label>
            <Input type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} required minLength={12} autoComplete="new-password" />
          </div>
          <div>
            <label className="field-label">{t("changepw.confirm")}</label>
            <Input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required autoComplete="new-password" />
          </div>
          <div className="flex gap-2">
            <Button type="submit" disabled={mutation.isPending}>{mutation.isPending ? t("changepw.submitting") : t("changepw.submit")}</Button>
            <Button type="button" variant="secondary" onClick={() => { void AuthApi.logout().finally(() => { localStorage.removeItem("dyehouse_user"); navigate("/login", { replace: true }); }); }}>
              {t("changepw.logout")}
            </Button>
          </div>
          {error && <p className="form-error">{error}</p>}
        </form>
      </Card>
    </>
  );
}
