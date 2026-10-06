import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { WarehousesApi } from "@/api/client";
import { PageHeader, Card, Button, Input, Select, Badge } from "@/components/ui";
import { ExportButtons } from "@/components/ExportButtons";
import ImportPanel from "@/components/ImportPanel";
import { WarehousesExports } from "@/api/exports";

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
        action={
          <div className="flex items-center gap-2">
            <ExportButtons
              excel={{ action: WarehousesExports.excel }}
              pdf={{ action: WarehousesExports.pdf }}
            />
            <Button onClick={() => setShowForm((s) => !s)}>{showForm ? "إلغاء" : "+ مخزن جديد"}</Button>
          </div>
        }
      />

      {/*
        Warehouses import is create-only by design: changing a warehouse's Kind
        would silently reinterpret every historical balance it already holds,
        so an existing code is always reported as a conflict rather than
        updated. `supportsUpdate={false}` is what shows that in the UI.
      */}
      <ImportPanel
        base="/warehouses/import"
        supportsUpdate={false}
        columns={["Code", "Name", "Kind"]}
        title="استيراد المخازن من Excel"
        onDone={() => qc.invalidateQueries({ queryKey: ["warehouses"] })}
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
              <label className="field-label">كود المخزن</label>
              <Input value={code} onChange={(e) => setCode(e.target.value)} required maxLength={30} />
            </div>
            <div>
              <label className="field-label">اسم المخزن</label>
              <Input value={name} onChange={(e) => setName(e.target.value)} required maxLength={200} />
            </div>
            <div>
              <label className="field-label">نوع المخزن</label>
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
          {error && <p className="form-error mt-3">{error}</p>}
        </Card>
      )}

      <Card>
        <table className="table">
          <thead>
            <tr>
              <th>الكود</th>
              <th>الاسم</th>
              <th>النوع</th>
              <th>الحالة</th>
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
                <td className="font-medium ltr-nums">{w.code}</td>
                <td>{w.name}</td>
                <td><Badge tone="blue">{kindLabel(w.kind)}</Badge></td>
                <td>
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
