import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { CustomersApi, ProductionFloorApi } from "@/api/client";
import { PageHeader, Card, Select, Badge } from "@/components/ui";

const priorityLabel: Record<string, string> = { Low: "منخفضة", Normal: "عادية", High: "عالية", Urgent: "عاجلة" };
const priorityTone: Record<string, "gray" | "blue" | "yellow" | "red"> = { Low: "gray", Normal: "blue", High: "yellow", Urgent: "red" };

export default function ProductionFloorPage() {
  const [customerId, setCustomerId] = useState("");
  const [priority, setPriority] = useState("");

  const { data: customers } = useQuery({ queryKey: ["customers", "active"], queryFn: () => CustomersApi.list({ activeOnly: true }) });
  const { data: rows, isLoading } = useQuery({
    queryKey: ["production-floor", customerId, priority],
    queryFn: () => ProductionFloorApi.get({ customerId: customerId || undefined, priority: priority || undefined }),
    refetchInterval: 30000
  });

  return (
    <>
      <PageHeader title="شاشة أرضية المصنع" subtitle="نظرة حية على كل أمر تشغيل جارٍ ومرحلته الحالية - تُحدَّث تلقائيًا" />

      <Card className="p-4 mb-4 flex flex-wrap gap-3">
        <div className="w-56">
          <Select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
            <option value="">كل العملاء</option>{customers?.map((c) => <option key={c.id} value={c.id}>{c.code} - {c.name}</option>)}
          </Select>
        </div>
        <div className="w-40">
          <Select value={priority} onChange={(e) => setPriority(e.target.value)}>
            <option value="">كل الأولويات</option><option value="Low">منخفضة</option><option value="Normal">عادية</option><option value="High">عالية</option><option value="Urgent">عاجلة</option>
          </Select>
        </div>
      </Card>

      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">أمر التشغيل</th><th className="text-start px-4 py-3 font-medium">العميل</th>
            <th className="text-start px-4 py-3 font-medium">الصنف</th><th className="text-start px-4 py-3 font-medium">المرحلة الحالية</th>
            <th className="text-start px-4 py-3 font-medium">الوقت في المرحلة</th><th className="text-start px-4 py-3 font-medium">الأولوية</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && rows?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد أوامر تشغيل جارية حاليًا</td></tr>}
            {rows?.map((r) => (
              <tr key={r.productionOrderId} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 font-medium ltr-nums">{r.orderNumber}</td>
                <td className="px-4 py-3">{r.customerCode}</td>
                <td className="px-4 py-3">{r.itemCode}{r.color ? ` (${r.color})` : ""}</td>
                <td className="px-4 py-3">
                  <Badge tone={r.currentStageStatus === "InProgress" ? "blue" : "gray"}>{r.currentStageName}</Badge>
                </td>
                <td className="px-4 py-3 ltr-nums">{r.minutesInStage != null ? `${Math.round(r.minutesInStage)} دقيقة` : "-"}</td>
                <td className="px-4 py-3"><Badge tone={priorityTone[r.priority]}>{priorityLabel[r.priority]}</Badge></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
