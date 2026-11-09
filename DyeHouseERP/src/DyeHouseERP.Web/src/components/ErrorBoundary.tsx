import { Component, type ErrorInfo, type ReactNode } from "react";
import { AlertTriangle, RotateCcw } from "lucide-react";
import { useI18n } from "@/i18n";
import { Button } from "@/components/ui";

/**
 * A render-time failure must never leave a blank page.
 *
 * Why this exists: a thrown error during render (a malformed API payload read as `data.rows[0]`,
 * an undefined lookup, a chart fed bad numbers) unmounts React's whole tree, which on this shell
 * means a white screen with the sidebar gone and no way back except the browser's reload button.
 * An error boundary keeps that failure local: the page area says what happened, the rest of the
 * app stays alive, and there is a way to retry.
 *
 * Two layers, matching where a crash should be contained:
 *   * App.tsx wraps the routes, so /login, /print/*, /scan/* and the catch-all are covered too;
 *   * Layout.tsx wraps the <Outlet />, so a crashing page leaves the sidebar, header and the
 *     current navigation intact - the user can simply click another module.
 *
 * A route change resets the boundary (`resetKey`), because navigating away is itself a retry.
 */
type Props = {
  children: ReactNode;
  /** Changing this clears a captured error: the caller passes the current route key. */
  resetKey?: string;
};

type State = { error: Error | null };

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // The only place a render crash is reported. React strips the component stack from the thrown
    // error, so it is logged here (console, not the API: a client crash never reaches the server).
    console.error("Unhandled render error:", error, info.componentStack);
  }

  componentDidUpdate(prevProps: Props) {
    if (this.state.error && prevProps.resetKey !== this.props.resetKey) this.reset();
  }

  reset = () => this.setState({ error: null });

  render() {
    if (this.state.error) return <PageCrash error={this.state.error} onRetry={this.reset} />;
    return this.props.children;
  }
}

/**
 * What the user sees instead of a white screen: the fact that rendering failed, the reassurance
 * that nothing was lost, a retry, and the technical message folded away for whoever reports it.
 */
function PageCrash({ error, onRetry }: { error: Error; onRetry: () => void }) {
  const { t } = useI18n();
  const message = error?.message || String(error || "");

  return (
    <div role="alert" aria-live="assertive" className="grid min-h-[50vh] place-items-center p-4 sm:p-6">
      <div className="card card-pad w-full max-w-2xl bg-danger-soft">
        <div className="flex items-start gap-3">
          <AlertTriangle size={22} className="mt-0.5 shrink-0 text-danger-ink" aria-hidden="true" />
          <div className="min-w-0 flex-1">
            <h1 className="text-base font-bold text-danger-ink">
              {t("error.boundary.title", "تعذّر عرض هذه الصفحة")}
            </h1>
            <p className="mt-1 text-sm text-danger-ink">
              {t(
                "error.boundary.body",
                "حدث خطأ أثناء عرض محتوى الصفحة، ولم تُفقد أي بيانات محفوظة. بقية التطبيق تعمل: أعد المحاولة، أو انتقل إلى صفحة أخرى، أو حدّث الصفحة."
              )}
            </p>

            {message && (
              <details className="mt-3">
                <summary className="cursor-pointer text-2xs font-medium text-ink-muted">
                  {t("error.boundary.details", "تفاصيل تقنية")}
                </summary>
                <pre className="mt-2 max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-lg border border-line bg-surface p-2 text-2xs text-ink-muted">
                  {message}
                </pre>
              </details>
            )}

            <div className="mt-4 flex flex-wrap items-center gap-2">
              <Button onClick={onRetry}>
                <RotateCcw size={14} className="me-1" />
                {t("error.boundary.retry", "إعادة المحاولة")}
              </Button>
              <Button variant="secondary" onClick={() => window.location.reload()}>
                {t("error.boundary.reload", "تحديث الصفحة")}
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
