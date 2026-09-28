import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CustomersApi,
  FormationGroupInput,
  FormationRequestsApi,
  FormationRequestStatus,
  FormationSpecificationsApi,
  ItemsApi,
  RawMessagesApi,
  UnitOfMeasure
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { FormationRequestsExports } from "@/api/exports";
import { useI18n } from "@/i18n";

/**
 * Formation Requests - طلب تشكيل (spec sections 28-33).
 *
 * One request, many groups/cells: e.g. 6 tubs x 500 KG in one specification and
 * 8 x 250 KG in another, out of the same customer raw material message. Each
 * group picks a reusable specification cell (spec section 29) which fills its
 * fields; every value stays editable per group.
 */

type GroupDraft = {
  key: string;
  id?: string;
  name: string;
  plannedQuantity: string;
  unit: UnitOfMeasure;
  tubCount: string;
  color: string;
  specificationTemplateId: string;
  widthCm: string;
  metersPerKg: string;
  gsm: string;
  tubFormat: string;
  windingTapeFormat: string;
  qualityInstructions: string;
  labInstructions: string;
  internalInstructions: string;
  customerInstructions: string;
  notes: string;
};

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

const emptyGroup = (unit: UnitOfMeasure): GroupDraft => ({
  key: Math.random().toString(36).slice(2),
  name: "",
  plannedQuantity: "",
  unit,
  tubCount: "",
  color: "",
  specificationTemplateId: "",
  widthCm: "",
  metersPerKg: "",
  gsm: "",
  tubFormat: "",
  windingTapeFormat: "",
  qualityInstructions: "",
  labInstructions: "",
  internalInstructions: "",
  customerInstructions: "",
  notes: ""
});

