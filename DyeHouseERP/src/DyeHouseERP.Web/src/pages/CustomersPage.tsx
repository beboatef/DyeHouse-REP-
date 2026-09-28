import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CustomersApi } from "@/api/client";
import { CustomersExports } from "@/api/exports";
import { PageHeader, Card, Button, Input, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import ImportPanel from "@/components/ImportPanel";

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
            <ExportButtons
              excel={{ action: CustomersExports.excel }}
              pdf={{ action: CustomersExports.pdf }}
            />
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

      <ImportPanel
        base="/customers/import"
        columns={["Code", "Name", "AccountNumber", "IsActive"]}
        title="استيراد العملاء من Excel"
        onDone={() => qc.invalidateQueries({ queryKey: ["customers"] })}
      />

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
