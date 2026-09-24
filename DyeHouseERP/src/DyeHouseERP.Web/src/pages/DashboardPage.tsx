import { useQuery } from "@tanstack/react-query";
import { DashboardApi } from "@/api/client";
import { Card } from "@/components/ui";
import {
  Users, Factory, ClipboardCheck, PackageCheck, FileWarning, Receipt
} from "lucide-react";
import {
  ResponsiveContainer, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  PieChart, Pie, Cell, Legend
} from "recharts";

const statusLabel: Record<string, string> = {
  Draft: "مسودة", RawAllocated: "تم تخصيص الخام", InProduction: "قيد التشغيل",
  Completed: "مكتمل", Cancelled: "ملغي"
};

const pieColors = ["#6366f1", "#f59e0b", "#10b981", "#64748b", "#ef4444"];

function StatCard({
  icon, label, value, tone
}: { icon: React.ReactNode; label: string; value: string | number; tone: string }) {
  return (
    <Card className="p-5 flex items-center gap-4">
      <div className={`w-12 h-12 rounded-xl flex items-center justify-center ${tone}`}>{icon}</div>
      <div>
        <div className="text-2xl font-bold text-gray-900 ltr-nums">{value}</div>
        <div className="text-xs text-gray-500 mt-0.5">{label}</div>
      </div>
    </Card>
  );
}

export default function DashboardPage() {
  const { data, isLoading } = useQuery({ queryKey: ["dashboard-summary"], queryFn: () => DashboardApi.summary() });

  if (isLoading || !data) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        {[1, 2, 3, 4, 5, 6].map((i) => <Card key={i} className="p-5 h-24 animate-pulse bg-gray-100" />)}
      </div>
    );
  }

  const statusChartData = data.productionOrdersByStatus.map((s) => ({ name: statusLabel[s.status] ?? s.status, value: s.count }));
  const receiptsChartData = data.rawReceiptsLast14Days.map((r) => ({
    date: new Date(r.date).toLocaleDateString("en-GB", { day: "2-digit", month: "2-digit" }),
    kg: r.totalKg
  }));

  return (
    <>
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">لوحة التحكم</h1>
        <p className="text-sm text-gray-500 mt-1">نظرة عامة حية على العمليات - كل الأرقام هنا من قاعدة البيانات مباشرة</p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 mb-6">
        <StatCard icon={<Users size={22} className="text-indigo-600" />} tone="bg-indigo-50" label="عملاء نشطون" value={data.activeCustomers} />
        <StatCard icon={<Factory size={22} className="text-amber-600" />} tone="bg-amber-50" label="أوامر تشغيل جارية" value={data.activeProductionOrders} />
        <StatCard icon={<ClipboardCheck size={22} className="text-emerald-600" />} tone="bg-emerald-50" label="بانتظار فحص الاستلام" value={data.pendingInspections} />
        <StatCard icon={<PackageCheck size={22} className="text-sky-600" />} tone="bg-sky-50" label="رصيد المخزون الجاهز (كجم)" value={Math.round(data.readyGoodsBalanceKg)} />
        <StatCard icon={<Receipt size={22} className="text-violet-600" />} tone="bg-violet-50" label="فواتير مستحقة" value={`${data.openInvoicesCount} (${Math.round(data.openInvoicesTotal)})`} />
        <StatCard icon={<FileWarning size={22} className="text-rose-600" />} tone="bg-rose-50" label="تجاوز رصيد سالب هذا الشهر" value={data.pendingNegativeStockRisk} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card className="p-5 lg:col-span-2">
          <div className="text-sm font-semibold text-gray-700 mb-4">استلام الخام - آخر 14 يوم (كجم)</div>
          <ResponsiveContainer width="100%" height={260}>
            <LineChart data={receiptsChartData}>
              <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
              <XAxis dataKey="date" fontSize={11} />
              <YAxis fontSize={11} />
              <Tooltip />
              <Line type="monotone" dataKey="kg" stroke="#6366f1" strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </Card>

        <Card className="p-5">
          <div className="text-sm font-semibold text-gray-700 mb-4">أوامر التشغيل حسب الحالة</div>
          {statusChartData.length === 0 ? (
            <div className="h-[260px] flex items-center justify-center text-sm text-gray-400">لا توجد أوامر تشغيل بعد</div>
          ) : (
            <ResponsiveContainer width="100%" height={260}>
              <PieChart>
                <Pie data={statusChartData} dataKey="value" nameKey="name" innerRadius={55} outerRadius={85} paddingAngle={2}>
                  {statusChartData.map((_, i) => <Cell key={i} fill={pieColors[i % pieColors.length]} />)}
                </Pie>
                <Tooltip />
                <Legend wrapperStyle={{ fontSize: 12 }} />
              </PieChart>
            </ResponsiveContainer>
          )}
        </Card>
      </div>
    </>
  );
}
