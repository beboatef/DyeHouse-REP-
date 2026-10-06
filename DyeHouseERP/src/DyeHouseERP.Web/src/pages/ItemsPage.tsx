import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ItemImportApi,
  ItemImportPreview,
  ItemImportExecuteResult,
  ItemsApi,
  UnitOfMeasure
} from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import { ItemsExports } from "@/api/exports";
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

  const [search, setSearch] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState({
    code: "",
    nameAr: "",
    nameEn: "",
    category: "",
    baseUnit: "KG" as UnitOfMeasure
  });
  const [error, setError] = useState<string | null>(null);

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
        baseUnit: form.baseUnit
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["items"] });
      setShowForm(false);
      setForm({ code: "", nameAr: "", nameEn: "", category: "", baseUnit: "KG" });
      setError(null);
    },
    onError: (err: any) =>
      setError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const previewMutation = useMutation({
    mutationFn: (allowUpdate: boolean) => ItemImportApi.preview(file as File, allowUpdate),
    onSuccess: (data) => {
      setPreview(data);
      setResult(null);
      setImportError(null);
    },
    onError: (err: any) =>
      setImportError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  const executeMutation = useMutation({
    mutationFn: (allowUpdate: boolean) => ItemImportApi.execute(file as File, allowUpdate),
    onSuccess: (data) => {
      setResult(data);
      qc.invalidateQueries({ queryKey: ["items"] });
    },
    onError: (err: any) =>
      setImportError(err?.response?.data?.detail ?? err?.response?.data?.title ?? t("common.error"))
  });

  return (
    <>
      <PageHeader
        title={t("items.title")}
        subtitle={t("items.subtitle")}
        action={
          <div className="flex gap-2">
            <ExportButtons
              excel={{ label: t("common.export"), action: ItemsExports.excel }}
              pdf={{ label: t("common.exportPdf"), action: ItemsExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? t("common.cancel") : t("items.new")}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-5 gap-4 items-end"
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
              <Select value={form.baseUnit} onChange={(e) => setForm({ ...form, baseUnit: e.target.value as UnitOfMeasure })}>
                <option value="KG">{t("common.kg")} (KG)</option>
                <option value="Meter">{t("common.meter")} (Meter)</option>
              </Select>
            </div>
            <div className="sm:col-span-5 flex items-center gap-3">
              <Button type="submit" disabled={createMutation.isPending || !form.code || (!form.nameAr && !form.nameEn)}>
                {createMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
              <span className="text-2xs text-slate-400">{t("items.baseUnitHint")}</span>
              {error && <span className="form-error">{error}</span>}
            </div>
          </form>
        </Card>
      )}

      {/* ---------------- Excel import (template -> preview -> confirm -> result) ---------------- */}
      <Card className="p-5 mb-6">
        <h3 className="text-sm font-bold text-slate-800">{t("items.importTitle")}</h3>
        <p className="text-2xs text-slate-500 mt-1">{t("items.importHint")}</p>
        <p className="text-2xs text-amber-600 mt-0.5">{t("items.importUpdateHint")}</p>

        <div className="flex flex-wrap items-center gap-3 mt-3">
          <input
            type="file"
            accept=".xlsx,.xlsm"
            className="block h-[38px] w-full max-w-xs cursor-pointer rounded-lg border border-line-strong
                       bg-surface text-sm text-ink file:me-2 file:cursor-pointer file:rounded-md
                       file:border-0 file:bg-surface-sunken file:px-3 file:py-2 file:text-xs
                       file:font-semibold file:text-ink hover:border-brand-500"
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
            <div className="flex flex-wrap gap-4 text-xs text-slate-600 mb-2">
              <span>{t("common.total")}: <b className="ltr-nums">{preview.totalRows}</b></span>
              <span className="text-green-700">{t("common.validRows")}: <b className="ltr-nums">{preview.validRows}</b></span>
              <span className="text-red-600">{t("common.invalidRows")}: <b className="ltr-nums">{preview.invalidRows}</b></span>
              <span>{t("common.add")}: <b className="ltr-nums">{preview.newRows}</b></span>
              <span>{t("items.updated")}: <b className="ltr-nums">{preview.existingRows}</b></span>
            </div>

            <div className="max-h-64 overflow-y-auto border border-slate-200 rounded-lg">
              <table className="table table-dense">
                <thead className="bg-slate-50">
                  <tr className="text-gray-500">
                    <th>{t("common.row")}</th>
                    <th>{t("common.code")}</th>
                    <th>{t("common.nameAr")}</th>
                    <th>{t("common.nameEn")}</th>
                    <th>{t("common.category")}</th>
                    <th>{t("common.baseUnit")}</th>
                    <th>{t("common.status")}</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((row) => (
                    <tr key={row.rowNumber} className="border-t border-slate-100">
                      <td className="ltr-nums">{row.rowNumber}</td>
                      <td className="ltr-nums">{row.code}</td>
                      <td>{row.nameAr}</td>
                      <td>{row.nameEn}</td>
                      <td>{row.category ?? "—"}</td>
                      <td className="ltr-nums">{row.baseUnit}</td>
                      <td>
                        {row.isValid ? (
                          <Badge tone={row.action === "Update" ? "yellow" : "green"}>{row.action}</Badge>
                        ) : (
                          <span className="text-red-600">{row.errors.join("; ")}</span>
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
          <div className="mt-4 rounded-lg bg-slate-50 border border-slate-200 p-3 text-xs">
            <div className="font-semibold text-slate-700 mb-1">{t("common.results")}</div>
            <div className="flex flex-wrap gap-4">
              <span>{t("items.imported")}: <b className="ltr-nums">{result.created}</b></span>
              <span>{t("items.updated")}: <b className="ltr-nums">{result.updated}</b></span>
              <span>{t("items.skipped")}: <b className="ltr-nums">{result.skipped}</b></span>
            </div>
            {result.errors.length > 0 && (
              <ul className="mt-2 list-disc ps-4 text-red-600 space-y-0.5">
                {result.errors.map((e, index) => (
                  <li key={index}>{e}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      </Card>

      <Card className="p-4 mb-4">
        <Input placeholder={t("common.searchPlaceholder")} value={search} onChange={(e) => setSearch(e.target.value)} />
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
              <th>{t("common.status")}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.loading")}</td></tr>
            )}
            {!isLoading && items?.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">{t("common.empty")}</td></tr>
            )}
            {items?.map((item) => (
              <tr key={item.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="font-medium ltr-nums">{item.code}</td>
                <td>{item.nameAr || pick(item.nameAr, item.nameEn, item.name)}</td>
                <td>{item.nameEn || "—"}</td>
                <td className="text-ink-muted">{item.category ?? "—"}</td>
                <td>
                  <Badge tone="blue">{item.baseUnit === "KG" ? t("common.kg") : t("common.meter")}</Badge>
                </td>
                <td>
                  <Badge tone={item.isActive ? "green" : "gray"}>{item.isActive ? t("common.active") : t("common.inactive")}</Badge>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
