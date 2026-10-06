import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { DashboardApi } from "@/api/client";
import { Card, Badge, KpiCard, QuickTile, fmt, type BadgeTone } from "@/components/ui";
import { revealStyle } from "@/components/motion";
import { useI18n } from "@/i18n";
import {
  Users, Factory, ClipboardCheck, PackageCheck, FileWarning, Receipt,
  FormInput, BadgeDollarSign, AlertTriangle, FlaskConical, LifeBuoy,
  Layers, Truck, TrendingDown, Wallet, ShoppingCart, Banknote, ArrowUpRight
} from "lucide-react";
import {
  ResponsiveContainer, AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip
} from "recharts";
import { useTheme } from "@/components/ThemeProvider";

/**
 * Industrial dyehouse dashboard (spec section 7).
 *
 * Every figure is a live aggregate from the database - nothing here is
 * decorative or hardcoded, which is why there are NO "+2.12% vs last week"
 * chips: the API exposes no comparison period, and inventing a delta would be
 * exactly the fake dashboard number this project forbids. Where the reference
 * design shows a delta chip, this screen shows a real related count instead.
 *
 * The layout follows the reference visual language - a four-card KPI row, a
 * solid quick-stat strip, a hero area chart with a distribution panel beside
 * it, and a status-pilled activity table - while keeping the operational
 * groupings a dyehouse manager actually works from further down the page.
 */

/** The small tinted trend line inside a KPI card (dashboard-only chart). */
function Sparkline({ data, color, id }: {
  data: { date: string; kg: number }[]; color: string; id: string;
}) {
  return (
    <div className="mt-2 h-10 w-full" aria-hidden="true">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 2, right: 0, bottom: 0, left: 0 }}>
          <defs>
            <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={color} stopOpacity={0.35} />
              <stop offset="95%" stopColor={color} stopOpacity={0} />
            </linearGradient>
          </defs>
          <Area type="monotone" dataKey="kg" stroke={color} strokeWidth={2}
            fill={`url(#${id})`} dot={false}
            isAnimationActive animationDuration={600} animationEasing="ease-out" />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}

/** Segmented distribution panel (the reference's side "overview" card). */
function DistributionPanel({ items }: { items: { name: string; value: number; color: string }[] }) {
  const total = items.reduce((sum, item) => sum + item.value, 0);

  if (total === 0) return null;

  return (
    <div className="space-y-3.5">
      {items.map((item) => (
        <div key={item.name}>
          <div className="mb-1.5 flex items-center justify-between gap-2 text-xs">
            <span className="flex min-w-0 items-center gap-1.5 text-ink-muted">
              <span className="legend-dot" style={{ background: item.color }} />
              <span className="truncate">{item.name}</span>
            </span>
            <span className="ltr-nums shrink-0 font-semibold text-ink">{fmt(item.value)}</span>
          </div>
          <div className="seg-track">
            <div className="seg-fill"
              style={{ width: `${(item.value / total) * 100}%`, background: item.color }} />
          </div>
        </div>
      ))}
    </div>
  );
}

function Section({
  title,
  children,
  delay
}: {
  title: string;
  children: React.ReactNode;
  /** Stagger slot, so the sections cascade down the page instead of all firing
      at once. Transform-only, so it never affects the page height. */
  delay?: number;
}) {
  return (
    <section
      className={`mb-6 ${delay !== undefined ? "motion-rise" : ""}`}
      style={delay !== undefined ? revealStyle(delay) : undefined}
    >
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-ink-subtle">{title}</h2>
      {children}
    </section>
  );
}

/** Deterministic tone for a posted document type, so the activity table reads at a glance. */
function documentTone(documentType: string): BadgeTone {
  const type = documentType.toLowerCase();
  if (type.includes("receipt") || type.includes("message") || type.includes("ready")) return "success";
  if (type.includes("delivery") || type.includes("invoice")) return "info";
  if (type.includes("cancel")) return "danger";
  if (type.includes("adjust") || type.includes("transfer") || type.includes("issue")) return "warning";
  return "neutral";
}

