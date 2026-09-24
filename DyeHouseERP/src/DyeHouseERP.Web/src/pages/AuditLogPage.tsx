import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AuditLogApi } from "@/api/client";
import { PageHeader, Card, Input, Badge } from "@/components/ui";

const actionTone: Record<string, "green" | "blue" | "red" | "yellow" | "gray"> = {
  Create: "green", Update: "blue", Delete: "red", Login: "green", LoginFailed: "red"
};

export default function AuditLogPage() {
  const [entityName, setEntityName] = useState("");
  const [userName, setUserName] = useState("");

  const { data: entries, isLoading, error } = useQuery({
    queryKey: ["audit-log", entityName, userName],
    queryFn: () => AuditLogApi.list({ entityName: entityName || undefined, userName: userName || undefined })
  });

  return (
    <>
      <PageHeader title="سجل التدقيق" subtitle="كل حركة إنشاء/تعديل/حذف مسجلة تلقائيًا - من عمل إيه، امتى، وقبل/بعد التغيير" />

      <Card className="p-4 mb-4 flex flex-wrap gap-3">
        <Input placeholder="اسم الكيان (مثال: ProductionOrder)" value={entityName} onChange={(e) => setEntityName(e.target.value)} className="max-w-xs" />
        <Input placeholder="اسم المستخدم" value={userName} onChange={(e) => setUserName(e.target.value)} className="max-w-xs" />
      </Card>

      {error && (
        <Card className="p-4 mb-4 border-red-300 bg-red-50 text-sm text-red-700">
          هذه الشاشة متاحة فقط لمن يملك دور admin.
        </Card>
      )}

      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">الوقت</th><th className="text-start px-4 py-3 font-medium">المستخدم</th>
            <th className="text-start px-4 py-3 font-medium">الإجراء</th><th className="text-start px-4 py-3 font-medium">الكيان</th>
            <th className="text-start px-4 py-3 font-medium">المعرّف</th><th className="text-start px-4 py-3 font-medium">IP</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && entries?.length === 0 && <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-400">لا توجد حركات مسجلة</td></tr>}
            {entries?.map((e) => (
              <tr key={e.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 ltr-nums">{new Date(e.occurredAtUtc).toLocaleString("en-GB")}</td>
                <td className="px-4 py-3">{e.userName}</td>
                <td className="px-4 py-3"><Badge tone={actionTone[e.action] ?? "gray"}>{e.action}</Badge></td>
                <td className="px-4 py-3">{e.entityName}</td>
                <td className="px-4 py-3 ltr-nums text-xs text-gray-500">{e.entityId?.slice(0, 8) ?? "-"}</td>
                <td className="px-4 py-3 ltr-nums text-xs text-gray-500">{e.ipAddress ?? "-"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
