import { Link, Navigate, useLocation, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { ArrowLeft, Factory } from "lucide-react";
import { DeliveriesApi, InvoicesApi, ProductionOrdersApi, RawMessagesApi } from "@/api/client";
import { Badge } from "@/components/ui";
import RequestFailure from "@/components/RequestFailure";
import { useI18n } from "@/i18n";
import { loginHref } from "@/utils/returnUrl";

/**
 * The QR code printed on a job card opens /scan/:type/:id. A scan is a
 * floor action, so this page is a one-screen summary - order number, current
 * stage, next action - with the next action one tap away (two taps total
 * from the camera: scan, then the button).
 */
export default function ScanViewPage() {
  const { type, id } = useParams();
  const location = useLocation();

  // The interceptor would bounce an unauthenticated API call to a bare
  // /login and lose the scanned URL. Redirect first, with the scan URL as
  // the validated same-origin return target, so no request fires unsigned.
  const signedIn = typeof localStorage !== "undefined" && Boolean(localStorage.getItem("dyehouse_user"));
  if (!signedIn) return <Navigate to={loginHref(location.pathname)} replace />;

  if (type === "production-order") return <ProductionOrderScan id={id!} />;
  if (type === "invoice") return <InvoiceScan id={id!} />;
  if (type === "delivery") return <DeliveryScan id={id!} />;
  if (type === "raw-message") return <RawMessageScan id={id!} />;
  return <UnknownScan />;
}

function ScanShell({ title, number, action, children }: {
  title: string;
  number: string;
  /** The next action: rendered first, full-width, so it is one tap away. */
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="mx-auto w-full max-w-lg p-4">
      <div className="card card-pad">
        <div className="flex items-center gap-2 text-ink-subtle">
          <Factory size={16} />
          <span className="text-xs">{title}</span>
        </div>
        <div className="mt-1 text-3xl font-bold tracking-tight text-ink" dir="ltr">{number}</div>
        {action && <div className="mt-4">{action}</div>}
        <div className="mt-4 space-y-2 border-t border-line pt-4">{children}</div>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 text-sm">
      <span className="shrink-0 text-ink-subtle">{label}</span>
      <span className="min-w-0 text-end font-medium text-ink">{value}</span>
    </div>
  );
}

/**
 * Big full-width link styled as a primary button: the 44px minimum comes
 * from .btn-floor (index.css). A Link, not a Button, because Button renders
 * a <button> and cannot carry navigation.
 */
function NextActionButton({ to, children }: { to: string; children: ReactNode }) {
  const { lang } = useI18n();
  return (
    <Link to={to} className="btn btn-primary btn-floor w-full text-base">
      <span>{children}</span>
      <ArrowLeft size={18} className={lang === "ar" ? "" : "rotate-180"} />
    </Link>
  );
}

function ProductionOrderScan({ id }: { id: string }) {
  const { t } = useI18n();
  const { data: order, isLoading, error, refetch } = useQuery({
    queryKey: ["scan-order", id],
    queryFn: () => ProductionOrdersApi.get(id)
  });
  if (isLoading) return <div className="p-8 text-center text-ink-subtle">{t("scan.loading", "جارٍ التحميل...")}</div>;
  if (error || !order)
    return (
      <div className="mx-auto w-full max-w-lg p-4">
        <RequestFailure error={error} onRetry={() => void refetch()} />
      </div>
    );

  const completedKg = order.stageExecutions?.filter((s) => s.status === "Completed").length ?? 0;
  const currentStage = order.stageExecutions?.find((s) => s.status === "InProgress");
  // The next action mirrors the floor: a paused order resumes, an order with
  // no open stage starts, otherwise the open stage records output - all on
  // the order page, so the scan stays a summary and never a second editor.
  const nextAction =
    order.status === "Paused"
      ? t("scan.nextAction.resume", "استئناف الإنتاج")
      : !currentStage
        ? t("scan.nextAction.start", "بدء التشغيل")
        : t("scan.nextAction.record", "تسجيل إنتاج المرحلة");

  return (
    <ScanShell
      title={t("scan.jobOrder", "أمر تشغيل")}
      number={order.orderNumber}
      action={
        <NextActionButton to={`/production-orders/${order.id}`}>{nextAction}</NextActionButton>
      }
    >
      <Row label={t("scan.currentStage", "المرحلة الحالية")} value={currentStage?.stageName ?? t("scan.awaitingStart", "في انتظار البدء")} />
      <Row label={t("scan.status", "الحالة")} value={<Badge tone="blue">{order.status}</Badge>} />
      <Row label={t("scan.customer", "العميل")} value={`${order.customerCode} - ${order.customerName}`} />
      <Row label={t("scan.color", "اللون")} value={order.color ?? "-"} />
      <Row
        label={t("scan.requestedQty", "الكمية المطلوبة")}
        value={`${order.requestedQuantityKg ?? ""} ${order.requestedQuantityKg ? t("unit.kg") : ""} ${order.requestedQuantityMeter ?? ""} ${order.requestedQuantityMeter ? t("unit.meter") : ""}`}
      />
      <Row label={t("scan.completedStages", "مراحل مكتملة")} value={completedKg} />
    </ScanShell>
  );
}

function InvoiceScan({ id }: { id: string }) {
  const { t } = useI18n();
  const { data: invoice, isLoading, error, refetch } = useQuery({ queryKey: ["scan-invoice", id], queryFn: () => InvoicesApi.get(id) });
  if (isLoading) return <div className="p-8 text-center text-ink-subtle">{t("scan.loading", "جارٍ التحميل...")}</div>;
  if (error || !invoice)
    return (
      <div className="mx-auto w-full max-w-lg p-4">
        <RequestFailure error={error} onRetry={() => void refetch()} />
      </div>
    );

  return (
    <ScanShell title={t("scan.invoice", "فاتورة")} number={invoice.invoiceNumber}>
      <Row label={t("scan.customer", "العميل")} value={`${invoice.customerCode} - ${invoice.customerName}`} />
      <Row label={t("scan.date", "التاريخ")} value={new Date(invoice.invoiceDate).toLocaleDateString("en-GB")} />
      <Row label={t("scan.total", "الإجمالي")} value={invoice.total} />
      <Row label={t("scan.status", "الحالة")} value={<Badge tone="blue">{invoice.status}</Badge>} />
    </ScanShell>
  );
}

function DeliveryScan({ id }: { id: string }) {
  const { t } = useI18n();
  const { data: delivery, isLoading, error, refetch } = useQuery({ queryKey: ["scan-delivery", id], queryFn: () => DeliveriesApi.get(id) });
  if (isLoading) return <div className="p-8 text-center text-ink-subtle">{t("scan.loading", "جارٍ التحميل...")}</div>;
  if (error || !delivery)
    return (
      <div className="mx-auto w-full max-w-lg p-4">
        <RequestFailure error={error} onRetry={() => void refetch()} />
      </div>
    );

  return (
    <ScanShell title={t("scan.delivery", "إذن تسليم")} number={delivery.deliveryNumber}>
      <Row label={t("scan.customer", "العميل")} value={`${delivery.customerCode} - ${delivery.customerName}`} />
      <Row label={t("scan.date", "التاريخ")} value={new Date(delivery.deliveryDate).toLocaleDateString("en-GB")} />
      <Row label={t("scan.lines", "عدد البنود")} value={delivery.lines.length} />
      <Row label={t("scan.status", "الحالة")} value={<Badge tone="blue">{delivery.status}</Badge>} />
    </ScanShell>
  );
}

function RawMessageScan({ id }: { id: string }) {
  const { t } = useI18n();
  const { data: message, isLoading, error, refetch } = useQuery({ queryKey: ["scan-raw-message", id], queryFn: () => RawMessagesApi.get(id) });
  if (isLoading) return <div className="p-8 text-center text-ink-subtle">{t("scan.loading", "جارٍ التحميل...")}</div>;
  if (error || !message)
    return (
      <div className="mx-auto w-full max-w-lg p-4">
        <RequestFailure error={error} onRetry={() => void refetch()} />
      </div>
    );

  return (
    <ScanShell title={t("scan.rawMessage", "إذن استلام خام")} number={message.messageNumber}>
      <Row label={t("scan.customer", "العميل")} value={`${message.customerCode} - ${message.customerName}`} />
      <Row label={t("scan.warehouse", "المخزن")} value={message.warehouseName} />
      <Row label={t("scan.date", "التاريخ")} value={new Date(message.receiptDate).toLocaleDateString("en-GB")} />
      <Row label={t("scan.inspection", "حالة الفحص")} value={<Badge tone="blue">{message.inspectionStatus}</Badge>} />
    </ScanShell>
  );
}

function UnknownScan() {
  const { t } = useI18n();
  return <div className="p-8 text-center text-ink-subtle">{t("scan.unknown", "نوع المستند غير معروف")}</div>;
}
