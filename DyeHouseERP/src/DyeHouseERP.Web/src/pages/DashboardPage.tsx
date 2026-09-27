import { useQuery } from "@tanstack/react-query";
import { DashboardApi } from "@/api/client";
import { Card } from "@/components/ui";
import { useI18n } from "@/i18n";
import {
  Users, Factory, ClipboardCheck, PackageCheck, FileWarning, Receipt,
  FormInput, BadgeDollarSign, AlertTriangle
} from "lucide-react";
import {
  ResponsiveContainer, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  PieChart, Pie, Cell, Legend
} from "recharts";

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
  const { t } = useI18n();
  const { data, isLoading } = useQuery({ queryKey: ["dashboard-summary"], queryFn: () => DashboardApi.summary() });

  if (isLoading || !data) {
    return (
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        {[1, 2, 3, 4, 5, 6].map((i) => <Card key={i} className="p-5 h-24 animate-pulse bg-gray-100" />)}
      </div>
    );
  }

  const statusChartData = data.productionOrdersByStatus.map((s) => ({
    name: t(`prod.status.${s.status}`, s.status),
    value: s.count
  }));
  const receiptsChartData = data.rawReceiptsLast14Days.map((r) => ({
    date: new Date(r.date).toLocaleDateString("en-GB", { day: "2-digit", month: "2-digit" }),
    kg: r.totalKg
  }));

  return (
    <>
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">{t("nav.dashboard")}</h1>
        <p className="text-sm text-gray-500 mt-1">{t("app.subtitle")}</p>
      </div>

      {/* Every figure is a live aggregate from the database - no decorative or hardcoded numbers. */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 mb-6">
        <StatCard icon={<Users size={22} className="text-indigo-600" />} tone="bg-indigo-50" label={t("dash.activeCustomers")} value={data.activeCustomers} />
        <StatCard icon={<Factory size={22} className="text-amber-600" />} tone="bg-amber-50" label={t("dash.activeJobOrders")} value={data.activeProductionOrders} />
        <StatCard icon={<ClipboardCheck size={22} className="text-emerald-600" />} tone="bg-emerald-50" label={t("dash.pendingInspections")} value={data.pendingInspections} />
        <StatCard icon={<PackageCheck size={22} className="text-sky-600" />} tone="bg-sky-50" label={t("dash.readyGoodsKg")} value={Math.round(data.readyGoodsBalanceKg)} />
        <StatCard icon={<Receipt size={22} className="text-violet-600" />} tone="bg-violet-50" label={t("dash.openInvoices")} value={`${data.openInvoicesCount} (${Math.round(data.openInvoicesTotal)})`} />
        <StatCard icon={<FileWarning size={22} className="text-rose-600" />} tone="bg-rose-50" label={t("dash.negativeStockOverrides")} value={data.pendingNegativeStockRisk} />

        {/* Formation requests and checks (spec section 51) */}
        <StatCard icon={<FormInput size={22} className="text-cyan-600" />} tone="bg-cyan-50" label={t("dash.pendingFormationRequests")} value={data.pendingFormationRequests} />
        <StatCard icon={<FormInput size={22} className="text-blue-600" />} tone="bg-blue-50" label={t("dash.formationInProgress")} value={data.formationRequestsInProgress} />
        <StatCard icon={<BadgeDollarSign size={22} className="text-emerald-600" />} tone="bg-emerald-50" label={t("dash.checksInHand")} value={Math.round(data.checksInHandAmount)} />
        <StatCard icon={<BadgeDollarSign size={22} className="text-amber-600" />} tone="bg-amber-50" label={t("dash.checksDueSoon")} value={data.checksDueSoonCount} />
        <StatCard icon={<AlertTriangle size={22} className="text-rose-600" />} tone="bg-rose-50" label={t("dash.overdueChecks")} value={data.overdueChecksCount} />
        <StatCard icon={<Users size={22} className="text-slate-600" />} tone="bg-slate-100" label={t("nav.suppliers")} value={data.activeSuppliers} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card className="p-5 lg:col-span-2">
          <div className="text-sm font-semibold text-gray-700 mb-4">{t("fr.rawMessage")} - 14d (KG)</div>
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
          <div className="text-sm font-semibold text-gray-700 mb-4">{t("nav.jobOrders")}</div>
          {statusChartData.length === 0 ? (
            <div className="h-[260px] flex items-center justify-center text-sm text-gray-400">{t("common.empty")}</div>
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
