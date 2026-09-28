import { Link, useLocation } from "react-router-dom";
import { useI18n } from "@/i18n";
import { Button, Card } from "@/components/ui";

/**
 * Catch-all for unknown URLs.
 *
 * Without this, any mistyped path, stale bookmark or renamed screen rendered a
 * completely blank page inside the app shell - no message, no navigation, no
 * way back. That is a dead end in an ERP where printed document links and QR
 * codes are a normal way to move around.
 *
 * It sits outside <RequireAuth> deliberately: a signed-out user following a
 * bad link should be offered the sign-in page, not bounced between guards.
 */
export default function NotFoundPage() {
  const { t, pick } = useI18n();
  const location = useLocation();

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 p-6">
      <Card className="max-w-md w-full text-center p-8">
        <div className="text-5xl font-bold text-gray-200 ltr-nums">404</div>
        <h1 className="mt-3 text-lg font-semibold text-gray-800">{pick("الصفحة غير موجودة", "Page not found")}</h1>
        <p className="mt-2 text-sm text-gray-500">
          {pick("الرابط الذي طلبته غير صحيح أو تم تغيير الصفحة.", "The link you requested is invalid, or the screen has moved.")}
        </p>
        <p className="mt-3 text-xs text-gray-400 ltr-nums break-all" dir="ltr">
          {location.pathname}
        </p>
        <div className="mt-6 flex flex-wrap items-center justify-center gap-2">
          <Link to="/">
            <Button>{t("nav.dashboard")}</Button>
          </Link>
          <Link to="/login">
            <Button variant="secondary">{pick("تسجيل الدخول", "Sign in")}</Button>
          </Link>
        </div>
      </Card>
    </div>
  );
}
