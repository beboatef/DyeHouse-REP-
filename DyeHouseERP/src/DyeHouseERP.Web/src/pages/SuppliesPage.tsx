import { Fragment, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  MaterialsApi,
  PayrollApi,
  SuppliesApi,
  WarehousesApi,
  type MaterialUnit,
  type SupplyIssue,
  type SupplyIssueStatus
} from "@/api/client";
import { Badge, Button, Card, Input, PageHeader, Select } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { SuppliesExports } from "@/api/exports";
import { useI18n } from "@/i18n";

/**
 * Operating supplies internal issue (spec section 27).
 *
 * Spare parts, packaging and maintenance consumables are issued to a
 * department / internal user. The cost is NOT charged to any Job Order - that
 * only ever happens through an explicit material issue or cost entry.
 */
export default function SuppliesPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();

  const [statusFilter, setStatusFilter] = useState<"" | SupplyIssueStatus>("");
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);

  const [warehouseId, setWarehouseId] = useState("");
  const [issuedTo, setIssuedTo] = useState("");
  const [departmentId, setDepartmentId] = useState("");
  const [purpose, setPurpose] = useState("");
  const [issueDate, setIssueDate] = useState(new Date().toISOString().slice(0, 10));
  const [notes, setNotes] = useState("");

  // Line entry for the currently expanded draft issue.
  const [materialId, setMaterialId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");

  const { data: issues, isLoading } = useQuery({
    queryKey: ["supply-issues", statusFilter],
    queryFn: () => SuppliesApi.list({ status: statusFilter || undefined })
  });

  const { data: warehouses } = useQuery({ queryKey: ["warehouses"], queryFn: () => WarehousesApi.list() });
  const { data: departments } = useQuery({
    queryKey: ["departments", "active"],
    queryFn: () => PayrollApi.departments({ activeOnly: true })
  });

  // Only supply-classified materials can be issued here - the API enforces the
  // same rule, so the picker never offers a chemical that would be refused.
  const { data: supplyMaterials } = useQuery({
    queryKey: ["materials", "supplies"],
    queryFn: () => MaterialsApi.list({ activeOnly: true, kind: "OperatingSupply" })
  });

  const create = useMutation({
    mutationFn: () =>
      SuppliesApi.create({
        issueDate,
        warehouseId,
        issuedTo,
        purpose,
        departmentId: departmentId || undefined,
        notes: notes || undefined
      }),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["supply-issues"] });
      setShowForm(false);
      setIssuedTo("");
      setPurpose("");
      setNotes("");
      setError(null);
      setExpanded(created.id);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const addLine = useMutation({
    mutationFn: (issueId: string) =>
      SuppliesApi.addLine(issueId, {
        materialId,
        quantity: Number(quantity),
        unitCost: unitCost ? Number(unitCost) : undefined
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["supply-issues"] });
      setMaterialId("");
      setQuantity("");
      setUnitCost("");
      setError(null);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const removeLine = useMutation({
    mutationFn: (vars: { id: string; lineId: string }) => SuppliesApi.removeLine(vars.id, vars.lineId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["supply-issues"] })
  });

  const post = useMutation({
    mutationFn: (id: string) => SuppliesApi.post(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["supply-issues"] }),
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const cancel = useMutation({
    mutationFn: (id: string) => {
      const reason = window.prompt(t("common.reason"));
      return SuppliesApi.cancel(id, reason || "");
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["supply-issues"] }),
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const money = (v: number) =>
    v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  const statusTone = (s: SupplyIssueStatus) =>
    s === "Posted" ? "green" : s === "Cancelled" ? "red" : "yellow";

  const statusLabel = (s: SupplyIssueStatus) =>
    s === "Posted" ? t("sup.posted") : s === "Cancelled" ? t("sup.cancelled") : t("sup.draft");

  const unitFor = (id: string): MaterialUnit => supplyMaterials?.find((m) => m.id === id)?.unit ?? "KG";

  return (
    <>
      <PageHeader
        title={t("sup.title")}
        subtitle={t("sup.subtitle")}
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: SuppliesExports.excel }}
              pdf={{ action: SuppliesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("sup.new")}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              create.mutate();
            }}
          >
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.warehouse")}</label>
              <Select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
                <option value="">—</option>
                {warehouses?.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </Select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("sup.issuedTo")}</label>
              <Input value={issuedTo} onChange={(e) => setIssuedTo(e.target.value)} required maxLength={200} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("sup.department")}</label>
              <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
                <option value="">—</option>
                {departments?.map((d) => (
                  <option key={d.id} value={d.id}>{pick(d.nameAr, d.nameEn, d.name)}</option>
                ))}
              </Select>
            </div>
            <div className="sm:col-span-2">
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("sup.purpose")}</label>
              <Input value={purpose} onChange={(e) => setPurpose(e.target.value)} required maxLength={500} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.date")}</label>
              <Input type="date" value={issueDate} onChange={(e) => setIssueDate(e.target.value)} />
            </div>
            <div className="sm:col-span-2">
              <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.notes")}</label>
              <Input value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} />
            </div>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? t("common.saving") : t("common.save")}
            </Button>
          </form>
        </Card>
      )}

      <Card className="p-4 mb-4 flex flex-col sm:flex-row gap-3 sm:items-end">
        <div className="w-full sm:w-56">
          <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.status")}</label>
          <Select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as "" | SupplyIssueStatus)}>
            <option value="">{t("common.all")}</option>
            <option value="Draft">{t("sup.draft")}</option>
            <option value="Posted">{t("sup.posted")}</option>
            <option value="Cancelled">{t("sup.cancelled")}</option>
          </Select>
        </div>
        <p className="text-xs text-gray-400 sm:mb-2">{t("sup.onlySupplies")}</p>
      </Card>

      {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">{t("approvals.document")}</th>
              <th className="text-start px-4 py-3 font-medium">PDF</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.date")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("sup.issuedTo")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("sup.purpose")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("sup.totalCost")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.status")}</th>
              <th className="text-start px-4 py-3 font-medium">{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td>
              </tr>
            )}
            {!isLoading && issues?.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td>
              </tr>
            )}
            {issues?.map((issue: SupplyIssue) => (
              <Fragment key={issue.id}>
                <tr className="border-b border-gray-100 hover:bg-gray-50">
                  <td className="px-4 py-3 font-medium ltr-nums">{issue.issueNumber}</td>
                  <td className="px-4 py-3">
                    <Button variant="ghost" onClick={SuppliesExports.documentPdf(issue.id)}>PDF</Button>
                  </td>
                  <td className="px-4 py-3 ltr-nums">{new Date(issue.issueDate).toLocaleDateString("en-GB")}</td>
                  <td className="px-4 py-3">
                    <div>{issue.issuedTo}</div>
                    {issue.departmentName && <div className="text-xs text-gray-400">{issue.departmentName}</div>}
                  </td>
                  <td className="px-4 py-3 text-gray-600">{issue.purpose}</td>
                  <td className="px-4 py-3 ltr-nums">{money(issue.totalCost)}</td>
                  <td className="px-4 py-3">
                    <Badge tone={statusTone(issue.status)}>{statusLabel(issue.status)}</Badge>
                  </td>
                  <td className="px-4 py-3 flex flex-wrap gap-2">
                    <Button variant="ghost" onClick={() => setExpanded(expanded === issue.id ? null : issue.id)}>
                      {expanded === issue.id ? t("common.close") : t("common.details")}
                    </Button>
                    {issue.isEditable && (
                      <Button variant="secondary" onClick={() => post.mutate(issue.id)} disabled={post.isPending}>
                        {t("sup.post")}
                      </Button>
                    )}
                    {issue.status !== "Cancelled" && (
                      <Button variant="ghost" onClick={() => cancel.mutate(issue.id)}>
                        {t("sup.cancelIssue")}
                      </Button>
                    )}
                  </td>
                </tr>

                {expanded === issue.id && (
                  <tr className="bg-gray-50/60">
                    <td colSpan={8} className="px-4 py-4">
                      <table className="w-full text-sm mb-4">
                        <thead>
                          <tr className="text-gray-500 text-xs border-b border-gray-200">
                            <th className="text-start py-2 font-medium">{t("common.item")}</th>
                            <th className="text-start py-2 font-medium">{t("common.quantity")}</th>
                            <th className="text-start py-2 font-medium">{t("pur.unitPrice")}</th>
                            <th className="text-start py-2 font-medium">{t("common.total")}</th>
                            <th className="text-start py-2 font-medium">{t("common.actions")}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {issue.lines.length === 0 && (
                            <tr>
                              <td colSpan={5} className="py-3 text-center text-gray-400 text-xs">{t("common.empty")}</td>
                            </tr>
                          )}
                          {issue.lines.map((l) => (
                            <tr key={l.id} className="border-b border-gray-100 last:border-0">
                              <td className="py-2">
                                <span className="ltr-nums text-gray-500 me-2">{l.materialCode}</span>
                                {l.materialName}
                              </td>
                              <td className="py-2 ltr-nums">{l.quantity} {l.unit}</td>
                              <td className="py-2 ltr-nums">{money(l.unitCost)}</td>
                              <td className="py-2 ltr-nums">{money(l.totalCost)}</td>
                              <td className="py-2">
                                {issue.isEditable && (
                                  <button
                                    className="text-red-500 hover:underline text-xs font-semibold"
                                    onClick={() => removeLine.mutate({ id: issue.id, lineId: l.id })}
                                  >
                                    {t("common.remove")}
                                  </button>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>

                      {issue.isEditable && (
                        <form
                          className="grid grid-cols-1 sm:grid-cols-4 gap-3 items-end"
                          onSubmit={(e) => {
                            e.preventDefault();
                            addLine.mutate(issue.id);
                          }}
                        >
                          <div>
                            <label className="block text-xs font-medium text-gray-600 mb-1">{t("common.item")}</label>
                            <Select value={materialId} onChange={(e) => setMaterialId(e.target.value)} required>
                              <option value="">—</option>
                              {supplyMaterials?.map((m) => (
                                <option key={m.id} value={m.id}>
                                  {m.code} — {m.name} ({m.unit})
                                </option>
                              ))}
                            </Select>
                          </div>
                          <div>
                            <label className="block text-xs font-medium text-gray-600 mb-1">
                              {t("common.quantity")} {materialId ? `(${unitFor(materialId)})` : ""}
                            </label>
                            <Input
                              type="number"
                              step="0.001"
                              min="0"
                              value={quantity}
                              onChange={(e) => setQuantity(e.target.value)}
                              required
                            />
                          </div>
                          <div>
                            <label className="block text-xs font-medium text-gray-600 mb-1">
                              {t("pur.unitPrice")}
                            </label>
                            <Input type="number" step="0.0001" min="0" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} />
                          </div>
                          <Button type="submit" disabled={addLine.isPending}>{t("sup.addLine")}</Button>
                        </form>
                      )}

                      {issue.status !== "Draft" && issue.timeline.length > 0 && (
                        <div className="mt-4">
                          <div className="text-xs font-semibold text-gray-600 mb-2">{t("common.timeline")}</div>
                          <ul className="text-xs text-gray-500 space-y-1">
                            {issue.timeline.map((entry, i) => (
                              <li key={i}>
                                <span className="font-medium text-gray-700">{entry.action}</span>
                                {entry.user && <> · {entry.user}</>}
                                {entry.atUtc && <> · {new Date(entry.atUtc).toLocaleString("en-GB")}</>}
                              </li>
                            ))}
                          </ul>
                        </div>
                      )}
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
