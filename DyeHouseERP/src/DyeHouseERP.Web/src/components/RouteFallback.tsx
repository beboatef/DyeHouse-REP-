import { useI18n } from "@/i18n";

/**
 * Shown while a route's chunk is being fetched (route-level code splitting).
 *
 * Deliberately static: on a warm connection this is visible for the length of
 * one module request, so a spinner or shimmer would be nothing but a flash.
 * `role="status"` + `aria-live` tell a screen reader the page is loading
 * without moving focus or announcing the whole shell again.
 */
export default function RouteFallback() {
  const { t } = useI18n();

  return (
    <div
      role="status"
      aria-live="polite"
      className="grid min-h-[40vh] place-items-center px-6 py-10 text-sm text-ink-subtle"
    >
      {t("common.loading", "جارٍ التحميل...")}
    </div>
  );
}
