import { useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { Button } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * The new-deployment prompt (Part 3). The service worker installs updates in
 * the background but never activates them on its own; when one is waiting,
 * this banner offers a refresh and only then tells the worker to take over.
 * Hidden entirely when the browser has no waiting worker.
 */
export default function ServiceWorkerUpdatePrompt() {
  const { t } = useI18n();
  const [waiting, setWaiting] = useState<ServiceWorker | null>(null);
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    let cancelled = false;
    void navigator.serviceWorker.getRegistration().then((registration) => {
      if (cancelled || !registration) return;
      if (registration.waiting) {
        setWaiting(registration.waiting);
        return;
      }
      registration.addEventListener("updatefound", () => {
        const next = registration.installing;
        if (!next) return;
        next.addEventListener("statechange", () => {
          if (next.state === "installed" && registration.waiting && !cancelled) {
            setWaiting(registration.waiting);
          }
        });
      });
    });
    // A deployment that lands while the app is open is found on the next
    // visit check, not by polling in a loop.
    const onVisible = () => {
      if (document.visibilityState === "visible") {
        void navigator.serviceWorker.getRegistration().then((r) => void r?.update());
      }
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      cancelled = true;
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);

  useEffect(() => {
    if (!waiting) return;
    const onChange = () => window.location.reload();
    navigator.serviceWorker.addEventListener("controllerchange", onChange);
    return () => navigator.serviceWorker.removeEventListener("controllerchange", onChange);
  }, [waiting]);

  if (!waiting || dismissed) return null;

  return (
    <div
      role="status"
      className="no-print fixed inset-x-0 bottom-0 z-[60] flex items-center justify-center gap-3 border-t border-line bg-surface px-4 py-3 shadow-pop"
    >
      <span className="text-sm font-medium text-ink">{t("pwa.updateAvailable")}</span>
      <Button
        className="btn-floor"
        onClick={() => waiting.postMessage("SKIP_WAITING")}
      >
        <RefreshCw size={16} />
        {t("pwa.refresh")}
      </Button>
      <Button variant="ghost" className="btn-floor" onClick={() => setDismissed(true)}>
        {t("pwa.dismiss")}
      </Button>
    </div>
  );
}
