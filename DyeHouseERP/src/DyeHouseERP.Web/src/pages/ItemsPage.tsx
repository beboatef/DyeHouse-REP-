import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ItemsApi, UnitOfMeasure } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

export default function ItemsPage() {
  const [search, setSearch] = useState("");
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [baseUnit, setBaseUnit] = useState<UnitOfMeasure>("KG");
  const [error, setError] = useState<string | null>(null);

  const qc = useQueryClient();
  const { data: items, isLoading } = useQuery({
    queryKey: ["items", search],
    queryFn: () => ItemsApi.list({ search: search || undefined })
  });

  const createMutation = useMutation({
    mutationFn: ItemsApi.create,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["items"] });
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
        title="الأصناف"
        subtitle="وحدة القياس الأساسية للصنف: كيلوجرام أو متر - مستقلتان تمامًا ولا يوجد تحويل تلقائي بينهما"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ صنف جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate({ code, name, baseUnit });
            }}
          >
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">كود الصنف</label>
              <Input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">اسم الصنف</label>
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">وحدة القياس الأساسية</label>
              <Select value={baseUnit} onChange={(e) => setBaseUnit(e.target.value as UnitOfMeasure)}>
                <option value="KG">كيلوجرام (KG)</option>
                <option value="Meter">متر (Meter)</option>
              </Select>
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
        </Card>
      )}

      <Card className="p-4 mb-4">
        <Input placeholder="بحث بالكود أو الاسم..." value={search} onChange={(e) => setSearch(e.target.value)} />
      </Card>

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">الكود</th>
              <th className="text-start px-4 py-3 font-medium">الاسم</th>
              <th className="text-start px-4 py-3 font-medium">الوحدة الأساسية</th>
              <th className="text-start px-4 py-3 font-medium">الحالة</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>
            )}
            {!isLoading && items?.length === 0 && (
              <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">لا توجد أصناف بعد</td></tr>
            )}
            {items?.map((i) => (
              <tr key={i.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{i.code}</td>
                <td className="px-4 py-3">{i.name}</td>
                <td className="px-4 py-3">
                  <Badge tone="blue">{i.baseUnit === "KG" ? "كيلوجرام" : "متر"}</Badge>
                </td>
                <td className="px-4 py-3">
                  <Badge tone={i.isActive ? "green" : "gray"}>{i.isActive ? "نشط" : "غير نشط"}</Badge>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
