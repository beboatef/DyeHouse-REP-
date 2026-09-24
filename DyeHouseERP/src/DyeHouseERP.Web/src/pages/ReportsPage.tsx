import { useQuery } from "@tanstack/react-query";
import { ReportsApi } from "@/api/client";
import { PageHeader, Card, Button } from "@/components/ui";

export default function ReportsPage() {
  const { data: overrides, isLoading } = useQuery({ queryKey: ["negative-stock-overrides"], queryFn: () => ReportsApi.negativeStockOverrides() });

  return (
    <>
      <PageHeader
        title="تقرير تجاوز الرصيد السالب"
        subtitle="كل تجاوز لقاعدة منع الرصيد السالب - من طلبه، من اعتمده، والرصيد قبل وبعد"
        action={
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => ReportsApi.downloadNegativeStockOverridesPdf()}>تنزيل PDF</Button>
            <Button variant="secondary" onClick={() => ReportsApi.downloadNegativeStockOverridesExcel()}>تنزيل Excel</Button>
          </div>
        }
      />
      <Card>
        <table className="w-full text-sm">
          <thead><tr className="border-b border-gray-200 text-gray-500 text-xs">
            <th className="text-start px-4 py-3 font-medium">التاريخ</th><th className="text-start px-4 py-3 font-medium">الرسالة</th>
            <th className="text-start px-4 py-3 font-medium">العميل</th><th className="text-start px-4 py-3 font-medium">الصنف</th>
            <th className="text-start px-4 py-3 font-medium">المطلوب</th><th className="text-start px-4 py-3 font-medium">قبل</th>
            <th className="text-start px-4 py-3 font-medium">بعد</th><th className="text-start px-4 py-3 font-medium">السبب</th>
            <th className="text-start px-4 py-3 font-medium">طلبه</th><th className="text-start px-4 py-3 font-medium">اعتمده</th>
          </tr></thead>
          <tbody>
            {isLoading && <tr><td colSpan={10} className="px-4 py-6 text-center text-gray-400">جارٍ التحميل...</td></tr>}
            {!isLoading && overrides?.length === 0 && <tr><td colSpan={10} className="px-4 py-6 text-center text-gray-400">لا توجد تجاوزات مسجلة</td></tr>}
            {overrides?.map((o) => (
              <tr key={o.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
                <td className="px-4 py-3 ltr-nums">{new Date(o.approvedAtUtc).toLocaleString("en-GB")}</td>
                <td className="px-4 py-3 ltr-nums">{o.messageNumber}</td>
                <td className="px-4 py-3">{o.customerCode}</td>
                <td className="px-4 py-3">{o.itemCode}</td>
                <td className="px-4 py-3 ltr-nums">{o.requestedQuantity}</td>
                <td className="px-4 py-3 ltr-nums">{o.balanceBefore}</td>
                <td className="px-4 py-3 ltr-nums">{o.resultingBalance}</td>
                <td className="px-4 py-3">{o.reason}</td>
                <td className="px-4 py-3">{o.requestedBy}</td>
                <td className="px-4 py-3">{o.approvedBy}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
