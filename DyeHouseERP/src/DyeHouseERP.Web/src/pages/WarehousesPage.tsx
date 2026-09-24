import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";

const kinds = [
  { value: "RawMaterial", label: "مخزن خام" },
  { value: "Materials", label: "مخزن مواد/كيماويات" },
  { value: "ReadyGoods", label: "مخزن جاهز" }
];

export default function WarehousesPage() {
  const [showForm, setShowForm] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [kind, setKind] = useState("RawMaterial");
  const [error, setError] = useState<string | null>(null);

  const qc = useQueryClient();
  const { data: warehouses, isLoading } = useQuery({
    queryKey: ["warehouses"],
    queryFn: () => WarehousesApi.list()
  });

  const createMutation = useMutation({
    mutationFn: WarehousesApi.create,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["warehouses"] });
      setShowForm(false);
      setCode("");
      setName("");
      setError(null);
    },
    onError: (err: any) => setError(err?.response?.data?.title ?? "حدث خطأ أثناء الحفظ")
  });

  const kindLabel = (k: string) => kinds.find((x) => x.value === k)?.label ?? k;

  return (
    <>
      <PageHeader
        title="المخازن"
        subtitle="عدد ونوع المخازن قابل للتخصيص بالكامل - لا يوجد عدد ثابت مفروض على النظام"
        action={<Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مخزن جديد"}</Button>}
      />

      {showForm && (
        <Card className="p-5 mb-6">
          <form
            className="grid grid-cols-1 sm:grid-cols-4 gap-4 items-end"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate({ code, name, kind });
            }}
          >
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">كود المخزن</label>
              <Input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">اسم المخزن</label>
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">نوع المخزن</label>
              <Select value={kind} onChange={(e) => setKind(e.target.value)}>
                {kinds.map((k) => (
                  <option key={k.value} value={k.value}>{k.label}</option>
                ))}
              </Select>
            </div>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? "جارٍ الحفظ..." : "حفظ"}
            </Button>
          </form>
          {error && <p className="text-sm text-red-600 mt-3">{error}</p>}
        </Card>
      )}

      <Card>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 text-gray-500 text-xs">
              <th className="text-start px-4 py-3 font-medium">الكود</th>
              <th className="text-start px-4 py-3 font-medium">الاسم</th>
              <th className="text-start px-4 py-3 font-medium">النوع</th>
              <th className="text-start px-4 py-3 font-medium">الحالة</th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>
            )}
            {!isLoading && warehouses?.length === 0 && (
              <tr><td colSpan={4} className="px-4 py-6 text-center text-gray-400">لا توجد مخازن بعد</td></tr>
            )}
            {warehouses?.map((w) => (
              <tr key={w.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{w.code}</td>
                <td className="px-4 py-3">{w.name}</td>
                <td className="px-4 py-3"><Badge tone="blue">{kindLabel(w.kind)}</Badge></td>
                <td className="px-4 py-3">
                  <Badge tone={w.isActive ? "green" : "gray"}>{w.isActive ? "نشط" : "غير نشط"}</Badge>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
