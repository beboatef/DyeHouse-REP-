import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomerImportApi, CustomerImportExecuteResult, CustomerImportPreview, CustomersApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { Upload, Download } from "lucide-react";

export default function CustomersPage() {
  const [search, setSearch] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);

  const qc = useQueryClient();
  const { data: customers, isLoading } = useQuery({
    queryKey: ["customers", search],
    queryFn: () => CustomersApi.list({ search: search || undefined })
  });

  const createMutation = useMutation({
    mutationFn: CustomersApi.create,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customers"] });
      setShowForm(false);
      setCode("");
      setName("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  return (
    <>
      <PageHeader
        title="العملاء"
        subtitle="بيانات العملاء الأساسية - مرتبطة بجميع مستندات الخام والإنتاج والفواتير"
        action={
          <div className="flex items-center gap-2">
            <Button variant="secondary" onClick={() => CustomerImportApi.exportExcel({ search: search || undefined })}>
              <span className="inline-flex items-center gap-1.5"><Download size={14} /> تصدير Excel</span>
            </Button>
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ عميل جديد"}</Button>
          </div>
        }
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate({ code, name });
            }}
          >
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">كود العميل</label>
              <Input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">اسم العميل</label>
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} />
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
        </Card>
      )}

      <CustomerImportPanel onDone={() => qc.invalidateQueries({ queryKey: ["customers"] })} />

      <Card className="p-4 mb-4">
        <Input placeholder="بحث بالكود أو الاسم..." value={search} onChange={(e) => setSearch(e.target.value)} />
      </Card>

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">الكود</th>
              <th className="text-start px-4 py-3 font-medium">الاسم</th>
              <th className="text-start px-4 py-3 font-medium">الحالة</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={3} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>
            )}
            {!isLoading && customers?.length === 0 && (
              <tr><td colSpan={3} className="px-4 py-6 text-center text-gray-400">لا يوجد عملاء بعد</td></tr>
            )}
            {customers?.map((c) => (
              <tr key={c.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{c.code}</td>
                <td className="px-4 py-3">{c.name}</td>
                <td className="px-4 py-3">
                  <Badge tone={c.isActive ? "green" : "gray"}>{c.isActive ? "نشط" : "غير نشط"}</Badge>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}

/// Excel import wizard: Upload -> Preview (validate, show errors) -> Confirm -> Execute -> Results.
/// Matches the safe-import workflow required by the spec - nothing is written
/// to the database until the user explicitly confirms after reviewing errors.
function CustomerImportPanel({ onDone }: { onDone: () => void }) {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<CustomerImportPreview | null>(null);
  const [result, setResult] = useState<CustomerImportExecuteResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const previewMutation = useMutation({
    mutationFn: (f: File) => CustomerImportApi.preview(f),
    onSuccess: (data) => { setPreview(data); setResult(null); setError(null); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "تعذرت قراءة الملف - تأكد أنه بصيغة Excel (.xlsx) وبه عمودا Code و Name")
  });

  const executeMutation = useMutation({
    mutationFn: (f: File) => CustomerImportApi.execute(f),
    onSuccess: (data) => { setResult(data); onDone(); },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء التنفيذ")
  });

  const reset = () => {
    setFile(null); setPreview(null); setResult(null); setError(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  };

  if (!open) {
    return (
      <div className="flex justify-end mb-4">
        <Button variant="secondary" onClick={() => setOpen(true)}>
          <span className="inline-flex items-center gap-1.5"><Upload size={14} /> استيراد من Excel</span>
        </Button>
      </div>
    );
  }

  return (
    <Card className="p-5 mb-6">
      <div className="flex items-center justify-between mb-3">
        <div className="font-semibold text-gray-800">استيراد عملاء من ملف Excel</div>
        <button className="text-sm text-gray-400 hover:text-gray-600" onClick={() => { setOpen(false); reset(); }}>إغلاق</button>
      </div>
      <p className="text-xs text-gray-500 mb-4">الملف لازم يحتوي على عمودين بعنوان <span className="ltr-nums font-medium">Code</span> و <span className="ltr-nums font-medium">Name</span> في الصف الأول.</p>

      {!result && (
        <>
          <div className="flex items-center gap-3 mb-4">
            <input
              ref={fileInputRef}
              type="file"
              accept=".xlsx"
              onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setResult(null); setError(null); }}
              className="text-sm"
            />
            <Button
              variant="secondary"
              disabled={!file || previewMutation.isPending}
              onClick={() => file && previewMutation.mutate(file)}
            >
              {previewMutation.isPending ? "جارٍ الفحص..." : "معاينة"}
            </Button>
          </div>

          {error && <p className="text-sm text-red-600 mb-3">{error}</p>}

          {preview && (
            <>
              <div className="flex gap-4 mb-3 text-sm">
                <span className="text-green-700">صالح للاستيراد: <span className="font-bold ltr-nums">{preview.validCount}</span></span>
                <span className="text-red-700">به أخطاء: <span className="font-bold ltr-nums">{preview.invalidCount}</span></span>
              </div>
              <div className="max-h-64 overflow-y-auto border border-gray-100 rounded-lg">
                <table className="w-full text-xs">
                  <thead className="bg-gray-50 sticky top-0">
                    <tr>
                      <th className="text-start px-3 py-2">صف</th>
                      <th className="text-start px-3 py-2">الكود</th>
                      <th className="text-start px-3 py-2">الاسم</th>
                      <th className="text-start px-3 py-2">الحالة</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows.map((r) => (
                      <tr key={r.rowNumber} className={`border-t border-gray-50 ${!r.isValid ? "bg-red-50" : ""}`}>
                        <td className="px-3 py-1.5 ltr-nums">{r.rowNumber}</td>
                        <td className="px-3 py-1.5 ltr-nums">{r.values.Code ?? "-"}</td>
                        <td className="px-3 py-1.5">{r.values.Name ?? "-"}</td>
                        <td className="px-3 py-1.5">{r.isValid ? <span className="text-green-700">صالح</span> : <span className="text-red-700">{r.error}</span>}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="mt-4">
                <Button
                  disabled={preview.validCount === 0 || executeMutation.isPending}
                  onClick={() => file && executeMutation.mutate(file)}
                >
                  {executeMutation.isPending ? "جارٍ التنفيذ..." : `تأكيد استيراد ${preview.validCount} عميل`}
                </Button>
              </div>
            </>
          )}
        </>
      )}

      {result && (
        <Card className="p-4 bg-green-50 border-green-200">
          <p className="text-sm text-green-800">
            تم إنشاء <span className="font-bold ltr-nums">{result.createdCount}</span> عميل بنجاح.
            {result.skippedInvalidCount > 0 && <> تم تجاهل <span className="font-bold ltr-nums">{result.skippedInvalidCount}</span> صف به أخطاء.</>}
          </p>
          <Button variant="secondary" className="mt-3" onClick={() => { setOpen(false); reset(); }}>تم</Button>
        </Card>
      )}
    </Card>
  );
}
