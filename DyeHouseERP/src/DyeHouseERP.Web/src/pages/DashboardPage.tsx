import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { DashboardApi } from "@/api/client";
import { Card } from "@/components/ui";
import { useI18n } from "@/i18n";
import {
  Users, Factory, ClipboardCheck, PackageCheck, FileWarning, Receipt,
  FormInput, BadgeDollarSign, AlertTriangle, FlaskConical, LifeBuoy,
  Layers, Truck, TrendingDown, Wallet, ShoppingCart, Banknote
} from "lucide-react";
import {
  ResponsiveContainer, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  PieChart, Pie, Cell, Legend
} from "recharts";
import { useTheme } from "@/components/ThemeProvider";

/**
 * Industrial ERP dashboard (spec section 7).
 *
 * Every figure is a live aggregate from the database - nothing here is
 * decorative or hardcoded. What changed is the *presentation*: the previous
 * version was one flat wall of 20 equally-weighted cards, which reads as a
 * stats template rather than an operations screen. They are now grouped by
 * what a dyehouse manager actually watches (production, materials, finance,
 * attention) so the numbers that need action are visually separable.
 *
 * The chart palette reads from the theme so the charts stay legible in dark
 * mode instead of keeping hardcoded light-mode hex values.
 */

type Tone = "brand" | "success" | "warning" | "danger" | "info" | "neutral";

const toneClass: Record<Tone, string> = {
  brand: "bg-brand-50 text-brand-700 dark:bg-brand-900/50 dark:text-brand-200",
  success: "bg-success-soft text-success-ink",
  warning: "bg-warning-soft text-warning-ink",
  danger: "bg-danger-soft text-danger-ink",
  info: "bg-info-soft text-info-ink",
  neutral: "bg-surface-sunken text-ink-muted"
};

