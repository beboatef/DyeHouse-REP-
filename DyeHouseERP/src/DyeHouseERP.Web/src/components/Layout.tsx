import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { SettingsApi } from "@/api/client";
import { useI18n } from "@/i18n";
import {
  LayoutDashboard, Users, Package, Warehouse,
  ClipboardList, Layers3, GitBranch, FlaskConical, PackageCheck, Truck, Receipt,
  Wallet, FileBarChart, ScrollText, UserCog, Settings, LogOut, Factory,
  FormInput, Ruler, BadgeDollarSign, LifeBuoy, Languages, ShieldCheck,
  ShoppingCart, Banknote
} from "lucide-react";

type NavItem = { to: string; labelKey: string; icon: React.ReactNode; end?: boolean };
type NavGroup = { titleKey: string; items: NavItem[] };

/**
 * Main navigation (spec section 4), plus the single Warehouse module required by
 * section 14: receipts, issues, returns, transfers, adjustments, external
 * processing, balances and reports all live inside /warehouse instead of each
 * having its own top-level sidebar entry. Their individual routes are kept
 * working so existing bookmarks and deep links never break.
 */
const navGroups: NavGroup[] = [
  {
    titleKey: "nav.group.main",
    items: [
      { to: "/", labelKey: "nav.dashboard", icon: <LayoutDashboard size={17} />, end: true },
      { to: "/production-floor", labelKey: "nav.productionFloor", icon: <Factory size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.master",
    items: [
      { to: "/customers", labelKey: "nav.customers", icon: <Users size={17} /> },
      { to: "/suppliers", labelKey: "nav.suppliers", icon: <HandshakeIcon /> },
      { to: "/items", labelKey: "nav.items", icon: <Package size={17} /> },
      { to: "/warehouses", labelKey: "nav.warehouses", icon: <Warehouse size={17} /> },
      { to: "/production-stages", labelKey: "nav.productionStages", icon: <Layers3 size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.warehouse",
    items: [{ to: "/warehouse", labelKey: "nav.warehouseHub", icon: <Warehouse size={17} /> }]
  },
  {
    titleKey: "nav.group.formation",
    items: [
      { to: "/formation-requests", labelKey: "nav.formationRequests", icon: <FormInput size={17} /> },
      { to: "/formation-specifications", labelKey: "nav.formationSpecifications", icon: <Ruler size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.production",
    items: [
      { to: "/production-orders", labelKey: "nav.jobOrders", icon: <ClipboardList size={17} /> },
      { to: "/separates", labelKey: "nav.production", icon: <GitBranch size={17} /> },
      { to: "/ready-goods", labelKey: "nav.readyGoods", icon: <PackageCheck size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.materials",
    items: [
      { to: "/materials", labelKey: "nav.materials", icon: <FlaskConical size={17} /> },
      { to: "/warehouse?tab=master", labelKey: "nav.operatingSupplies", icon: <LifeBuoy size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.finance",
    items: [
      { to: "/deliveries", labelKey: "nav.delivery", icon: <Truck size={17} /> },
      { to: "/invoices", labelKey: "nav.invoices", icon: <Receipt size={17} /> },
      { to: "/treasury", labelKey: "nav.treasury", icon: <Wallet size={17} /> },
      { to: "/checks", labelKey: "nav.checks", icon: <BadgeDollarSign size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.purchases",
    items: [{ to: "/purchases", labelKey: "nav.purchases", icon: <ShoppingCart size={17} /> }]
  },
  {
    titleKey: "nav.group.hr",
    items: [{ to: "/payroll", labelKey: "nav.payroll", icon: <Banknote size={17} /> }]
  },
  {
    titleKey: "nav.group.reports",
    items: [
      { to: "/reports", labelKey: "nav.reports", icon: <FileBarChart size={17} /> },
      { to: "/reports/builder", labelKey: "nav.reportBuilder", icon: <FileBarChart size={17} /> },
      { to: "/audit-log", labelKey: "nav.auditLog", icon: <ScrollText size={17} /> },
      { to: "/period-closing", labelKey: "nav.periodClosing", icon: <ShieldCheck size={17} /> }
    ]
  },
  {
    titleKey: "nav.group.other",
    items: [
      { to: "/customer-portal", labelKey: "nav.customerPortal", icon: <Users size={17} /> },
      { to: "/users", labelKey: "nav.users", icon: <UserCog size={17} /> },
      { to: "/settings", labelKey: "nav.settings", icon: <Settings size={17} /> }
    ]
  }
];

// Small inline icon so the supplier entry does not need another lucide import name.
function HandshakeIcon() {
  return <Users size={17} />;
}

export default function Layout() {
  const navigate = useNavigate();
  const { t, lang, setLang } = useI18n();
  const storedUser = localStorage.getItem("dyehouse_user");
  const user = storedUser ? JSON.parse(storedUser) : null;

  const { data: settings } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  const logout = () => {
    localStorage.removeItem("dyehouse_token");
    localStorage.removeItem("dyehouse_user");
    navigate("/login");
  };

  return (
    <div className="flex h-screen w-full">
      <aside
        className="w-64 shrink-0 text-white flex flex-col"
        style={{ background: "linear-gradient(180deg, #1f2937 0%, #0f172a 100%)" }}
      >
        <div className="px-5 py-5 border-b border-white/10 flex items-center gap-3">
          {settings?.logoDataUrl ? (
            <img src={settings.logoDataUrl} alt="" className="w-10 h-10 rounded-lg object-contain bg-white/90 p-1" />
          ) : (
            <div className="w-10 h-10 rounded-lg bg-white/15 flex items-center justify-center">
              <Factory size={20} />
            </div>
          )}
          <div className="min-w-0">
            <div className="text-base font-bold leading-tight truncate">
              {lang === "ar" ? settings?.companyNameAr : settings?.companyNameEn || settings?.companyNameAr || "DyeHouse ERP"}
            </div>
            <div className="text-[11px] text-white/60 mt-0.5">{t("app.subtitle")}</div>
          </div>
        </div>

        {/* Language switcher (spec section 3) - Arabic RTL / English LTR */}
        <div className="px-4 py-3 border-b border-white/10 flex items-center gap-2">
          <Languages size={15} className="text-white/60" />
          <span className="text-[11px] text-white/50 me-auto">{t("app.language")}</span>
          <div className="flex rounded-lg overflow-hidden border border-white/15">
            <button
              onClick={() => setLang("ar")}
              className={`px-2 py-1 text-[11px] font-semibold transition-colors ${
                lang === "ar" ? "bg-white/20 text-white" : "text-white/60 hover:text-white"
              }`}
            >
              ع
            </button>
            <button
              onClick={() => setLang("en")}
              className={`px-2 py-1 text-[11px] font-semibold transition-colors ${
                lang === "en" ? "bg-white/20 text-white" : "text-white/60 hover:text-white"
              }`}
            >
              EN
            </button>
          </div>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-5 overflow-y-auto">
          {navGroups.map((group) => (
            <div key={group.titleKey}>
              <div className="px-3 text-[10px] font-semibold text-white/40 uppercase tracking-wide mb-1.5">
                {t(group.titleKey)}
              </div>
              <div className="space-y-0.5">
                {group.items.map((item) => (
                  <NavLink
                    key={`${item.to}-${item.labelKey}`}
                    to={item.to}
                    end={item.end}
                    className={({ isActive }) =>
                      `flex items-center gap-2.5 rounded-lg px-3 py-2 text-[13px] font-medium transition-colors ${
                        isActive ? "bg-white/15 text-white" : "text-white/75 hover:bg-white/10 hover:text-white"
                      }`
                    }
                  >
                    {item.icon}
                    {t(item.labelKey)}
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </nav>

        <div className="px-5 py-4 text-xs text-white/70 border-t border-white/10 flex items-center justify-between gap-2">
          <span className="truncate">{user?.displayName ?? user?.username ?? "-"}</span>
          <button onClick={logout} className="flex items-center gap-1 text-white/60 hover:text-white whitespace-nowrap">
            <LogOut size={14} />
            {t("app.logout")}
          </button>
        </div>
      </aside>

      <main className="flex-1 overflow-y-auto bg-slate-50">
        <div className="max-w-7xl mx-auto p-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
