import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi, DocumentPdfApi, ItemsApi, RawMessagesApi, WarehousesApi } from "@/api/client";
import type { RawMessage } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { useI18n } from "@/i18n";

type LineDraft = { itemId: string; quantityKg: string; quantityMeter: string; pieceCount: string; notes: string };
type RejectionDraft = { kg: string; meter: string };

const emptyLine = (): LineDraft => ({ itemId: "", quantityKg: "", quantityMeter: "", pieceCount: "", notes: "" });

const inspectionTone: Record<string, "yellow" | "green" | "red"> = {
  PendingInspection: "yellow",
  Accepted: "green",
  AcceptedWithNotes: "green",
  Rejected: "red"
};
const inspectionKey: Record<string, string> = {
  PendingInspection: "raw.inspection.pending",
  Accepted: "raw.inspection.accepted",
  AcceptedWithNotes: "raw.inspection.acceptedWithNotes",
  Rejected: "raw.inspection.rejected"
};

const num = (v: string) => (v ? Number(v) : undefined);

export default function RawMessagesPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [customerId, setCustomerId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [receiptDate, setReceiptDate] = useState(new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<LineDraft[]>([emptyLine()]);
  const [error, setError] = useState<string | null>(null);

  // Receiving inspection is recorded information, never a blocking approval step
  // (spec section 9) - so it lives in its own inline panel on each receipt.
  const [inspectionFor, setInspectionFor] = useState<string | null>(null);
  const [inspectionResult, setInspectionResult] = useState<"AcceptedWithNotes" | "Rejected">("AcceptedWithNotes");
  const [inspectionNotes, setInspectionNotes] = useState("");
  const [rejections, setRejections] = useState<Record<string, RejectionDraft>>({});
  const [inspectionError, setInspectionError] = useState<string | null>(null);

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: items } = useQuery({ queryKey: ["items", "active"], queryFn: () => ItemsApi.list({ activeOnly: true }) });
  const { data: warehouses } = useQuery({ queryKey: ["warehouses", "raw"], queryFn: () => WarehousesApi.list({ kind: "RawMaterial" }) });
  const { data: messages, isLoading } = useQuery({ queryKey: ["raw-messages"], queryFn: () => RawMessagesApi.list() });

  const createMutation = useMutation({
    mutationFn: RawMessagesApi.create,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      setShowForm(false);
      setLines([emptyLine()]);
      setNotes("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? t("common.error"))
  });

  const inspectionMutation = useMutation({
    mutationFn: (input: {
      id: string;
      result: string;
      notes?: string;
      rejections?: { lineId: string; rejectedQuantityKg?: number; rejectedQuantityMeter?: number }[];
    }) => RawMessagesApi.recordInspection(input.id, { result: input.result, notes: input.notes, rejections: input.rejections }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["raw-messages"] });
      setInspectionFor(null);
      setInspectionError(null);
    },
    onError: (err: any) => setInspectionError(err?.response?.data?.title ?? t("common.error"))
  });

  const openInspection = (message: RawMessage) => {
    if (inspectionFor === message.id) {
      setInspectionFor(null);
      return;
    }
    setInspectionFor(message.id);
    setInspectionResult("AcceptedWithNotes");
    setInspectionNotes("");
    setInspectionError(null);
    setRejections(
      Object.fromEntries(message.lines.map((l) => [l.id, { kg: "", meter: "" } satisfies RejectionDraft]))
    );
  };

  const submitInspection = (message: RawMessage) => {
    const payload = message.lines
      .map((l) => {
        const draft = rejections[l.id] ?? { kg: "", meter: "" };
        return { lineId: l.id, rejectedQuantityKg: num(draft.kg), rejectedQuantityMeter: num(draft.meter) };
      })
      .filter((r) => (r.rejectedQuantityKg ?? 0) > 0 || (r.rejectedQuantityMeter ?? 0) > 0);

    if (payload.length === 0) {
      setInspectionError(t("raw.inspection.noReject"));
      return;
    }

    inspectionMutation.mutate({
      id: message.id,
      result: inspectionResult,
      notes: inspectionNotes || undefined,
      rejections: payload
    });
  };

  const updateLine = (idx: number, patch: Partial<LineDraft>) =>
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));

  const setRejection = (lineId: string, patch: Partial<RejectionDraft>) =>
    setRejections((prev) => ({ ...prev, [lineId]: { ...(prev[lineId] ?? { kg: "", meter: "" }), ...patch } }));

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      receiptDate,
      customerId,
      warehouseId,
      notes: notes || undefined,
      lines: lines
        .filter((l) => l.itemId && (l.quantityKg || l.quantityMeter))
        .map((l) => ({
          itemId: l.itemId,
          quantityKg: l.quantityKg ? Number(l.quantityKg) : undefined,
          quantityMeter: l.quantityMeter ? Number(l.quantityMeter) : undefined,
          pieceCount: l.pieceCount ? Number(l.pieceCount) : undefined,
          notes: l.notes || undefined
        }))
    });
  };

  return (
    <>
      <PageHeader
        title={t("raw.title")}
        subtitle={t("raw.subtitle")}
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("raw.new")}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form onSubmit={submit} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.customer")}</label>
                <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)} required>
                  <option value="">{t("common.selectCustomer")}</option>
                  {customers?.map((c) => (
                    <option key={c.id} value={c.id}>{c.code} - {c.name}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.warehouse")}</label>
                <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                  <option value="">{t("common.selectWarehouse")}</option>
                  {warehouses?.map((w) => (
                    <option key={w.id} value={w.id}>{w.code} - {w.name}</option>
                  ))}
                </Select>
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-600 mb-1">{t("raw.receiptDate")}</label>
                <Input type="date" value={receiptDate} onChange={(e) => setReceiptDate(e.target.value)} required />
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="block text-xs font-medium text-gray-600">{t("raw.lines")}</label>
                <Button type="button" variant="ghost" onClick={() => setLines((p) => [...p, emptyLine()])}>{t("raw.addLine")}</Button>
              </div>
              <div className="space-y-3">
                {lines.map((line, idx) => (
                  <div key={idx} className="grid grid-cols-1 sm:grid-cols-6 gap-3 items-end bg-gray-50 rounded-lg p-3">
                    <div className="sm:col-span-2">
                      <label className="block text-[11px] text-gray-500 mb-1">{t("common.item")}</label>
                      <Select value={line.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} required>
                        <option value="">{t("common.select")}</option>
                        {items?.map((i) => (
                          <option key={i.id} value={i.id}>
                            {i.code} - {pick(i.nameAr, i.nameEn, i.name)} ({i.baseUnit})
                          </option>
                        ))}
                      </Select>
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("raw.qtyKg")}</label>
                      <Input type="number" step="0.001" min="0" value={line.quantityKg}
                        onChange={(e) => updateLine(idx, { quantityKg: e.target.value })} />
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("raw.qtyMeter")}</label>
                      <Input type="number" step="0.001" min="0" value={line.quantityMeter}
                        onChange={(e) => updateLine(idx, { quantityMeter: e.target.value })} />
                    </div>
                    <div>
                      <label className="block text-[11px] text-gray-500 mb-1">{t("raw.piecesHint")}</label>
                      <Input type="number" min="0" value={line.pieceCount}
                        onChange={(e) => updateLine(idx, { pieceCount: e.target.value })} />
                    </div>
                    <div className="flex gap-2">
                      <Input placeholder={t("common.notes")} value={line.notes} onChange={(e) => updateLine(idx, { notes: e.target.value })} />
                      {lines.length > 1 && (
                        <Button type="button" variant="secondary" onClick={() => setLines((p) => p.filter((_, i) => i !== idx))}>
                          {t("common.remove")}
                        </Button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("raw.generalNotes")}</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </div>

            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("raw.saveMessage")}
            </Button>
            {error && <p className="text-sm text-red-600">{error}</p>}
          </form>
        </Card>
      )}

      <div className="space-y-4">
        {isLoading && <Card className="p-6 text-center text-gray-400">{t("common.loading")}</Card>}
        {!isLoading && messages?.length === 0 && <Card className="p-6 text-center text-gray-400">{t("raw.empty")}</Card>}

        {messages?.map((m) => (
          <Card key={m.id} className="p-5">
            <div className="flex flex-wrap items-center justify-between gap-3 mb-3">
              <div>
                <span className="font-bold text-lg ltr-nums">{m.messageNumber}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{m.customerCode} - {m.customerName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-600">{m.warehouseName}</span>
                <span className="text-gray-400 mx-2">·</span>
                <span className="text-sm text-gray-500 ltr-nums">{new Date(m.receiptDate).toLocaleDateString("en-GB")}</span>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Link to={`/print/raw-message/${m.id}`} target="_blank">
                  <Button type="button" variant="ghost">{t("raw.previewPrint")}</Button>
                </Link>
                <Button variant="ghost" onClick={() => DocumentPdfApi.rawMessage(m.id, m.messageNumber)}>{t("raw.downloadPdf")}</Button>
                <Badge tone={inspectionTone[m.inspectionStatus]}>{t(inspectionKey[m.inspectionStatus], m.inspectionStatus)}</Badge>
                <Button variant="secondary" onClick={() => inspectionMutation.mutate({ id: m.id, result: "Accepted" })}>
                  {t("raw.inspection.accept")}
                </Button>
                <Button variant="secondary" onClick={() => openInspection(m)}>
                  {t("raw.inspection.partial")}
                </Button>
              </div>
            </div>

            <table className="w-full text-sm">
              <thead>
                <tr className="text-gray-400 text-xs border-b border-gray-100">
                  <th className="text-start py-2 font-medium">{t("common.item")}</th>
                  <th className="text-start py-2 font-medium">{t("raw.receivedQty")}</th>
                  <th className="text-start py-2 font-medium">{t("raw.rejectedQty")}</th>
                  <th className="text-start py-2 font-medium">{t("raw.acceptedQty")}</th>
                  <th className="text-start py-2 font-medium">{t("raw.remainingQty")}</th>
                  <th className="text-start py-2 font-medium">{t("raw.pieces")}</th>
                </tr>
              </thead>
              <tbody>
                {m.lines.map((l) => (
                  <tr key={l.id} className="border-b border-gray-50 last:border-0">
                    <td className="py-2">{l.itemCode} - {l.itemName}</td>
                    <td className="py-2 ltr-nums">
                      {l.quantityKg != null && `${l.quantityKg} ${t("common.kg")}`}
                      {l.quantityKg != null && l.quantityMeter != null && " / "}
                      {l.quantityMeter != null && `${l.quantityMeter} ${t("common.meter")}`}
                    </td>
                    <td className="py-2 ltr-nums text-red-600">
                      {l.rejectedQuantityKg == null && l.rejectedQuantityMeter == null
                        ? "-"
                        : [
                            l.rejectedQuantityKg != null ? `${l.rejectedQuantityKg} ${t("common.kg")}` : null,
                            l.rejectedQuantityMeter != null ? `${l.rejectedQuantityMeter} ${t("common.meter")}` : null
                          ].filter(Boolean).join(" / ")}
                    </td>
                    <td className="py-2 ltr-nums">
                      {l.acceptedQuantityKg != null && `${l.acceptedQuantityKg} ${t("common.kg")}`}
                      {l.acceptedQuantityKg != null && l.acceptedQuantityMeter != null && " / "}
                      {l.acceptedQuantityMeter != null && `${l.acceptedQuantityMeter} ${t("common.meter")}`}
                    </td>
                    <td className="py-2 ltr-nums font-medium">
                      {l.remainingKg != null && `${l.remainingKg} ${t("common.kg")}`}
                      {l.remainingKg != null && l.remainingMeter != null && " / "}
                      {l.remainingMeter != null && `${l.remainingMeter} ${t("common.meter")}`}
                    </td>
                    <td className="py-2 ltr-nums">{l.pieceCount ?? "-"}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            {inspectionFor === m.id && (
              <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50/60 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2 mb-1">
                  <span className="text-sm font-semibold text-amber-900">{t("raw.inspection.title")}</span>
                  <span className="text-xs text-amber-800">{t("raw.inspection.hint")}</span>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 mt-3">
                  <div>
                    <label className="block text-[11px] text-gray-600 mb-1">{t("raw.inspection.result")}</label>
                    <Select value={inspectionResult} onChange={(e) => setInspectionResult(e.target.value as "AcceptedWithNotes" | "Rejected")}>
                      <option value="AcceptedWithNotes">{t("raw.inspection.acceptedWithNotes")}</option>
                      <option value="Rejected">{t("raw.inspection.rejected")}</option>
                    </Select>
                  </div>
                  <div>
                    <label className="block text-[11px] text-gray-600 mb-1">{t("raw.inspection.notes")}</label>
                    <Input value={inspectionNotes} onChange={(e) => setInspectionNotes(e.target.value)} />
                  </div>
                </div>

                <div className="mt-3 space-y-2">
                  {m.lines.map((l) => (
                    <div key={l.id} className="grid grid-cols-1 sm:grid-cols-3 gap-3 items-end bg-white/70 rounded-lg p-3">
                      <div className="text-sm text-gray-700">
                        {l.itemCode} - {l.itemName}
                        <div className="text-[11px] text-gray-400 ltr-nums">
                          {t("raw.receivedQty")}: {l.quantityKg ?? "-"} {t("common.kg")} / {l.quantityMeter ?? "-"} {t("common.meter")}
                        </div>
                      </div>
                      <div>
                        <label className="block text-[11px] text-gray-500 mb-1">{t("raw.rejectedQty")} ({t("common.kg")})</label>
                        <Input type="number" step="0.001" min="0" value={rejections[l.id]?.kg ?? ""}
                          onChange={(e) => setRejection(l.id, { kg: e.target.value })} />
                      </div>
                      <div>
                        <label className="block text-[11px] text-gray-500 mb-1">{t("raw.rejectedQty")} ({t("common.meter")})</label>
                        <Input type="number" step="0.001" min="0" value={rejections[l.id]?.meter ?? ""}
                          onChange={(e) => setRejection(l.id, { meter: e.target.value })} />
                      </div>
                    </div>
                  ))}
                </div>

                <div className="flex items-center gap-2 mt-3">
                  <Button variant="secondary" disabled={inspectionMutation.isPending} onClick={() => submitInspection(m)}>
                    {inspectionMutation.isPending ? t("common.saving") : t("raw.inspection.save")}
                  </Button>
                  <Button variant="ghost" onClick={() => setInspectionFor(null)}>{t("common.cancel")}</Button>
                  {inspectionError && <span className="text-sm text-red-600">{inspectionError}</span>}
                </div>
              </div>
            )}
          </Card>
        ))}
      </div>
    </>
  );
}