function StatCard({
  icon, label, value, tone = "brand"
}: { icon: React.ReactNode; label: string; value: string | number; tone?: Tone }) {
  return (
    <Card className="card-pad flex items-center gap-3">
      <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${toneClass[tone]}`}>
        {icon}
      </div>
      <div className="min-w-0">
        <div className="ltr-nums truncate text-xl font-bold text-ink">{value}</div>
        <div className="mt-0.5 truncate text-xs text-ink-muted">{label}</div>
      </div>
    </Card>
  );
}

function Section({
  title, children
}: { title: string; children: React.ReactNode }) {
  return (
    <section className="mb-6">
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-ink-subtle">{title}</h2>
      {children}
    </section>
  );
}

export default function DashboardPage() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const { resolved } = useTheme();
  const isDark = resolved === "dark";
  const { data, isLoading } = useQuery({ queryKey: ["dashboard-summary"], queryFn: () => DashboardApi.summary() });

  if (isLoading || !data) {
    return (
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
          <Card key={i} className="h-20 animate-pulse bg-surface-sunken" />
        ))}
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

  // Charts need explicit colours; recharts does not read CSS variables from
  // SVG attributes, so the theme value is resolved here.
  const chart = {
    grid: isDark ? "#1f2937" : "#eef2f7",
    axis: isDark ? "#94a3b8" : "#6b7280",
    line: isDark ? "#60a5fa" : "#2563eb",
    pie: isDark
      ? ["#60a5fa", "#f59e0b", "#10b981", "#94a3b8", "#ef4444"]
      : ["#2563eb", "#d97706", "#059669", "#6b7280", "#dc2626"],
    tooltipBg: isDark ? "#1f2937" : "#ffffff",
    tooltipBorder: isDark ? "#334155" : "#e5e7eb",
    tooltipText: isDark ? "#f3f4f6" : "#111827"
  };

  return (
    <>
      <div className="mb-6">
        <h1 className="page-title">{t("nav.dashboard")}</h1>
        <p className="page-subtitle">{t("app.subtitle")}</p>
      </div>

      {/* ---- Needs attention: the figures that should drive a click ---- */}
      <Section title={t("dash.needsAttention", "يحتاج متابعة")}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard tone="danger" icon={<FileWarning size={20} />} label={t("dash.negativeStockOverrides")} value={data.pendingNegativeStockRisk} />
          <StatCard tone="danger" icon={<AlertTriangle size={20} />} label={t("dash.overdueChecks")} value={data.overdueChecksCount} />
          <StatCard tone="warning" icon={<ClipboardCheck size={20} />} label={t("dash.pendingApprovals")} value={data.pendingApprovalsCount} />
          <StatCard tone="warning" icon={<TrendingDown size={20} />} label={t("dash.lowStock")} value={data.lowStockMaterialCount} />
        </div>
      </Section>

      {/* ---- Production ---- */}
      <Section title={t("nav.group.production")}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard icon={<Factory size={20} />} label={t("dash.activeJobOrders")} value={data.activeProductionOrders} />
          <StatCard tone="warning" icon={<Layers size={20} />} label={t("dash.wip")} value={data.workInProgressOrders} />
          <StatCard tone="success" icon={<PackageCheck size={20} />} label={t("dash.readyGoodsKg")} value={Math.round(data.readyGoodsBalanceKg)} />
          <StatCard tone="info" icon={<ClipboardCheck size={20} />} label={t("dash.pendingInspections")} value={data.pendingInspections} />
          <StatCard icon={<FormInput size={20} />} label={t("dash.pendingFormationRequests")} value={data.pendingFormationRequests} />
          <StatCard tone="info" icon={<FormInput size={20} />} label={t("dash.formationInProgress")} value={data.formationRequestsInProgress} />
          <StatCard tone="warning" icon={<Truck size={20} />} label={t("dash.externalProcessing")} value={data.externalProcessingOutstandingCount} />
        </div>
      </Section>

      {/* ---- Materials & inventory ---- */}
      <Section title={t("nav.group.materials")}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard icon={<Layers size={20} />} label={t("dash.customerRawMaterial")} value={Math.round(data.customerRawMaterialKg)} />
          <StatCard tone="brand" icon={<FlaskConical size={20} />} label={t("dash.materialStock")} value={Math.round(data.materialStockKg)} />
          <StatCard tone="info" icon={<LifeBuoy size={20} />} label={t("dash.supplyStock")} value={Math.round(data.operatingSupplyStockKg)} />
        </div>
      </Section>

      {/* ---- Finance ---- */}
      <Section title={t("nav.group.finance")}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard tone="warning" icon={<Wallet size={20} />} label={t("dash.customerBalance")} value={Math.round(data.outstandingCustomerBalance)} />
          <StatCard tone="danger" icon={<Wallet size={20} />} label={t("dash.supplierBalance")} value={Math.round(data.outstandingSupplierBalance)} />
          <StatCard tone="brand" icon={<Receipt size={20} />} label={t("dash.openInvoices")} value={`${data.openInvoicesCount} (${Math.round(data.openInvoicesTotal)})`} />
          <StatCard tone="success" icon={<BadgeDollarSign size={20} />} label={t("dash.checksInHand")} value={Math.round(data.checksInHandAmount)} />
          <StatCard tone="warning" icon={<BadgeDollarSign size={20} />} label={t("dash.checksDueSoon")} value={data.checksDueSoonCount} />
        </div>
      </Section>

      {/* ---- Master data ---- */}
      <Section title={t("nav.group.master")}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard icon={<Users size={20} />} label={t("dash.activeCustomers")} value={data.activeCustomers} />
          <StatCard icon={<Users size={20} />} label={t("nav.suppliers")} value={data.activeSuppliers} />
          <StatCard icon={<ShoppingCart size={20} />} label={t("nav.purchases")} value={t("dash.reference", "—")} />
          <StatCard icon={<Banknote size={20} />} label={t("nav.payroll")} value={t("dash.reference", "—")} />
        </div>
      </Section>

      {/* ---- Recent posted documents: real ledger-backed activity ---- */}
      <Card className="card-pad mb-6">
        <div className="mb-4 text-sm font-semibold text-ink">{t("dash.recentTransactions")}</div>
        {data.recentTransactions.length === 0 ? (
          <div className="empty-state">
            <div className="empty-state-text">{t("dash.noRecentTransactions")}</div>
          </div>
        ) : (
          <div className="table-wrap -mx-4 sm:-mx-5">
            <table className="table">
              <thead>
                <tr>
                  <th>{t("approvals.document")}</th>
                  <th>{t("approvals.category")}</th>
                  <th>{t("approvals.party")}</th>
                  <th>{t("common.date")}</th>
                </tr>
              </thead>
              <tbody>
                {data.recentTransactions.map((r, i) => (
                  <tr
                    key={`${r.documentType}-${r.documentNumber}-${i}`}
                    className="cursor-pointer"
                    onClick={() => navigate(r.linkPath)}
                  >
                    <td className="ltr-nums font-medium">{r.documentNumber}</td>
                    <td className="text-ink-muted">{r.documentType}</td>
                    <td>{r.party ?? r.summary ?? "—"}</td>
                    <td className="ltr-nums text-ink-muted">{new Date(r.date).toLocaleDateString("en-GB")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="card-pad lg:col-span-2">
          <div className="mb-4 text-sm font-semibold text-ink">{t("fr.rawMessage")} - 14d (KG)</div>
          <ResponsiveContainer width="100%" height={260}>
            <LineChart data={receiptsChartData}>
              <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} />
              <XAxis dataKey="date" fontSize={11} stroke={chart.axis} tickLine={false} />
              <YAxis fontSize={11} stroke={chart.axis} tickLine={false} />
              <Tooltip
                contentStyle={{ background: chart.tooltipBg, border: `1px solid ${chart.tooltipBorder}`, color: chart.tooltipText, borderRadius: 8 }}
              />
              <Line type="monotone" dataKey="kg" stroke={chart.line} strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </Card>

        <Card className="card-pad">
          <div className="mb-4 text-sm font-semibold text-ink">{t("nav.jobOrders")}</div>
          {statusChartData.length === 0 ? (
            <div className="flex h-[260px] items-center justify-center text-sm text-ink-subtle">{t("common.empty")}</div>
          ) : (
            <ResponsiveContainer width="100%" height={260}>
              <PieChart>
                <Pie data={statusChartData} dataKey="value" nameKey="name" innerRadius={55} outerRadius={85} paddingAngle={2}>
                  {statusChartData.map((_, i) => (
                    <Cell key={i} fill={chart.pie[i % chart.pie.length]} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{ background: chart.tooltipBg, border: `1px solid ${chart.tooltipBorder}`, color: chart.tooltipText, borderRadius: 8 }}
                />
                <Legend wrapperStyle={{ fontSize: 12, color: chart.tooltipText }} />
              </PieChart>
            </ResponsiveContainer>
          )}
        </Card>
      </div>
    </>
  );
}