export default function DashboardPage() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const { resolved } = useTheme();
  const isDark = resolved === "dark";
  const { data, isLoading } = useQuery({ queryKey: ["dashboard-summary"], queryFn: () => DashboardApi.summary() });

  if (isLoading || !data) {
    // A real skeleton (shimmer, not a pulsing block) that mirrors the KPI card
    // layout, so the dashboard's shape is already legible while it loads.
    return (
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
          <Card key={i} className="p-4 sm:p-5">
            <div className="skeleton h-3 w-24" />
            <div className="skeleton mt-3 h-6 w-16" />
            <div className="skeleton mt-3 h-2.5 w-32" />
          </Card>
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
    // Pastel multi-series palette echoing the reference area/line charts.
    series: isDark
      ? ["#60a5fa", "#34d399", "#a78bfa", "#fb7185", "#fbbf24"]
      : ["#2f80ed", "#2dce89", "#5b6ef5", "#f5365c", "#fb6340"],
    line: isDark ? "#60a5fa" : "#2f80ed",
    tooltipBg: isDark ? "#1f2937" : "#ffffff",
    tooltipBorder: isDark ? "#334155" : "#e5e7eb",
    tooltipText: isDark ? "#f3f4f6" : "#111827"
  };

  const attentionCount =
    data.pendingNegativeStockRisk + data.overdueChecksCount +
    data.pendingApprovalsCount + data.lowStockMaterialCount;

  return (
    <>
      {/* ---- Header: title, with a real attention summary where the
             reference puts its page action ---- */}
      <div className="mb-5 flex flex-col gap-3 sm:mb-6 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <h1 className="page-title">{t("nav.dashboard")}</h1>
          <p className="page-subtitle">{t("app.subtitle")}</p>
        </div>
        <button
          type="button"
          onClick={() => navigate("/approvals")}
          className="flex shrink-0 items-center gap-2 self-start rounded-lg border border-line bg-surface px-3 py-2 text-xs font-semibold text-ink shadow-card transition-colors hover:bg-surface-sunken"
        >
          <AlertTriangle size={15} className="text-warning" />
          <span className="truncate">{t("dash.needsAttention", "يحتاج متابعة")}</span>
          <span className="ltr-nums rounded-full bg-warning-soft px-2 py-0.5 text-2xs font-bold text-warning-ink">
            {fmt(attentionCount)}
          </span>
        </button>
      </div>

      {/* ---- KPI row (the reference's headline cards) ---- */}
      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard
          label={t("dash.customerRawMaterial")}
          value={Math.round(data.customerRawMaterialKg)}
          footnote={`${t("fr.rawMessage")} · 14d (KG)`}
          tone="success"
          icon={<Layers size={18} />}
          revealIndex={0}
          countUp
        >
          <Sparkline id="kpi-raw" data={receiptsChartData} color={chart.series[0]} />
        </KpiCard>

        <KpiCard
          label={t("dash.activeJobOrders")}
          value={data.activeProductionOrders}
          footnote={`${t("dash.wip")}: ${fmt(data.workInProgressOrders)}`}
          tone="brand"
          icon={<Factory size={18} />}
          revealIndex={1}
          countUp
        />

        <KpiCard
          label={t("dash.readyGoodsKg")}
          value={Math.round(data.readyGoodsBalanceKg)}
          footnote={`${t("dash.pendingFormationRequests")}: ${fmt(data.pendingFormationRequests)}`}
          tone="info"
          icon={<PackageCheck size={18} />}
          revealIndex={2}
          countUp
        />

        <KpiCard
          label={t("dash.pendingApprovals")}
          value={data.pendingApprovalsCount}
          footnote={`${t("dash.lowStock")}: ${fmt(data.lowStockMaterialCount)}`}
          tone="warning"
          icon={<ClipboardCheck size={18} />}
          revealIndex={3}
          countUp
        />
      </div>

      {/* ---- Solid quick-stat strip (finance position) ---- */}
      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <QuickTile index={1} icon={<Receipt size={20} />} label={t("dash.openInvoices")}
          delay={170}
          value={`${fmt(data.openInvoicesCount)} · ${fmt(Math.round(data.openInvoicesTotal))}`} />
        <QuickTile index={2} icon={<Wallet size={20} />} label={t("dash.customerBalance")}
          delay={215}
          value={fmt(Math.round(data.outstandingCustomerBalance))} />
        <QuickTile index={3} icon={<ShoppingCart size={20} />} label={t("dash.supplierBalance")}
          delay={260}
          value={fmt(Math.round(data.outstandingSupplierBalance))} />
        <QuickTile index={4} icon={<BadgeDollarSign size={20} />} label={t("dash.checksInHand")}
          delay={305}
          value={fmt(Math.round(data.checksInHandAmount))} />
      </div>

      {/* ---- Hero chart + distribution panel ---- */}
      <div className="mb-6 grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="p-4 sm:p-5 lg:col-span-2" delay={330}>
          <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
            <div className="text-sm font-semibold text-ink">{t("fr.rawMessage")} - 14d (KG)</div>
            <span className="flex items-center gap-1.5 text-2xs text-ink-subtle">
              <span className="legend-dot" style={{ background: chart.series[0] }} />
              {t("fr.rawMessage")}
            </span>
          </div>
          <ResponsiveContainer width="100%" height={260}>
            <AreaChart data={receiptsChartData}>
              <defs>
                <linearGradient id="hero-raw" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={chart.series[0]} stopOpacity={0.28} />
                  <stop offset="95%" stopColor={chart.series[0]} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke={chart.grid} vertical={false} />
              <XAxis dataKey="date" fontSize={11} stroke={chart.axis} tickLine={false} axisLine={false} />
              <YAxis fontSize={11} stroke={chart.axis} tickLine={false} axisLine={false} />
              <Tooltip
                contentStyle={{
                  background: chart.tooltipBg, border: `1px solid ${chart.tooltipBorder}`,
                  color: chart.tooltipText, borderRadius: 8
                }}
              />
              <Area type="monotone" dataKey="kg" name={t("fr.rawMessage")}
                stroke={chart.line} strokeWidth={2} fill="url(#hero-raw)" dot={false}
                isAnimationActive animationDuration={800} animationEasing="ease-out" />
            </AreaChart>
          </ResponsiveContainer>
        </Card>

        <Card className="p-4 sm:p-5" delay={330}>
          <div className="mb-4 flex items-center justify-between gap-2">
            <div className="text-sm font-semibold text-ink">{t("nav.jobOrders")}</div>
            <ArrowUpRight size={16} className="text-ink-subtle" />
          </div>

          {statusChartData.length === 0 ? (
            <div className="flex h-[220px] items-center justify-center text-sm text-ink-subtle">
              {t("common.empty")}
            </div>
          ) : (
            <DistributionPanel
              items={statusChartData.map((s, i) => ({
                name: s.name,
                value: s.value,
                color: chart.series[i % chart.series.length]
              }))}
            />
          )}
        </Card>
      </div>

      {/* ---- Needs attention ---- */}
      <Section title={t("dash.needsAttention", "يحتاج متابعة")} delay={370}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard tone="danger" icon={<FileWarning size={18} />} label={t("dash.negativeStockOverrides")} value={data.pendingNegativeStockRisk} />
          <KpiCard tone="danger" icon={<AlertTriangle size={18} />} label={t("dash.overdueChecks")} value={data.overdueChecksCount} />
          <KpiCard tone="warning" icon={<ClipboardCheck size={18} />} label={t("dash.pendingApprovals")} value={data.pendingApprovalsCount} />
          <KpiCard tone="warning" icon={<TrendingDown size={18} />} label={t("dash.lowStock")} value={data.lowStockMaterialCount} />
        </div>
      </Section>

      {/* ---- Production ---- */}
      <Section title={t("nav.group.production")} delay={402}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard icon={<Factory size={18} />} label={t("dash.activeJobOrders")} value={data.activeProductionOrders} />
          <KpiCard tone="warning" icon={<Layers size={18} />} label={t("dash.wip")} value={data.workInProgressOrders} />
          <KpiCard tone="success" icon={<PackageCheck size={18} />} label={t("dash.readyGoodsKg")} value={Math.round(data.readyGoodsBalanceKg)} />
          <KpiCard tone="info" icon={<ClipboardCheck size={18} />} label={t("dash.pendingInspections")} value={data.pendingInspections} />
          <KpiCard icon={<FormInput size={18} />} label={t("dash.pendingFormationRequests")} value={data.pendingFormationRequests} />
          <KpiCard tone="info" icon={<FormInput size={18} />} label={t("dash.formationInProgress")} value={data.formationRequestsInProgress} />
          <KpiCard tone="warning" icon={<Truck size={18} />} label={t("dash.externalProcessing")} value={data.externalProcessingOutstandingCount} />
        </div>
      </Section>

      {/* ---- Materials & inventory ---- */}
      <Section title={t("nav.group.materials")} delay={434}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard icon={<Layers size={18} />} label={t("dash.customerRawMaterial")} value={Math.round(data.customerRawMaterialKg)} />
          <KpiCard tone="brand" icon={<FlaskConical size={18} />} label={t("dash.materialStock")} value={Math.round(data.materialStockKg)} />
          <KpiCard tone="info" icon={<LifeBuoy size={18} />} label={t("dash.supplyStock")} value={Math.round(data.operatingSupplyStockKg)} />
        </div>
      </Section>

      {/* ---- Finance ---- */}
      <Section title={t("nav.group.finance")} delay={466}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard tone="warning" icon={<Wallet size={18} />} label={t("dash.customerBalance")} value={Math.round(data.outstandingCustomerBalance)} />
          <KpiCard tone="danger" icon={<Wallet size={18} />} label={t("dash.supplierBalance")} value={Math.round(data.outstandingSupplierBalance)} />
          <KpiCard tone="brand" icon={<Receipt size={18} />} label={t("dash.openInvoices")} value={`${fmt(data.openInvoicesCount)} (${fmt(Math.round(data.openInvoicesTotal))})`} />
          <KpiCard tone="success" icon={<BadgeDollarSign size={18} />} label={t("dash.checksInHand")} value={Math.round(data.checksInHandAmount)} />
          <KpiCard tone="warning" icon={<BadgeDollarSign size={18} />} label={t("dash.checksDueSoon")} value={data.checksDueSoonCount} />
        </div>
      </Section>

      {/* ---- Master data ---- */}
      <Section title={t("nav.group.master")} delay={498}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiCard icon={<Users size={18} />} label={t("dash.activeCustomers")} value={data.activeCustomers} />
          <KpiCard icon={<Users size={18} />} label={t("nav.suppliers")} value={data.activeSuppliers} />
          <KpiCard icon={<ShoppingCart size={18} />} label={t("nav.purchases")} value={t("dash.reference", "—")} />
          <KpiCard icon={<Banknote size={18} />} label={t("nav.payroll")} value={t("dash.reference", "—")} />
        </div>
      </Section>

      {/* ---- Recent posted documents: real ledger-backed activity ---- */}
      <Card className="p-4 sm:p-5" delay={530}>
        <div className="mb-4 flex items-center justify-between gap-2">
          <div className="text-sm font-semibold text-ink">{t("dash.recentTransactions")}</div>
          <button
            type="button"
            onClick={() => navigate("/audit-log")}
            className="btn-link"
          >
            {t("common.details")}
          </button>
        </div>
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
                    <td>
                      <Badge tone={documentTone(r.documentType)}>{r.documentType}</Badge>
                    </td>
                    <td>{r.party ?? r.summary ?? "—"}</td>
                    <td className="ltr-nums text-ink-muted">{new Date(r.date).toLocaleDateString("en-GB")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}