export default function FormationRequestsPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const editId = params.get("edit");

  const [showForm, setShowForm] = useState(Boolean(editId));
  const [statusFilter, setStatusFilter] = useState<FormationRequestStatus | "">("");
  const [customerFilter, setCustomerFilter] = useState("");
  const [error, setError] = useState<string | null>(null);

  const [header, setHeader] = useState({
    customerId: "",
    itemId: "",
    rawMessageId: "",
    requestDate: new Date().toISOString().slice(0, 10),
    notes: ""
  });
  const [groups, setGroups] = useState<GroupDraft[]>([emptyGroup("KG")]);

  const { data: customers } = useQuery({ queryKey: ["customers"], queryFn: () => CustomersApi.list() });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: templates } = useQuery({
    queryKey: ["formation-specifications", "active"],
    queryFn: () => FormationSpecificationsApi.list({ activeOnly: true })
  });

  const { data: customerMessages } = useQuery({
    queryKey: ["raw-messages", header.customerId],
    queryFn: () => RawMessagesApi.list({ customerId: header.customerId }),
    enabled: Boolean(header.customerId)
  });

  const { data: requests, isLoading } = useQuery({
    queryKey: ["formation-requests", statusFilter, customerFilter],
    queryFn: () =>
      FormationRequestsApi.list({
        status: statusFilter || undefined,
        customerId: customerFilter || undefined
      })
  });

  // ?edit=<id> loads an existing (draft) request back into the form for a real PUT update.
  const { data: editing } = useQuery({
    queryKey: ["formation-request", editId],
    queryFn: () => FormationRequestsApi.get(editId as string),
    enabled: Boolean(editId)
  });

  useEffect(() => {
    if (!editing) return;
    setHeader({
      customerId: editing.customerId,
      itemId: editing.itemId,
      rawMessageId: editing.rawMessageId ?? "",
      requestDate: editing.requestDate.slice(0, 10),
      notes: editing.notes ?? ""
    });
    setGroups(
      editing.groups.map((g) => ({
        key: g.id,
        id: g.id,
        name: g.name ?? "",
        plannedQuantity: String(g.plannedQuantity),
        unit: g.unit,
        tubCount: g.tubCount?.toString() ?? "",
        color: g.color ?? "",
        specificationTemplateId: g.specificationTemplateId ?? "",
        widthCm: g.widthCm?.toString() ?? "",
        metersPerKg: g.metersPerKg?.toString() ?? "",
        gsm: g.gsm?.toString() ?? "",
        tubFormat: g.tubFormat ?? "",
        windingTapeFormat: g.windingTapeFormat ?? "",
        qualityInstructions: g.qualityInstructions ?? "",
        labInstructions: g.labInstructions ?? "",
        internalInstructions: g.internalInstructions ?? "",
        customerInstructions: g.customerInstructions ?? "",
        notes: g.notes ?? ""
      }))
    );
    setShowForm(true);
  }, [editing]);

  const selectedItem = items?.find((i) => i.id === header.itemId);
  const unit: UnitOfMeasure = selectedItem?.baseUnit ?? groups[0]?.unit ?? "KG";
  const totalQuantity = groups.reduce((sum, g) => sum + (Number(g.plannedQuantity) || 0), 0);

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payloadGroups: FormationGroupInput[] = groups.map((g) => ({
        id: g.id ?? null,
        name: g.name || null,
        plannedQuantity: Number(g.plannedQuantity),
        unit,
        tubCount: g.tubCount ? Number(g.tubCount) : null,
        color: g.color || null,
        specificationTemplateId: g.specificationTemplateId || null,
        specification: {
          widthCm: g.widthCm ? Number(g.widthCm) : null,
          metersPerKg: g.metersPerKg ? Number(g.metersPerKg) : null,
          gsm: g.gsm ? Number(g.gsm) : null,
          tubFormat: g.tubFormat || null,
          windingTapeFormat: g.windingTapeFormat || null,
          qualityInstructions: g.qualityInstructions || null,
          labInstructions: g.labInstructions || null,
          internalInstructions: g.internalInstructions || null,
          customerInstructions: g.customerInstructions || null
        },
        notes: g.notes || null
      }));

      const body = {
        customerId: header.customerId,
        itemId: header.itemId,
        requestDate: header.requestDate,
        unit,
        groups: payloadGroups,
        rawMessageId: header.rawMessageId || null,
        notes: header.notes || null
      };

      return editId ? FormationRequestsApi.update(editId, body) : FormationRequestsApi.create(body);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["formation-requests"] });
      setParams({}, { replace: true });
      setShowForm(false);
      setError(null);
      setGroups([emptyGroup("KG")]);
      setHeader({ customerId: "", itemId: "", rawMessageId: "", requestDate: new Date().toISOString().slice(0, 10), notes: "" });
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const updateGroup = (key: string, patch: Partial<GroupDraft>) =>
    setGroups((prev) => prev.map((g) => (g.key === key ? { ...g, ...patch } : g)));

  /** Selecting a reusable cell copies its values onto the group - this copy is the snapshot. */
  const applyTemplate = (key: string, templateId: string) => {
    const template = templates?.find((x) => x.id === templateId);
    if (!template) {
      updateGroup(key, { specificationTemplateId: "" });
      return;
    }
    updateGroup(key, {
      specificationTemplateId: templateId,
      widthCm: template.widthCm?.toString() ?? "",
      metersPerKg: template.metersPerKg?.toString() ?? "",
      gsm: template.gsm?.toString() ?? "",
      tubFormat: template.tubFormat ?? "",
      windingTapeFormat: template.windingTapeFormat ?? "",
      qualityInstructions: template.qualityInstructions ?? "",
      labInstructions: template.labInstructions ?? "",
      internalInstructions: template.internalInstructions ?? "",
      customerInstructions: template.customerInstructions ?? "",
      notes: template.notes ?? ""
    });
  };

  const canSave = Boolean(header.customerId && header.itemId) && groups.length > 0 &&
    groups.every((g) => Number(g.plannedQuantity) > 0);

  return (
    <>
      <PageHeader
        title={t("fr.title")}
        subtitle={t("fr.subtitle")}
        action={
          <div className="flex gap-2">
            <ExportButtons
              excel={{ label: t("common.export"), action: FormationRequestsExports.excel }}
              pdf={{ label: t("common.exportPdf"), action: FormationRequestsExports.pdf }}
            />
            <Button
              onClick={() => {
                if (showForm) {
                  setShowForm(false);
                  setParams({}, { replace: true });
                } else {
                  setShowForm(true);
                }
              }}
            >
              {showForm ? t("common.cancel") : t("fr.new")}
            </Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6 space-y-5">
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.customer")}</label>
              <Select value={header.customerId} onChange={(e) => setHeader({ ...header, customerId: e.target.value, rawMessageId: "" })}>
                <option value="">—</option>
                {customers?.map((c) => (
                  <option key={c.id} value={c.id}>{c.code} - {c.name}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.item")}</label>
              <Select value={header.itemId} onChange={(e) => setHeader({ ...header, itemId: e.target.value })}>
                <option value="">—</option>
                {items?.map((i) => (
                  <option key={i.id} value={i.id}>
                    {i.code} - {pick(i.nameAr, i.nameEn, i.name)} ({i.baseUnit})
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("fr.rawMessage")}</label>
              <Select value={header.rawMessageId} onChange={(e) => setHeader({ ...header, rawMessageId: e.target.value })}>
                <option value="">{t("fr.selectMessage")}</option>
                {customerMessages?.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.messageNumber} · {m.receiptDate.slice(0, 10)}
                  </option>
                ))}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("fr.requestDate")}</label>
              <Input type="date" value={header.requestDate} onChange={(e) => setHeader({ ...header, requestDate: e.target.value })} />
            </div>
          </div>

          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold text-slate-800">{t("fr.groups")}</h3>
            <Button variant="secondary" onClick={() => setGroups((prev) => [...prev, emptyGroup(unit)])}>
              {t("fr.addGroup")}
            </Button>
          </div>

          <div className="space-y-4">
            {groups.map((group, index) => (
              <div key={group.key} className="rounded-xl border border-slate-200 p-4 bg-slate-50/60">
                <div className="flex items-center justify-between mb-3">
                  <span className="text-xs font-bold text-slate-500">
                    {t("fr.group")} #{index + 1}
                  </span>
                  {groups.length > 1 && (
                    <button
                      className="text-xs font-semibold text-red-600 hover:underline"
                      onClick={() => setGroups((prev) => prev.filter((g) => g.key !== group.key))}
                    >
                      {t("fr.removeGroup")}
                    </button>
                  )}
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.groupName")}</label>
                    <Input value={group.name} onChange={(e) => updateGroup(group.key, { name: e.target.value })} />
                  </div>
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">
                      {t("fr.plannedQuantity")} ({unit})
                    </label>
                    <Input
                      type="number"
                      step="0.001"
                      value={group.plannedQuantity}
                      onChange={(e) => updateGroup(group.key, { plannedQuantity: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.tubCount")}</label>
                    <Input type="number" value={group.tubCount} onChange={(e) => updateGroup(group.key, { tubCount: e.target.value })} />
                  </div>
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.color")}</label>
                    <Input value={group.color} onChange={(e) => updateGroup(group.key, { color: e.target.value })} />
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mt-3">
                  <div className="sm:col-span-1">
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.specTemplate")}</label>
                    <Select value={group.specificationTemplateId} onChange={(e) => applyTemplate(group.key, e.target.value)}>
                      <option value="">{t("fr.specTemplateNone")}</option>
                      {templates?.map((template) => (
                        <option key={template.id} value={template.id}>
                          {template.code} · {pick(template.nameAr, template.nameEn)}
                        </option>
                      ))}
                    </Select>
                  </div>
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.width")}</label>
                    <Input value={group.widthCm} onChange={(e) => updateGroup(group.key, { widthCm: e.target.value })} type="number" step="0.01" />
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.metersPerKg")}</label>
                      <Input value={group.metersPerKg} onChange={(e) => updateGroup(group.key, { metersPerKg: e.target.value })} type="number" step="0.0001" />
                    </div>
                    <div>
                      <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.gsm")}</label>
                      <Input value={group.gsm} onChange={(e) => updateGroup(group.key, { gsm: e.target.value })} type="number" step="0.01" />
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mt-3">
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.tubFormat")}</label>
                    <Input value={group.tubFormat} onChange={(e) => updateGroup(group.key, { tubFormat: e.target.value })} />
                  </div>
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("fr.windingTapeFormat")}</label>
                    <Input value={group.windingTapeFormat} onChange={(e) => updateGroup(group.key, { windingTapeFormat: e.target.value })} />
                  </div>
                  <div>
                    <label className="block text-[11px] font-medium text-gray-600 mb-1">{t("common.notes")}</label>
                    <Input value={group.notes} onChange={(e) => updateGroup(group.key, { notes: e.target.value })} />
                  </div>
                </div>

                <details className="mt-3">
                  <summary className="text-[11px] font-semibold text-slate-500 cursor-pointer">
                    {t("fr.qualityInstructions")} / {t("fr.labInstructions")} / {t("fr.internalInstructions")} / {t("fr.customerInstructions")}
                  </summary>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 mt-3">
                    {(
                      [
                        ["qualityInstructions", t("fr.qualityInstructions")],
                        ["labInstructions", t("fr.labInstructions")],
                        ["internalInstructions", t("fr.internalInstructions")],
                        ["customerInstructions", t("fr.customerInstructions")]
                      ] as [keyof GroupDraft, string][]
                    ).map(([key, label]) => (
                      <div key={String(key)}>
                        <label className="block text-[11px] font-medium text-gray-600 mb-1">{label}</label>
                        <textarea
                          rows={2}
                          className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none"
                          value={String(group[key] ?? "")}
                          onChange={(e) => updateGroup(group.key, { [key]: e.target.value } as Partial<GroupDraft>)}
                        />
                      </div>
                    ))}
                  </div>
                </details>
              </div>
            ))}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end">
            <div className="sm:col-span-2">
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.notes")}</label>
              <Input value={header.notes} onChange={(e) => setHeader({ ...header, notes: e.target.value })} />
            </div>
            <div className="text-sm text-slate-600">
              {t("fr.totalQuantity")}: <span className="ltr-nums font-bold">{totalQuantity}</span> {unit}
              <div className="text-[11px] text-slate-400">{t("fr.totalMustMatch")}</div>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Button onClick={() => saveMutation.mutate()} disabled={!canSave || saveMutation.isPending}>
              {saveMutation.isPending ? t("common.saving") : editId ? t("common.save") : t("common.add")}
            </Button>
            {!header.rawMessageId && <span className="text-[11px] text-amber-600">{t("fr.selectMessage")}</span>}
            {error && <span className="text-sm text-red-600">{error}</span>}
          </div>
        </Card>
      )}

      <Card className="p-4 mb-4 grid grid-cols-1 sm:grid-cols-3 gap-3">
        <Select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as FormationRequestStatus | "")}>
          <option value="">{t("common.all")} - {t("common.status")}</option>
          {(["Draft", "Submitted", "Approved", "InProgress", "PartiallyCompleted", "Completed", "Rejected", "Cancelled"] as FormationRequestStatus[]).map(
            (s) => (
              <option key={s} value={s}>{t(`fr.status.${s}`)}</option>
            )
          )}
        </Select>
        <Select value={customerFilter} onChange={(e) => setCustomerFilter(e.target.value)}>
          <option value="">{t("common.all")} - {t("common.customer")}</option>
          {customers?.map((c) => (
            <option key={c.id} value={c.id}>{c.code} - {c.name}</option>
          ))}
        </Select>
      </Card>

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("fr.number")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.date")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.customer")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.item")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.groups")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.totalQuantity")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("fr.jobOrder")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.pdf")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={9} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>
            )}
            {!isLoading && requests?.length === 0 && (
              <tr><td colSpan={9} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {requests?.map((request) => (
              <tr key={request.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium">
                  <Link to={`/formation-requests/${request.id}`} className="text-brand-600 hover:underline ltr-nums">
                    {request.requestNumber}
                  </Link>
                </td>
                <td className="px-4 py-3">
                  <Button variant="ghost" onClick={FormationRequestsExports.documentPdf(request.id)}>
                    {t("common.pdf")}
                  </Button>
                </td>
                <td className="px-4 py-3 ltr-nums text-gray-600">{request.requestDate.slice(0, 10)}</td>
                <td className="px-4 py-3">
                  <span className="ltr-nums font-medium">{request.customerCode}</span>
                  <span className="text-gray-500"> · {request.customerName}</span>
                </td>
                <td className="px-4 py-3 ltr-nums">{request.itemCode}</td>
                <td className="px-4 py-3 ltr-nums">{request.groups.length}</td>
                <td className="px-4 py-3 ltr-nums">
                  {request.totalQuantity} {request.unit}
                </td>
                <td className="px-4 py-3">
                  <Badge tone={statusTone[request.status]}>{t(`fr.status.${request.status}`)}</Badge>
                </td>
                <td className="px-4 py-3 ltr-nums text-gray-600">{request.productionOrderNumber ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
