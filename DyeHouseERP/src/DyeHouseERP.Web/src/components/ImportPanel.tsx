import { useRef, useState } from "react";
import { Upload, Download, FileSpreadsheet, Loader2, CircleCheck } from "lucide-react";
import { api } from "@/api/client";
import { Button, Card } from "./ui";

/**
 * One Excel-import wizard for every master-data list (spec section 38).
 *
 * The workflow is deliberately Upload -> Preview -> Confirm -> Result, and it
 * is the *same component* for Customers, Items, Materials, Suppliers,
 * Warehouses and Employees - only the four endpoints change. That is what
 * makes the guarantees real rather than per-screen promises:
 *
 *   - Preview never writes. It returns one row per spreadsheet line with the
 *     exact field values, whether that row would be created or updated, and
 *     every reason it is invalid.
 *   - Execute re-uploads the same file and the server re-validates it from
 *     scratch, so a stale preview can never push a bad row into the database.
 *   - Invalid rows are reported and skipped, never written.
 *   - Overwriting an existing record is a *separate* pair of endpoints guarded
 *     by the module's edit permission, and only when the caller ticks the box -
 *     so there is no silent overwrite.
 */
export interface ImportRowPreview {
  rowNumber: number;
  isValid: boolean;
  isExisting: boolean;
  /** "Create" | "Update" | "Skip" - exactly what Execute will do. */
  action: string;
  errors: string[];
  values: Record<string, string | null>;
}

export interface ImportPreview {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  newRows: number;
  existingRows: number;
  updateExistingAllowed: boolean;
  rows: ImportRowPreview[];
}

export interface ImportExecuteResult {
  created: number;
  updated: number;
  skipped: number;
  errors: string[];
  rows: ImportRowPreview[];
}

export interface ImportEndpoints {
  /** The path prefix, e.g. "/customers/import" - the panel appends /template, /preview, ... */
  base: string;
  /** Set false for lists where a spreadsheet must never change an existing record. */
  supportsUpdate?: boolean;
  /** Human labels for the columns, used as the table header. */
  columns: string[];
  title?: string;
  onDone?: () => void;
}

const upload = (url: string, file: File) => {
  const form = new FormData();
  form.append("file", file);
  return api
    .post(url, form, { headers: { "Content-Type": "multipart/form-data" } })
    .then((response) => response.data as any);
};

