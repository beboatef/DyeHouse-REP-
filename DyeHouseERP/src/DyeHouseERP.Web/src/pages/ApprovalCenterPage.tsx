import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { ApprovalsApi, type ApprovalItem } from "@/api/client";
import { Badge, Button, Card, PageHeader } from "@/components/ui";
import { useI18n } from "@/i18n";
import { ArrowLeft, ArrowRight } from "lucide-react";

/**
 * Centralized Approval Center (spec section 44).
 *
 * Read-only on purpose: the API returns the real pending documents from each
 * module with the permission that module enforces, and "Open" navigates to the
 * screen that owns the approval. That way there is exactly one implementation
 * of every approval rule, and the queue can never drift from it.
 */
export default function ApprovalCenterPage() {
  const { t, isRtl } = useI18n();
  const navigate = useNavigate();

  const { data, isLoading } = useQuery({
    queryKey: ["approval-center"],
    queryFn: () => ApprovalsApi.get(30)
  });

  const money = (v: number) =>
    v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  const pending = data?.items.filter((i) => !i.informational) ?? [];
  const recorded = data?.items.filter((i) => i.informational) ?? [];

  const renderRow = (item: ApprovalItem) => (
    <tr key={`${item.category}-${item.id}`} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
      <td>
        <div className="font-medium text-gray-800 ltr-nums">{item.documentNumber}</div>
        <div className="text-xs text-gray-400">{new Date(item.date).toLocaleDateString("en-GB")}</div>
      </td>
      <td>
        <Badge tone={item.informational ? "gray" : "yellow"}>
          {t(`approvals.cat.${item.category}`, item.category)}
        </Badge>
      </td>
      <td className="text-gray-700">{item.party ?? "—"}</td>
      <td className="text-ink-muted">
        <div>{item.summary ?? "—"}</div>
        {item.amount != null && <div className="text-xs text-gray-500 ltr-nums">{money(item.amount)}</div>}
        {item.informational && <div className="text-xs text-gray-400">{t("approvals.informational")}</div>}
      </td>
      <td className="text-ink-muted">
        <div>{item.requestedBy}</div>
        {item.requestedAtUtc && (
          <div className="text-xs text-gray-400">{new Date(item.requestedAtUtc).toLocaleString("en-GB")}</div>
        )}
      </td>
      <td>
        <Button variant="secondary" onClick={() => navigate(item.linkPath)}>
          {t("approvals.open")} {isRtl ? <ArrowLeft size={14} /> : <ArrowRight size={14} />}
        </Button>
      </td>
    </tr>
  );

  return (
    <>
      <PageHeader title={t("approvals.title")} subtitle={t("approvals.subtitle")} />

      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3 mb-6">
        {Object.entries(data?.countsByCategory ?? {}).map(([category, count]) => (
          <Card key={category} className="p-4">
            <div className="text-2xl font-bold text-gray-900 ltr-nums">{count}</div>
            <div className="text-xs text-gray-500 mt-0.5">{t(`approvals.cat.${category}`, category)}</div>
          </Card>
        ))}
        <Card className="p-4">
          <div className="text-2xl font-bold text-amber-600 ltr-nums">{data?.totalPending ?? 0}</div>
          <div className="text-xs text-gray-500 mt-0.5">{t("approvals.totalPending")}</div>
        </Card>
      </div>

      <Card className="mb-6">
        <table className="table">
          <thead>
            <tr>
              <th>{t("approvals.document")}</th>
              <th>{t("approvals.category")}</th>
              <th>{t("approvals.party")}</th>
              <th>{t("approvals.summary")}</th>
              <th>{t("approvals.requestedBy")}</th>
              <th>{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && pending.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("approvals.empty")}</td>
              </tr>
            )}
            {pending.map(renderRow)}
          </tbody>
        </table>
      </Card>

      {recorded.length > 0 && (
        <Card>
          <div className="px-4 pt-4 text-sm font-semibold text-gray-700">
            {t("approvals.cat.NegativeStockException")}
          </div>
          <p className="px-4 pt-1 text-xs text-gray-400">{t("approvals.informational")}</p>
          <table className="table">
            <thead>
              <tr>
                <th>{t("approvals.document")}</th>
                <th>{t("approvals.category")}</th>
                <th>{t("approvals.party")}</th>
                <th>{t("approvals.summary")}</th>
                <th>{t("approvals.requestedBy")}</th>
                <th>{t("common.actions")}</th>
              </tr>
            </thead>
            <tbody>{recorded.map(renderRow)}</tbody>
          </table>
        </Card>
      )}
    </>
  );
}
