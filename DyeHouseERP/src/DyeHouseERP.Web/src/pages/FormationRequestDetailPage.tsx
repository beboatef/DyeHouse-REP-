import { Fragment, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FormationRequestsApi, FormationRequestStatus, JobOrderType, ProductionPriority } from "@/api/client";
import AttachmentsPanel from "@/components/AttachmentsPanel";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

/**
 * Formation Request detail (spec sections 30-33).
 *
 * Shows the frozen specification snapshot of every group/cell, the controlled
 * status workflow (submit / approve / reject / cancel), conversion into a Job
 * Order per group or for the whole request, and the full traceability chain
 * Customer -> Raw Material Message -> Formation Request -> Job Order ->
 * Production -> Ready Goods -> Delivery.
 */

const statusTone: Record<FormationRequestStatus, "gray" | "yellow" | "blue" | "green" | "red"> = {
  Draft: "gray",
  Submitted: "yellow",
  Approved: "blue",
  InProgress: "blue",
  PartiallyCompleted: "yellow",
  Completed: "green",
  Rejected: "red",
  Cancelled: "gray"
};

export default function FormationRequestDetailPage() {
  const { t } = useI18n();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const [reason, setReason] = useState("");
  const [convertGroupId, setConvertGroupId] = useState("");
  const [convertBasinId, setConvertBasinId] = useState("");
  const [jobOrderType, setJobOrderType] = useState<JobOrderType>("ClosedLine");
  const [priority, setPriority] = useState<ProductionPriority>("Normal");
  const [error, setError] = useState<string | null>(null);

  const { data: request, isLoading } = useQuery({
    queryKey: ["formation-request", id],
    queryFn: () => FormationRequestsApi.get(id as string),
    enabled: Boolean(id)
  });

  const { data: traceability } = useQuery({
    queryKey: ["formation-request-traceability", id],
    queryFn: () => FormationRequestsApi.traceability(id as string),
    enabled: Boolean(id)
  });

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["formation-request", id] });
    qc.invalidateQueries({ queryKey: ["formation-request-traceability", id] });
    qc.invalidateQueries({ queryKey: ["formation-requests"] });
    qc.invalidateQueries({ queryKey: ["production-orders"] });
  };

  const onError = (err: any) => setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"));

  const submitMutation = useMutation({ mutationFn: () => FormationRequestsApi.submit(id as string), onSuccess: refresh, onError });
  const approveMutation = useMutation({ mutationFn: () => FormationRequestsApi.approve(id as string), onSuccess: refresh, onError });
  const rejectMutation = useMutation({
    mutationFn: () => FormationRequestsApi.reject(id as string, reason),
    onSuccess: () => { setReason(""); refresh(); },
    onError
  });
  const cancelMutation = useMutation({
    mutationFn: () => FormationRequestsApi.cancel(id as string, reason),
    onSuccess: () => { setReason(""); refresh(); },
    onError
  });
  const convertMutation = useMutation({
    mutationFn: () =>
      FormationRequestsApi.convertToJobOrder(id as string, {
        groupId: convertGroupId || null,
        basinId: convertBasinId || null,
        jobOrderType,
        priority,
        orderDate: new Date().toISOString().slice(0, 10)
      }),
    onSuccess: () => { setConvertGroupId(""); setConvertBasinId(""); refresh(); },
    onError
  });

  if (isLoading || !request) {
    return <Card className="p-6 text-center text-gray-400">{t("common.loading")}</Card>;
  }

  const canEdit = request.status === "Draft" || request.status === "Rejected";
  const canConvert = request.status === "Approved" || request.status === "InProgress" || request.status === "PartiallyCompleted";
  // Basins and groups can be converted one by one (spec sections 10-11). A group that is split into basins
  // is never offered as a lump - that would plan the same quantity twice - and neither is a whole request
  // that contains such a group. The API enforces exactly the same rule server-side.
  const hasBasins = request.groups.some((g) => (g.basins?.length ?? 0) > 0);
  const convertibleGroups = request.groups.filter((g) => (g.basins?.length ?? 0) === 0);
  const convertibleBasins = request.groups.flatMap((g) =>
    (g.basins ?? []).map((b) => ({ group: g.groupNumber, basin: b }))
  );

  return (
    <>
      <PageHeader
        title={`${request.requestNumber}`}
        subtitle={`${request.customerCode} - ${request.customerName} · ${request.itemCode} · ${request.totalQuantity} ${request.unit}`}
        action={
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => FormationRequestsApi.downloadPdf(request.id, request.requestNumber)}>
              {t("common.pdf")}
            </Button>
            {canEdit && (
              <Button variant="secondary" onClick={() => navigate(`/formation-requests?edit=${request.id}`)}>
                {t("common.edit")}
              </Button>
            )}
            <Link to="/formation-requests">
              <Button variant="ghost">{t("common.close")}</Button>
            </Link>
          </div>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 mb-6">
        <Card className="p-5 lg:col-span-2">
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
            <Info label={t("common.status")}>
              <Badge tone={statusTone[request.status]}>{t(`fr.status.${request.status}`)}</Badge>
            </Info>
            <Info label={t("fr.requestDate")}>
              <span className="ltr-nums">{request.requestDate.slice(0, 10)}</span>
            </Info>
            <Info label={t("fr.rawMessage")}>
              {request.rawMessageId ? (
                <Link className="btn-link ltr-nums" to={`/print/raw-message/${request.rawMessageId}`}>
                  {request.messageNumber}
                </Link>
              ) : (
                <span className="text-amber-600">—</span>
              )}
            </Info>
            <Info label={t("fr.jobOrder")}>
              {request.productionOrderId ? (
                <Link className="btn-link ltr-nums" to={`/production-orders/${request.productionOrderId}`}>
                  {request.productionOrderNumber}
                </Link>
              ) : (
                <span className="text-gray-400">—</span>
              )}
            </Info>
            <Info label={t("fr.submittedBy")}>
              <span>{request.submittedBy ?? "—"}</span>
            </Info>
            <Info label={t("fr.approvedBy")}>
              <span>{request.approvedBy ?? "—"}</span>
            </Info>
            <Info label={t("fr.produced")}>
              <span className="ltr-nums">{request.producedQuantity} {request.unit}</span>
            </Info>
            <Info label={t("fr.remaining")}>
              <span className="ltr-nums">{request.totalQuantity - request.producedQuantity} {request.unit}</span>
            </Info>
          </div>
          {request.notes && <p className="text-sm text-gray-600 mt-4">{request.notes}</p>}
          {request.rejectionReason && (
            <p className="text-sm text-red-600 mt-4">{t("fr.rejectReason")}: {request.rejectionReason}</p>
          )}
          {request.cancellationReason && (
            <p className="text-sm text-red-600 mt-4">{t("fr.cancelReason")}: {request.cancellationReason}</p>
          )}
        </Card>

        <Card className="p-5">
          <h3 className="text-sm font-bold text-slate-800 mb-3">{t("common.actions")}</h3>
          <div className="space-y-3">
            {request.status === "Draft" && (
              <Button className="w-full" onClick={() => submitMutation.mutate()} disabled={submitMutation.isPending}>
                {t("fr.submit")}
              </Button>
            )}
            {request.status === "Submitted" && (
              <>
                <Button className="w-full" onClick={() => approveMutation.mutate()} disabled={approveMutation.isPending}>
                  {t("fr.approve")}
                </Button>
                <Button variant="secondary" className="w-full" onClick={() => rejectMutation.mutate()} disabled={!reason}>
                  {t("fr.reject")}
                </Button>
              </>
            )}
            {canConvert && (
              <div className="space-y-2 border-t border-slate-100 pt-3">
                <p className="text-2xs text-slate-500">{t("fr.convertToJobOrder")}</p>
                <Select value={convertBasinId} onChange={(e) => { setConvertBasinId(e.target.value); setConvertGroupId(""); }}>
                  <option value="">{t("fr.convertBasinNone")}</option>
                  {convertibleBasins.map(({ group, basin }) => (
                    <option key={basin.id} value={basin.id}>
                      {t("fr.group")} #{group} / {t("fr.basin")} #{basin.basinNumber} · {basin.plannedQuantity} {basin.unit}
                    </option>
                  ))}
                </Select>
                <Select value={convertGroupId} onChange={(e) => { setConvertGroupId(e.target.value); setConvertBasinId(""); }}>
                  <option value="">{t("fr.convertWhole")}</option>
                  {convertibleGroups.map((g) => (
                    <option key={g.id} value={g.id}>
                      {t("fr.group")} #{g.groupNumber} · {g.plannedQuantity} {g.unit}
                    </option>
                  ))}
                </Select>
                {hasBasins && !convertBasinId && (
                  <p className="text-2xs text-amber-600">{t("fr.convertBasinRequired")}</p>
                )}
                <Select value={jobOrderType} onChange={(e) => setJobOrderType(e.target.value as JobOrderType)}>
                  <option value="ClosedLine">{t("jo.type.ClosedLine")}</option>
                  <option value="OpenLine">{t("jo.type.OpenLine")}</option>
                </Select>
                <Select value={priority} onChange={(e) => setPriority(e.target.value as ProductionPriority)}>
                  <option value="Low">Low</option>
                  <option value="Normal">Normal</option>
                  <option value="High">High</option>
                  <option value="Urgent">Urgent</option>
                </Select>
                <Button
                  className="w-full"
                  onClick={() => convertMutation.mutate()}
                  disabled={convertMutation.isPending || (hasBasins && !convertBasinId && !convertGroupId)}
                >
                  {t("fr.convertToJobOrder")}
                </Button>
              </div>
            )}
            {(request.status === "Submitted" || request.status === "Approved" || request.status === "Rejected") && (
              <Button variant="secondary" className="w-full" onClick={() => cancelMutation.mutate()} disabled={!reason}>
                {t("fr.cancelRequest")}
              </Button>
            )}
            {(request.status === "Submitted" || request.status === "Approved" || request.status === "Rejected") && (
              <Input
                placeholder={`${t("fr.rejectReason")} / ${t("fr.cancelReason")}`}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            )}
            {error && <p className="form-error">{error}</p>}
          </div>
        </Card>
      </div>

      <Card className="mb-6">
        <div className="px-5 py-4 border-b border-slate-100 flex items-center justify-between">
          <h3 className="text-sm font-bold text-slate-800">{t("fr.groups")} / {t("fr.specification")}</h3>
          {request.groups.some((g) => g.specificationSnapshotAtUtc) && (
            <span className="text-2xs text-slate-400">{t("fr.snapshotFrozen")}</span>
          )}
        </div>
        <div className="overflow-x-auto">
          <table className="table">
            <thead>
              <tr>
                <th>#</th>
                <th>{t("fr.basin")}</th>
                <th>{t("fr.planned")}</th>
                <th>{t("fr.produced")}</th>
                <th>{t("fr.tubCount")}</th>
                <th>{t("fr.color")}</th>
                <th>{t("fr.width")}</th>
                <th>{t("fr.metersPerKg")}</th>
                <th>{t("fr.gsm")}</th>
                <th>{t("fr.tubFormat")}</th>
                <th>{t("fr.specTemplate")}</th>
              </tr>
            </thead>
            <tbody>
              {request.groups.map((g) => (
                <Fragment key={g.id}>
                  <tr className="border-b border-gray-100 last:border-0">
                    <td className="font-medium">{g.groupNumber}</td>
                    <td className="ltr-nums text-gray-400">
                      {(g.basins?.length ?? 0) === 0 ? "—" : t("fr.groupTotal")}
                    </td>
                    <td className="ltr-nums">{g.plannedQuantity} {g.unit}</td>
                    <td className="ltr-nums">{g.producedQuantity}</td>
                    <td className="ltr-nums">{g.tubCount ?? "—"}</td>
                    <td>{g.color ?? "—"}</td>
                    <td className="ltr-nums">{g.widthCm ?? "—"}</td>
                    <td className="ltr-nums">{g.metersPerKg ?? "—"}</td>
                    <td className="ltr-nums">{g.gsm ?? "—"}</td>
                    <td>{g.tubFormat ?? "—"}</td>
                    <td className="text-ink-subtle">{g.specificationTemplateName ?? "—"}</td>
                  </tr>
                  {/* One row per basin (spec sections 10-11) - its own quantity and its own specification. */}
                  {(g.basins ?? []).map((b) => (
                    <tr key={b.id} className="border-b border-gray-100 bg-slate-50/60 last:border-0">
                      <td className="font-medium text-brand-700">{g.groupNumber}</td>
                      <td className="font-medium text-brand-700">
                        {t("fr.basin")} #{b.basinNumber}
                      </td>
                      <td className="ltr-nums">
                        {b.plannedQuantity} {b.unit}
                        {b.producedQuantity > 0 && (
                          <span className="text-gray-400"> / {b.producedQuantity}</span>
                        )}
                      </td>
                      <td className="ltr-nums">{b.producedQuantity}</td>
                      <td className="ltr-nums">{b.tubCount ?? "—"}</td>
                      <td>{b.color ?? "—"}</td>
                      <td className="ltr-nums">{b.widthCm ?? g.widthCm ?? "—"}</td>
                      <td className="ltr-nums">{b.metersPerKg ?? g.metersPerKg ?? "—"}</td>
                      <td className="ltr-nums">{b.gsm ?? g.gsm ?? "—"}</td>
                      <td>{b.tubFormat ?? g.tubFormat ?? "—"}</td>
                      <td className="text-gray-500">
                        {b.specificationTemplateName ?? g.specificationTemplateName ?? "—"}
                      </td>
                    </tr>
                  ))}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <Card className="p-5">
        <h3 className="text-sm font-bold text-slate-800 mb-4">{t("common.traceability")}</h3>
        <ol className="space-y-2">
          {traceability?.links.map((link, index) => (
            <li key={`${link.stage}-${index}`} className="flex items-start gap-3">
              <span className="mt-1.5 w-2 h-2 rounded-full bg-brand-500 shrink-0" />
              <div className="text-sm">
                <span className="font-semibold text-slate-700">
                  {link.stage === "FormationRequest" ? t("fr.title") : link.stage}
                </span>
                {" · "}
                {link.route ? (
                  <Link to={link.route} className="btn-link ltr-nums">
                    {link.reference}
                  </Link>
                ) : (
                  <span className="ltr-nums">{link.reference}</span>
                )}
                {link.detail && <span className="text-gray-500"> · {link.detail}</span>}
                {link.dateUtc && <span className="text-gray-400"> · {String(link.dateUtc).slice(0, 10)}</span>}
              </div>
            </li>
          ))}
        </ol>
        {(!traceability || traceability.links.length === 0) && (
          <p className="text-sm text-gray-400">{t("common.empty")}</p>
        )}
      </Card>

      {/* Private attachments for this formation request (spec section 47). */}
      {id && (
        <div className="mt-6">
          <AttachmentsPanel entityType="FormationRequest" entityId={id} />
        </div>
      )}
    </>
  );
}

function Info({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="text-2xs text-slate-400 mb-0.5">{label}</div>
      <div className="font-medium text-slate-700">{children}</div>
    </div>
  );
}
