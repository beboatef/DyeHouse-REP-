import { AlertTriangle, RotateCcw } from "lucide-react";
import { Button, errorMessage } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * "The request failed" - said as itself, never as an empty list.
 *
 * Why this component exists: a failed GET used to render the same "لا توجد بيانات" row as a
 * successful read that returned nothing, so a server outage, an expired session or a missing
 * permission all looked like "this warehouse is empty". The two states are different facts and the
 * screen has to say which one it is.
 *
 * The wording is chosen from what actually happened:
 *   * no HTTP response  -> the server could not be reached (offline, DNS, timeout, API down);
 *   * 401/403           -> session/permission problem, not missing data;
 *   * 404               -> the requested document is gone;
 *   * 5xx               -> the server failed, retry and then read its log;
 *   * anything else     -> the API's own message (title/detail), because that is the real reason.
 */

/** HTTP status of a failed request, or null when it never reached the server. */
function errorStatus(error: unknown): number | null {
  const status = (error as { response?: { status?: unknown } })?.response?.status;
  return typeof status === "number" ? status : null;
}

type Translate = (key: string, fallback?: string) => string;

function failureText(error: unknown, t: Translate): string {
  const status = errorStatus(error);

  if (status === null)
    return t("req.failed.network", "تعذّر الوصول إلى الخادم — تحقّق من الاتصال ثم أعد المحاولة.");
  if (status === 401)
    return t("req.failed.session", "انتهت الجلسة أو تغيّرت صلاحياتك — سجّل الدخول من جديد.");
  if (status === 403)
    return t("req.failed.forbidden", "لا تملك صلاحية عرض هذه البيانات.");
  if (status === 404)
    return t("req.failed.missing", "المطلوب غير موجود — قد يكون محذوفًا أو الرابط غير صحيح.");
  if (status >= 500)
    return t("req.failed.server", "الخادم تعذّر عليه إكمال الطلب — أعد المحاولة، وإن تكرر راجع سجل الخادم.");

  return errorMessage(error, t("req.failed.generic", "فشل الطلب — أعد المحاولة."));
}

/** A failed read as a card, with the optional retry that re-runs the same query. */
export default function RequestFailure({ error, title, onRetry, className = "" }: {
  error: unknown;
  /** What failed, in the caller's words. Defaults to the generic "could not load the data". */
  title?: string;
  onRetry?: () => void;
  className?: string;
}) {
  const { t } = useI18n();

  return (
    <div role="alert" className={`card card-pad bg-danger-soft ${className}`}>
      <div className="flex items-start gap-2">
        <AlertTriangle size={18} className="mt-0.5 shrink-0 text-danger-ink" />
        <div className="min-w-0">
          <div className="text-sm font-semibold text-danger-ink">
            {title ?? t("req.failed.title", "تعذّر تحميل البيانات")}
          </div>
          <div className="mt-1 text-sm text-danger-ink">{failureText(error, t)}</div>
          <div className="mt-1 text-2xs text-ink-muted">
            {t("req.failed.notEmpty", "هذه ليست قائمة فارغة: الطلب نفسه فشل، لذلك لا يمكن تأكيد وجود أي بيانات.")}
          </div>
        </div>
        {onRetry && (
          <Button variant="secondary" className="ms-auto shrink-0" onClick={onRetry}>
            <RotateCcw size={14} className="me-1" />
            {t("req.failed.retry", "إعادة المحاولة")}
          </Button>
        )}
      </div>
    </div>
  );
}

/**
 * The same failure inside a table body: a row that says the read failed, so a table with no rows on
 * screen can never be mistaken for a table whose data is genuinely empty.
 */
export function RequestFailureRow({ error, colSpan }: { error: unknown; colSpan: number }) {
  const { t } = useI18n();

  return (
    <tr>
      <td colSpan={colSpan} className="px-4 py-6 text-center">
        <div className="text-sm font-medium text-danger-ink">
          {t("req.failed.row", "تعذّر تحميل البيانات — وليست قائمة فارغة")}
        </div>
        <div className="mt-1 text-2xs text-ink-muted">{failureText(error, t)}</div>
      </td>
    </tr>
  );
}
