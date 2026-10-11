import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { ApprovalsApi, type ApprovalItem, type ApprovalRequestRow } from "@/api/client";
import { Badge, Button, Card, Input, PageHeader } from "@/components/ui";
import { useI18n } from "@/i18n";
import { ArrowLeft, ArrowRight, Check, X } from "lucide-react";

/**
 * Centralized Approval Center (spec sections 24 + 44).
 *
 * Two different things live here, on purpose:
 *
 *   * the module approval queue (read-only): the real pending documents from
 *     each module with the permission that module enforces, where "Open"
 *     navigates to the screen that owns the approval, so there is exactly one
 *     implementation of every module rule;
 *   * the separation-of-duties requests (spec section 24): the negative-stock
 *     overrides raised from Customer transfers / External releases / Stock
 *     adjustments / Raw allocation. Approving one EXECUTES the module's own
 *     command, which consumes the request in the same transaction as the stock
 *     movement. The requester can never be the approver - the rule is enforced
 *     server-side from the authenticated session, not by hiding a button.
 */
export default function ApprovalCenterPage() {
  const { t, isRtl } = useI18n();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [rejecting, setRejecting] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["approval-center"],
    queryFn: () => ApprovalsApi.get(30)
  });

  const { data: requests } = useQuery({
    queryKey: ["approval-requests"],
    queryFn: () => ApprovalsApi.requests()
  });

  const approveMutation = useMutation({
    mutationFn: (id: string) => ApprovalsApi.approve(id),
    onSuccess: () => {
      setError(null);
      qc.invalidateQueries({ queryKey: ["approval-requests"] });
      qc.invalidateQueries({ queryKey: ["approval-center"] });
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? t("approvals.sod.actionFailed"))
  });

  const rejectMutation = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => ApprovalsApi.reject(id, reason),
    onSuccess: () => {
      setError(null);
      setRejecting(null);
      setRejectReason("");
      qc.invalidateQueries({ queryKey: ["approval-requests"] });
      qc.invalidateQueries({ queryKey: ["approval-center"] });
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? t("approvals.sod.actionFailed"))
  });

  const money = (v: number) =>
    v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  const pending = data?.items.filter((i) => !i.informational) ?? [];
  const recorded = data?.items.filter((i) => i.informational) ?? [];

  const openRequests = requests?.filter((r) => r.status === "Pending") ?? [];
  const decidedRequests = requests?.filter((r) => r.status !== "Pending") ?? [];

  const requestTypeLabel = (type: string) => t(`approvals.reqType.${type}`, type);
  const decisionTone = (status: string) =>
    status === "Approved" ? "green" : status === "Rejected" ? "red" : "yellow";

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

  const renderRequest = (request: ApprovalRequestRow) => (
    <tr key={request.id} className="border-b border-gray-100 last:border-0 align-top hover:bg-gray-50">
      <td>
        <div className="font-medium text-gray-800 ltr-nums">{request.documentNumber}</div>
        <div className="text-xs text-gray-400">
          {t("approvals.sod.requestedAt")}: {new Date(request.requestedAtUtc).toLocaleString("en-GB")}
        </div>
      </td>
      <td>
        <Badge tone="blue">{requestTypeLabel(request.type)}</Badge>
      </td>
      <td className="text-ink-muted">
        <div>{request.requestedBy}</div>
        {request.decidedBy && (
          <div className="text-xs text-gray-400">
            {t("approvals.sod.decidedBy")}: {request.decidedBy}
          </div>
        )}
      </td>
      <td className="text-ink-muted">
        <div>{request.reason}</div>
        {request.decisionReason && (
          <div className="text-xs text-red-600">
            {t("approvals.sod.decisionReason")}: {request.decisionReason}
          </div>
        )}
      </td>
      <td>
        {request.status === "Pending" ? (
          <div className="flex flex-wrap gap-2">
            <Button
              disabled={approveMutation.isPending}
              onClick={() => { setError(null); approveMutation.mutate(request.id); }}
            >
              <Check size={14} /> {t("approvals.sod.approve")}
            </Button>
            <Button
              variant="secondary"
              onClick={() => { setError(null); setRejecting(request.id); setRejectReason(""); }}
            >
              <X size={14} /> {t("approvals.sod.reject")}
            </Button>
            <Button variant="secondary" onClick={() => navigate(request.linkPath)}>
              {t("approvals.open")}
            </Button>
          </div>
        ) : (
          <Badge tone={decisionTone(request.status)}>
            {t(`approvals.sod.status.${request.status}`, request.status)}
          </Badge>
        )}
      </td>
    </tr>
  );

  const requestTable = (rows: ApprovalRequestRow[], includeActions: boolean) => (
    <table className="table">
      <thead>
        <tr>
          <th>{t("approvals.document")}</th>
          <th>{t("approvals.sod.operation")}</th>
          <th>{t("approvals.requestedBy")}</th>
          <th>{t("approvals.sod.reason")}</th>
          <th>{includeActions ? t("common.actions") : t("approvals.sod.decision")}</th>
        </tr>
      </thead>
      <tbody>{rows.map(renderRequest)}</tbody>
    </table>
  );

  return (
    <>
      <PageHeader title={t("approvals.title")} subtitle={t("approvals.subtitle")} />

      <Card className="mb-6">
        <div className="px-4 pt-4 text-sm font-semibold text-gray-700">{t("approvals.sod.title")}</div>
        <p className="px-4 pt-1 text-xs text-gray-500">{t("approvals.sod.subtitle")}</p>
        <p className="px-4 pt-1 text-2xs text-gray-400">{t("approvals.sod.bound")}</p>

        {error && <p className="mx-4 mt-2 rounded-lg bg-danger-soft px-3 py-2 text-sm text-danger-ink">{error}</p>}

        {openRequests.length === 0
          ? <p className="px-4 py-6 text-center text-sm text-gray-400">{t("approvals.sod.empty")}</p>
          : requestTable(openRequests, true)}

        {rejecting && (
          <div className="mx-4 mb-4 flex flex-wrap items-center gap-2 rounded-lg border border-line bg-surface-sunken p-3">
            <Input
              className="max-w-sm"
              placeholder={t("approvals.sod.rejectReason")}
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
              aria-label={t("approvals.sod.rejectReason")}
            />
            <Button
              variant="secondary"
              disabled={!rejectReason || rejectMutation.isPending}
              onClick={() => rejectMutation.mutate({ id: rejecting, reason: rejectReason })}
            >
              {rejectMutation.isPending ? t("common.saving") : t("approvals.sod.confirmReject")}
            </Button>
            <Button variant="ghost" onClick={() => { setRejecting(null); setRejectReason(""); }}>
              {t("common.cancel")}
            </Button>
          </div>
        )}

        {decidedRequests.length > 0 && (
          <>
            <div className="px-4 pt-4 text-sm font-semibold text-gray-700">{t("approvals.sod.history")}</div>
            {requestTable(decidedRequests, false)}
          </>
        )}
      </Card>

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
