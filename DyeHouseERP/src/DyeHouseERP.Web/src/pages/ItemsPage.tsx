import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download } from "lucide-react";
import {
  ItemImportApi,
  ItemImportPreview,
  ItemImportExecuteResult,
  ItemsApi,
  UnitOfMeasure
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge, SearchInput } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ItemsExports } from "@/api/exports";
import { readSignedInActor } from "@/permissions";
import { useI18n } from "@/i18n";

/**
 * Item master (spec sections 6-7).
 *
 * Base unit is EITHER KG or Meter, never both and never auto-converted - there
 * is deliberately no "Top/توب" unit and no "Raw Type" field. Names are bilingual
 * because every surface must work in Arabic and English.
 *
 * The Excel import follows the required workflow end to end:
 * template -> upload -> preview -> validate -> show errors -> confirm -> create
 * -> result. Existing item codes are never overwritten automatically; updating
 * them is a separate, permission-protected confirmation.
 */
export default function ItemsPage() {
  const { t, pick } = useI18n();
  const qc = useQueryClient();

  // The two rights this screen's FILE endpoints demand. Both names are the API's own constants
  // (`Permissions.ItemsView` / `Permissions.ReportsExport`, mirrored in permissions.ts) - the import
  // template needs items.view and every list export needs reports.export. A button the server would
  // refuse is simply not offered; the refusal itself is still enforced server-side on every request.
  const actor = useMemo(() => readSignedInActor(), []);
  const canDownloadTemplate = actor.can("items.view");
  const canExportList = actor.can("reports.export");

  const [search, setSearch] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({
    code: "",
    nameAr: "",
    nameEn: "",
    category: "",
    baseUnit: "KG" as UnitOfMeasure,
    // "" means "single unit" - the item uses only its base unit.
    secondaryUnit: "" as UnitOfMeasure | "",
    requireBothUnits: false
  });
  const [error, setError] = useState<string | null>(null);

  // Inline edit of ONE item. The unit configuration can only be changed while the item has
  // no inventory history at all - the backend refuses otherwise (never reinterpret a posted
  // quantity), and shows that refusal here.
  const [editing, setEditing] = useState<{
    id: string; code: string; nameAr: string; nameEn: string; category: string;
    baseUnit: UnitOfMeasure; secondaryUnit: UnitOfMeasure | "";
    requireBothUnits: boolean; isActive: boolean;
  } | null>(null);
  const [editError, setEditError] = useState<string | null>(null);

  // Explicit, item-scoped unit conversion rules. Empty is the normal case: KG and Meter are
  // never converted unless a rule is deliberately configured for a named operation.
  const [convForm, setConvForm] = useState({
    fromUnit: "Meter" as UnitOfMeasure,
    toUnit: "KG" as UnitOfMeasure,
    factor: "",
    operation: ""
  });

  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ItemImportPreview | null>(null);
  const [result, setResult] = useState<ItemImportExecuteResult | null>(null);
  const [importError, setImportError] = useState<string | null>(null);

  const { data: items, isLoading } = useQuery({
    queryKey: ["items", search],
    queryFn: () => ItemsApi.list({ search: search || undefined })
  });

  const createMutation = useMutation({
    mutationFn: () =>
      ItemsApi.create({
        code: form.code,
        name: form.nameEn || form.nameAr,
        nameAr: form.nameAr || undefined,
        nameEn: form.nameEn || undefined,
        category: form.category || undefined,
        baseUnit: form.baseUnit,
        secondaryUnit: form.secondaryUnit || null,
        requireBothUnits: form.requireBothUnits
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["items"] });
      setShowForm(false);
      setForm({ code: "", nameAr: "", nameEn: "", category: "", baseUnit: "KG", secondaryUnit: "", requireBothUnits: false });
      setError(null);
    },
    onError: (err: ApiErrorLike) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const updateMutation = useMutation({
    mutationFn: () => {
      if (!editing) throw new Error("no item selected");
      return ItemsApi.update(editing.id, {
        nameAr: editing.nameAr || undefined,
        nameEn: editing.nameEn || undefined,
        category: editing.category || undefined,
        baseUnit: editing.baseUnit,
        secondaryUnit: editing.secondaryUnit || null,
        // Explicit: a blank secondary unit must be distinguishable from "leave it as it is".
        clearSecondaryUnit: !editing.secondaryUnit,
        requireBothUnits: editing.requireBothUnits,
        isActive: editing.isActive
      });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["items"] });
      setEditing(null);
      setEditError(null);
    },
    onError: (err: ApiErrorLike) =>
      setEditError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const { data: conversions } = useQuery({
    queryKey: ["item-conversions", editing?.id],
    queryFn: () => ItemsApi.conversions(editing!.id),
    enabled: !!editing
  });

  const setConversionMutation = useMutation({
    mutationFn: () =>
      ItemsApi.setConversion(editing!.id, {
        fromUnit: convForm.fromUnit,
        toUnit: convForm.toUnit,
        factor: Number(convForm.factor),
        operation: convForm.operation
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["item-conversions"] });
      setConvForm({ ...convForm, factor: "", operation: "" });
      setEditError(null);
    },
    onError: (err: ApiErrorLike) =>
      setEditError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const removeConversionMutation = useMutation({
    mutationFn: (operation: string) => ItemsApi.removeConversion(editing!.id, operation),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["item-conversions"] })
  });

  const previewMutation = useMutation({
    mutationFn: (allowUpdate: boolean) => ItemImportApi.preview(file as File, allowUpdate),
    onSuccess: (data) => {
      setPreview(data);
      setResult(null);
      setImportError(null);
    },
    onError: (err: ApiErrorLike) =>
      setImportError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const executeMutation = useMutation({
    mutationFn: (allowUpdate: boolean) => ItemImportApi.execute(file as File, allowUpdate),
    onSuccess: (data) => {
      setResult(data);
      qc.invalidateQueries({ queryKey: ["items"] });
    },
    onError: (err: ApiErrorLike) =>
      setImportError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  return (
    <>
      <PageHeader
        title={t("items.title")}
        subtitle={t("items.subtitle")}
        action={
          <div className="flex gap-2">
            {canExportList && (
              <ExportButtons
                excel={{ label: t("common.export"), action: ItemsExports.excel }}
                pdf={{ label: t("common.exportPdf"), action: ItemsExports.pdf }}
              />
            )}
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("items.new")}</Button>
          </div>
        }
      />

      {editing && (
        <Card className="card-pad mb-6">
          <h3 className="card-title">
            {t("items.editTitle")}: <span className="ltr-nums">{editing.code}</span>
          </h3>
          <p className="text-2xs text-ink-muted mt-1">{t("items.editHint")}</p>
          <form
            className="grid grid-cols-1 sm:grid-cols-3 lg:grid-cols-6 gap-4 items-end mt-3"
            onSubmit={(e) => { e.preventDefault(); updateMutation.mutate(); }}
          >
            <div>
              <label className="field-label">{t("common.nameAr")}</label>
              <Input value={editing.nameAr} onChange={(e) => setEditing({ ...editing, nameAr: e.target.value })} />
            </div>
            <div>
              <label className="field-label">{t("common.nameEn")}</label>
              <Input value={editing.nameEn} onChange={(e) => setEditing({ ...editing, nameEn: e.target.value })} />
            </div>
            <div>
              <label className="field-label">{t("common.category")}</label>
              <Input value={editing.category} onChange={(e) => setEditing({ ...editing, category: e.target.value })} />
            </div>
            <div>
              <label className="field-label">{t("common.baseUnit")}</label>
              <Select
                value={editing.baseUnit}
                onChange={(e) => {
                  const baseUnit = e.target.value as UnitOfMeasure;
                  setEditing({
                    ...editing,
                    baseUnit,
                    secondaryUnit: editing.secondaryUnit === baseUnit ? "" : editing.secondaryUnit,
                    requireBothUnits: editing.secondaryUnit === baseUnit ? false : editing.requireBothUnits
                  });
                }}
              >
                <option value="KG">{t("common.kg")} (KG)</option>
                <option value="Meter">{t("common.meter")} (Meter)</option>
              </Select>
            </div>
            <div>
              <label className="field-label">{t("items.secondaryUnit")}</label>
              <Select
                value={editing.secondaryUnit}
                onChange={(e) => {
                  const secondaryUnit = e.target.value as UnitOfMeasure | "";
                  setEditing({
                    ...editing,
                    secondaryUnit,
                    requireBothUnits: secondaryUnit ? editing.requireBothUnits : false
                  });
                }}
              >
                <option value="">{t("items.noSecondaryUnit")}</option>
                {editing.baseUnit !== "KG" && <option value="KG">{t("common.kg")} (KG)</option>}
                {editing.baseUnit !== "Meter" && <option value="Meter">{t("common.meter")} (Meter)</option>}
              </Select>
            </div>
            <div className="flex flex-col gap-1">
              <label className="flex items-center gap-2 text-sm text-ink">
                <input
                  type="checkbox"
                  checked={editing.requireBothUnits}
                  disabled={!editing.secondaryUnit}
                  onChange={(e) => setEditing({ ...editing, requireBothUnits: e.target.checked })}
                />
                <span>{t("items.requireBothUnits")}</span>
              </label>
              <label className="flex items-center gap-2 text-sm text-ink">
                <input
                  type="checkbox"
                  checked={editing.isActive}
                  onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })}
                />
                <span>{t("common.active")}</span>
              </label>
            </div>
            <div className="sm:col-span-3 lg:col-span-6 flex flex-wrap items-center gap-3">
              <Button type="submit" disabled={updateMutation.isPending || (editing.requireBothUnits && !editing.secondaryUnit)}>
                {updateMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
              <Button type="button" variant="secondary" onClick={() => { setEditing(null); setEditError(null); }}>
                {t("common.cancel")}
              </Button>
              {editError && <span className="form-error">{editError}</span>}
            </div>
          </form>

          {/* Explicit, item-scoped conversion rules. Only available when the item uses two units;
              empty is the normal case, because KG<->Meter is never converted automatically. */}
          <div className="mt-5 border-t border-line pt-4">
            <h4 className="card-title">{t("items.conversionsTitle")}</h4>
            <p className="text-2xs text-ink-muted mt-1">{t("items.conversionsHint")}</p>

            {!editing.secondaryUnit ? (
              <p className="text-2xs text-warning-ink mt-2">{t("items.conversionsNeedSecondary")}</p>
            ) : (
              <>
                <div className="mt-2 max-w-2xl">
                  <table className="table table-dense">
                    <thead>
                      <tr>
                        <th>{t("items.operation")}</th>
                        <th>{t("items.fromUnit")}</th>
                        <th>{t("items.toUnit")}</th>
                        <th>{t("items.factor")}</th>
                        <th>{t("common.actions")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(!conversions || conversions.length === 0) && (
                        <tr><td colSpan={5} className="text-center text-ink-subtle">{t("items.noConversions")}</td></tr>
                      )}
                      {conversions?.map((rule) => (
                        <tr key={rule.id}>
                          <td>{rule.operation}</td>
                          <td className="ltr-nums">{rule.fromUnit}</td>
                          <td className="ltr-nums">{rule.toUnit}</td>
                          <td className="ltr-nums">{rule.factor}</td>
                          <td>
                            <Button
                              variant="ghost"
                              size="sm"
                              disabled={removeConversionMutation.isPending}
                              onClick={() => removeConversionMutation.mutate(rule.operation)}
                            >
                              {t("common.delete")}
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-5 gap-3 items-end mt-3">
                  <div>
                    <label className="field-label">{t("items.operation")}</label>
                    <Input value={convForm.operation} onChange={(e) => setConvForm({ ...convForm, operation: e.target.value })} />
                  </div>
                  <div>
                    <label className="field-label">{t("items.fromUnit")}</label>
                    <Select value={convForm.fromUnit} onChange={(e) => setConvForm({ ...convForm, fromUnit: e.target.value as UnitOfMeasure })}>
                      <option value="KG">KG</option>
                      <option value="Meter">Meter</option>
                    </Select>
                  </div>
                  <div>
                    <label className="field-label">{t("items.toUnit")}</label>
                    <Select value={convForm.toUnit} onChange={(e) => setConvForm({ ...convForm, toUnit: e.target.value as UnitOfMeasure })}>
                      <option value="KG">KG</option>
                      <option value="Meter">Meter</option>
                    </Select>
                  </div>
                  <div>
                    <label className="field-label">{t("items.factor")}</label>
                    <Input type="number" step="0.000001" min="0" value={convForm.factor} onChange={(e) => setConvForm({ ...convForm, factor: e.target.value })} />
                  </div>
                  <Button
                    type="button"
                    disabled={
                      setConversionMutation.isPending ||
                      !convForm.operation ||
                      Number(convForm.factor) <= 0 ||
                      convForm.fromUnit === convForm.toUnit
                    }
                    onClick={() => setConversionMutation.mutate()}
                  >
                    {t("items.addConversion")}
                  </Button>
                </div>
              </>
            )}
          </div>
        </Card>
      )}

      {showForm && (
        <Card className="card-pad mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-3 lg:grid-cols-6 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <div>
              <label className="field-label">{t("items.code")}</label>
              <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} required maxLength={30} />
            </div>
            <div>
              <label className="field-label">{t("items.nameAr")}</label>
              <Input value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} maxLength={200} />
            </div>
            <div>
              <label className="field-label">{t("items.nameEn")}</label>
              <Input value={form.nameEn} onChange={(e) => setForm({ ...form, nameEn: e.target.value })} maxLength={200} />
            </div>
            <div>
              <label className="field-label">{t("items.category")}</label>
              <Input
                value={form.category}
                placeholder={t("items.categoryPlaceholder")}
                onChange={(e) => setForm({ ...form, category: e.target.value })}
              />
            </div>
            <div>
              <label className="field-label">{t("common.baseUnit")}</label>
              <Select
                value={form.baseUnit}
                onChange={(e) => {
                  const baseUnit = e.target.value as UnitOfMeasure;
                  setForm({
                    ...form,
                    baseUnit,
                    // A secondary unit can never equal the base unit - clear it if it now would.
                    secondaryUnit: form.secondaryUnit === baseUnit ? "" : form.secondaryUnit,
                    requireBothUnits: form.secondaryUnit === baseUnit ? false : form.requireBothUnits
                  });
                }}
              >
                <option value="KG">{t("common.kg")} (KG)</option>
                <option value="Meter">{t("common.meter")} (Meter)</option>
              </Select>
            </div>
            <div>
              <label className="field-label">{t("items.secondaryUnit")}</label>
              <Select
                value={form.secondaryUnit}
                onChange={(e) => {
                  const secondaryUnit = e.target.value as UnitOfMeasure | "";
                  setForm({
                    ...form,
                    secondaryUnit,
                    requireBothUnits: secondaryUnit ? form.requireBothUnits : false
                  });
                }}
              >
                <option value="">{t("items.noSecondaryUnit")}</option>
                {form.baseUnit !== "KG" && <option value="KG">{t("common.kg")} (KG)</option>}
                {form.baseUnit !== "Meter" && <option value="Meter">{t("common.meter")} (Meter)</option>}
              </Select>
            </div>
            <div className="flex flex-col justify-end">
              <label className="check-row">
                <input
                  type="checkbox"
                  checked={form.requireBothUnits}
                  disabled={!form.secondaryUnit}
                  onChange={(e) => setForm({ ...form, requireBothUnits: e.target.checked })}
                />
                <span>{t("items.requireBothUnits")}</span>
              </label>
            </div>
            <div className="sm:col-span-3 lg:col-span-6 flex flex-wrap items-center gap-3">
              <Button
                type="submit"
                disabled={
                  createMutation.isPending ||
                  !form.code ||
                  (!form.nameAr && !form.nameEn) ||
                  (form.requireBothUnits && !form.secondaryUnit)
                }
              >
                {createMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
              <span className="text-2xs text-ink-subtle">{t("items.baseUnitHint")}</span>
              {error && <span className="form-error">{error}</span>}
            </div>
          </form>
        </Card>
      )}

      {/* ---------------- Excel import (template -> preview -> confirm -> result) ---------------- */}
      <Card className="card-pad mb-6">
        <h3 className="card-title">{t("items.importTitle")}</h3>
        <p className="text-2xs text-ink-muted mt-1">{t("items.importHint")}</p>
        <p className="text-2xs text-warning-ink mt-0.5">{t("items.importUpdateHint")}</p>

        <div className="flex flex-wrap items-center gap-3 mt-3">
          {canDownloadTemplate && (
            <Button variant="secondary" type="button" onClick={() => void ItemsExports.template()}>
              <Download size={15} /> {t("common.template")}
            </Button>
          )}
          <input
            type="file"
            accept=".xlsx,.xlsm"
            className="file-input"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setPreview(null);
              setResult(null);
            }}
          />
          <Button
            variant="secondary"
            disabled={!file || previewMutation.isPending}
            onClick={() => previewMutation.mutate(false)}
          >
            {t("common.preview")}
          </Button>
          <Button variant="ghost" disabled={!file || previewMutation.isPending} onClick={() => previewMutation.mutate(true)}>
            {t("common.preview")} + {t("items.updated")}
          </Button>
        </div>

        {importError && <p className="form-error mt-3">{importError}</p>}

        {preview && (
          <div className="mt-4">
            <div className="flex flex-wrap gap-4 text-xs text-ink-muted mb-2">
              <span>{t("common.total")}: <b className="ltr-nums">{preview.totalRows}</b></span>
              <span className="text-success-ink">{t("common.validRows")}: <b className="ltr-nums">{preview.validRows}</b></span>
              <span className="text-danger-ink">{t("common.invalidRows")}: <b className="ltr-nums">{preview.invalidRows}</b></span>
              <span>{t("common.add")}: <b className="ltr-nums">{preview.newRows}</b></span>
              <span>{t("items.updated")}: <b className="ltr-nums">{preview.existingRows}</b></span>
            </div>

            <div className="max-h-64 overflow-y-auto border border-line rounded-lg">
              <table className="table table-dense">
                <thead className="bg-surface-sunken">
                  <tr className="text-ink-muted">
                    <th>{t("common.row")}</th>
                    <th>{t("common.code")}</th>
                    <th>{t("common.nameAr")}</th>
                    <th>{t("common.nameEn")}</th>
                    <th>{t("common.category")}</th>
                    <th>{t("common.baseUnit")}</th>
                    <th>{t("items.secondaryUnit")}</th>
                    <th>{t("common.status")}</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((row) => (
                    <tr key={row.rowNumber} className="border-t border-line">
                      <td className="ltr-nums">{row.rowNumber}</td>
                      <td className="ltr-nums">{row.code}</td>
                      <td>{row.nameAr}</td>
                      <td>{row.nameEn}</td>
                      <td>{row.category ?? "—"}</td>
                      <td className="ltr-nums">{row.baseUnit}</td>
                      <td className="ltr-nums">{row.secondaryUnit || "—"}{row.requireBothUnits ? " •" : ""}</td>
                      <td>
                        {row.isValid ? (
                          <Badge tone={row.action === "Update" ? "yellow" : "green"}>{row.action}</Badge>
                        ) : (
                          <span className="text-danger-ink">{row.errors.join("; ")}</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex flex-wrap gap-3 mt-3">
              <Button
                disabled={executeMutation.isPending || preview.validRows === 0}
                onClick={() => executeMutation.mutate(false)}
              >
                {t("items.confirmImport")}
              </Button>
              {preview.existingRows > 0 && (
                <Button
                  variant="secondary"
                  disabled={executeMutation.isPending}
                  onClick={() => executeMutation.mutate(true)}
                >
                  {t("items.confirmImportWithUpdate")}
                </Button>
              )}
            </div>
          </div>
        )}

        {result && (
          <div className="mt-4 rounded-lg bg-surface-sunken border border-line p-3 text-xs">
            <div className="font-semibold text-ink mb-1">{t("common.results")}</div>
            <div className="flex flex-wrap gap-4">
              <span>{t("items.imported")}: <b className="ltr-nums">{result.created}</b></span>
              <span>{t("items.updated")}: <b className="ltr-nums">{result.updated}</b></span>
              <span>{t("items.skipped")}: <b className="ltr-nums">{result.skipped}</b></span>
            </div>
            {result.errors.length > 0 && (
              <ul className="mt-2 list-disc ps-4 text-danger-ink space-y-0.5">
                {result.errors.map((e, index) => (
                  <li key={index}>{e}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      </Card>

      <Card className="card-pad mb-4">
        <SearchInput placeholder={t("common.searchPlaceholder")} value={search} onChange={(e) => setSearch(e.target.value)} />
      </Card>

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>{t("common.code")}</th>
              <th>{t("common.nameAr")}</th>
              <th>{t("common.nameEn")}</th>
              <th>{t("common.category")}</th>
              <th>{t("common.baseUnit")}</th>
              <th>{t("items.secondaryUnit")}</th>
              <th>{t("common.status")}</th>
              <th>{t("common.actions")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={7} className="px-4 py-6 text-center text-ink-subtle">{t("common.loading")}</td></tr>
            )}
            {!isLoading && items?.length === 0 && (
              <tr><td colSpan={7} className="px-4 py-6 text-center text-ink-subtle">{t("common.empty")}</td></tr>
            )}
            {items?.map((item) => (
              <tr key={item.id} className="border-b border-line last:border-0 hover:bg-surface-sunken">
                <td className="font-medium ltr-nums">{item.code}</td>
                <td>{item.nameAr || pick(item.nameAr, item.nameEn, item.name)}</td>
                <td>{item.nameEn || "—"}</td>
                <td className="text-ink-muted">{item.category ?? "—"}</td>
                <td>
                  <Badge tone="blue">{item.baseUnit === "KG" ? t("common.kg") : t("common.meter")}</Badge>
                </td>
                <td>
                  {item.secondaryUnit ? (
                    <span className="flex items-center gap-1">
                      <Badge tone="blue">
                        {item.secondaryUnit === "KG" ? t("common.kg") : t("common.meter")}
                      </Badge>
                      {item.requireBothUnits && <Badge tone="yellow">{t("items.bothRequired")}</Badge>}
                    </span>
                  ) : (
                    <span className="text-ink-muted">—</span>
                  )}
                </td>
                <td>
                  <Badge tone={item.isActive ? "green" : "gray"}>{item.isActive ? t("common.active") : t("common.inactive")}</Badge>
                </td>
                <td>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      setEditError(null);
                      setEditing({
                        id: item.id, code: item.code,
                        nameAr: item.nameAr, nameEn: item.nameEn, category: item.category ?? "",
                        baseUnit: item.baseUnit, secondaryUnit: item.secondaryUnit ?? "",
                        requireBothUnits: item.requireBothUnits, isActive: item.isActive
                      });
                    }}
                  >
                    {t("common.edit")}
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