export default function ImportPanel({
  base, supportsUpdate = true, columns, title = "استيراد من Excel", onDone
}: ImportEndpoints) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [result, setResult] = useState<ImportExecuteResult | null>(null);
  const [withUpdate, setWithUpdate] = useState(false);
  const [busy, setBusy] = useState<null | "preview" | "execute" | "template">(null);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const suffix = withUpdate && supportsUpdate ? "-update" : "";

  const run = async (kind: "preview" | "execute" | "template") => {
    setError(null);
    if (kind === "template") {
      const response = await api.get(`${base}/template`, { responseType: "blob" });
      const url = window.URL.createObjectURL(new Blob([response.data as Blob]));
      const link = document.createElement("a");
      link.href = url;
      link.download = "import-template.xlsx";
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
      return;
    }

    if (!file) {
      setError("اختر ملف Excel أولاً.");
      return;
    }

    setBusy(kind);
    try {
      const data = kind === "preview"
        ? await upload(`${base}/preview${suffix}`, file)
        : await upload(`${base}/execute${suffix}`, file);
      if (kind === "preview") setPreview(data as ImportPreview);
      else {
        setResult(data as ImportExecuteResult);
        onDone?.();
      }
    } catch (e: any) {
      setError(e?.response?.data?.message || e?.response?.data?.title || "فشل الاستيراد.");
    } finally {
      setBusy(null);
    }
  };

  const reset = () => {
    setFile(null);
    setPreview(null);
    setResult(null);
    setError(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  return (
    <Card className="mt-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2 font-semibold">
          <FileSpreadsheet size={18} className="text-brand-600" />
          {title}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" type="button" onClick={() => run("template")}>
            <Download size={15} /> تحميل قالب الاستيراد
          </Button>
          <Button variant="secondary" type="button" onClick={reset}>إلغاء / ملف جديد</Button>
        </div>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-3">
        <input
          ref={inputRef}
          type="file"
          accept=".xlsx,.xlsm"
          onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setResult(null); }}
          className="block h-[38px] w-full max-w-xs cursor-pointer rounded-lg border border-line-strong
                     bg-surface text-sm text-ink file:me-2 file:cursor-pointer file:rounded-md
                     file:border-0 file:bg-surface-sunken file:px-3 file:py-2 file:text-xs
                     file:font-semibold file:text-ink hover:border-brand-500"
        />
        {supportsUpdate && (
          <label className="flex items-center gap-2 text-sm text-gray-600">
            <input
              type="checkbox"
              checked={withUpdate}
              onChange={(e) => { setWithUpdate(e.target.checked); setPreview(null); }}
            />
            السماح بتحديث السجلات الموجودة (يتطلب صلاحية التعديل)
          </label>
        )}
        <Button type="button" disabled={!file || busy !== null} onClick={() => run("preview")}>
          {busy === "preview" ? <Loader2 size={15} className="animate-spin" /> : <Upload size={15} />} معاينة قبل الاستيراد
        </Button>
      </div>

      {error && <div className="motion-rise mt-3 text-sm text-red-600">{error}</div>}

      {preview && !result && (
        <div className="motion-rise mt-4">
          <div className="flex flex-wrap gap-4 text-sm">
            <span>إجمالي الصفوف: <b className="ltr-nums">{preview.totalRows}</b></span>
            <span className="text-green-700">صالحة: <b className="ltr-nums">{preview.validRows}</b></span>
            <span className="text-red-600">أخطاء: <b className="ltr-nums">{preview.invalidRows}</b></span>
            <span>جديدة: <b className="ltr-nums">{preview.newRows}</b></span>
            <span>موجودة: <b className="ltr-nums">{preview.existingRows}</b></span>
          </div>

          {preview.invalidRows > 0 && (
            <div className="mt-2 text-sm text-red-600">
              الصفوف غير الصالحة لن تُكتب نهائياً. صحّحها في الملف ثم أعد المعاينة.
            </div>
          )}

          <div className="mt-3 max-h-80 overflow-auto rounded-lg border border-line">
            <table className="table table-dense">
              <thead>
                <tr>
                  <th>#</th>
                  <th>الإجراء</th>
                  {columns.map((c) => <th key={c}>{c}</th>)}
                  <th>الأخطاء</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row) => (
                  <tr key={row.rowNumber} className={row.isValid ? "" : "bg-danger-soft/60"}>
                    <td className="ltr-nums">{row.rowNumber}</td>
                    <td>
                      {row.isValid
                        ? (row.action === "Update" ? "تحديث" : "إضافة")
                        : "تخطي"}
                    </td>
                    {columns.map((c) => (
                      <td key={c}>{row.values[c] ?? ""}</td>
                    ))}
                    <td className="text-red-600">{row.errors.join(" • ")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="mt-3 flex items-center gap-2">
            <Button type="button" disabled={preview.validRows === 0 || busy !== null} onClick={() => run("execute")}>
              {busy === "execute" ? <Loader2 size={15} className="animate-spin" /> : <CircleCheck size={15} />}
              تأكيد الاستيراد ({preview.validRows} صف)
            </Button>
            {preview.validRows === 0 && <span className="text-sm text-gray-500">لا توجد صفوف صالحة للتنفيذ.</span>}
          </div>
        </div>
      )}

      {result && (
        <div className="motion-rise mt-4">
          <div className="flex items-center gap-2 font-semibold text-green-700">
            <CircleCheck size={18} /> تم الاستيراد
          </div>
          <div className="mt-2 flex flex-wrap gap-4 text-sm">
            <span>تمت الإضافة: <b className="ltr-nums">{result.created}</b></span>
            <span>تم التحديث: <b className="ltr-nums">{result.updated}</b></span>
            <span>تم التخطي: <b className="ltr-nums">{result.skipped}</b></span>
          </div>
          {result.errors.length > 0 && (
            <ul className="mt-2 list-inside list-disc text-sm text-red-600">
              {result.errors.map((e, i) => <li key={i}>{e}</li>)}
            </ul>
          )}
        </div>
      )}
    </Card>
  );
}
