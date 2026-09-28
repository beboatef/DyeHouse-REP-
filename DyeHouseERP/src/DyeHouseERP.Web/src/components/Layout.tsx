import { useEffect, useMemo, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { SettingsApi } from "@/api/client";
import { useI18n } from "@/i18n";
import { useTheme, type Theme } from "@/components/ThemeProvider";
import {
  LayoutDashboard, Users, Package, Warehouse,
  ClipboardList, Layers3, GitBranch, FlaskConical, PackageCheck, Truck, Receipt,
  Wallet, FileBarChart, ScrollText, UserCog, Settings, LogOut, Factory,
  FormInput, Ruler, BadgeDollarSign, LifeBuoy, Languages, ShieldCheck,
  ShoppingCart, Banknote, Handshake, ClipboardCheck,
  Menu, X, PanelLeftClose, PanelLeftOpen, Sun, Moon, Monitor,
  ChevronRight, ChevronLeft, Search
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
      { to: "/", labelKey: "nav.dashboard", icon: <LayoutDashboard size={18} />, end: true },
      { to: "/approvals", labelKey: "nav.approvals", icon: <ClipboardCheck size={18} /> },
      { to: "/production-floor", labelKey: "nav.productionFloor", icon: <Factory size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.master",
    items: [
      { to: "/customers", labelKey: "nav.customers", icon: <Users size={18} /> },
      { to: "/suppliers", labelKey: "nav.suppliers", icon: <Handshake size={18} /> },
      { to: "/items", labelKey: "nav.items", icon: <Package size={18} /> },
      { to: "/warehouses", labelKey: "nav.warehouses", icon: <Warehouse size={18} /> },
      { to: "/production-stages", labelKey: "nav.productionStages", icon: <Layers3 size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.warehouse",
    items: [{ to: "/warehouse", labelKey: "nav.warehouseHub", icon: <Warehouse size={18} /> }]
  },
  {
    titleKey: "nav.group.formation",
    items: [
      { to: "/formation-requests", labelKey: "nav.formationRequests", icon: <FormInput size={18} /> },
      { to: "/formation-specifications", labelKey: "nav.formationSpecifications", icon: <Ruler size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.production",
    items: [
      { to: "/production-orders", labelKey: "nav.jobOrders", icon: <ClipboardList size={18} /> },
      { to: "/separates", labelKey: "nav.production", icon: <GitBranch size={18} /> },
      { to: "/ready-goods", labelKey: "nav.readyGoods", icon: <PackageCheck size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.materials",
    items: [
      { to: "/materials", labelKey: "nav.materials", icon: <FlaskConical size={18} /> },
      { to: "/supplies", labelKey: "nav.supplies", icon: <LifeBuoy size={18} /> },
      { to: "/material-sales", labelKey: "nav.materialSales", icon: <BadgeDollarSign size={18} /> },
      { to: "/warehouse?tab=master", labelKey: "nav.operatingSupplies", icon: <Warehouse size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.finance",
    items: [
      { to: "/deliveries", labelKey: "nav.delivery", icon: <Truck size={18} /> },
      { to: "/invoices", labelKey: "nav.invoices", icon: <Receipt size={18} /> },
      { to: "/treasury", labelKey: "nav.treasury", icon: <Wallet size={18} /> },
      { to: "/checks", labelKey: "nav.checks", icon: <BadgeDollarSign size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.purchases",
    items: [{ to: "/purchases", labelKey: "nav.purchases", icon: <ShoppingCart size={18} /> }]
  },
  {
    titleKey: "nav.group.hr",
    items: [{ to: "/payroll", labelKey: "nav.payroll", icon: <Banknote size={18} /> }]
  },
  {
    titleKey: "nav.group.reports",
    items: [
      { to: "/reports", labelKey: "nav.reports", icon: <FileBarChart size={18} /> },
      { to: "/reports/builder", labelKey: "nav.reportBuilder", icon: <FileBarChart size={18} /> },
      { to: "/audit-log", labelKey: "nav.auditLog", icon: <ScrollText size={18} /> },
      { to: "/period-closing", labelKey: "nav.periodClosing", icon: <ShieldCheck size={18} /> }
    ]
  },
  {
    titleKey: "nav.group.other",
    items: [
      { to: "/customer-portal", labelKey: "nav.customerPortal", icon: <Users size={18} /> },
      { to: "/users", labelKey: "nav.users", icon: <UserCog size={18} /> },
      { to: "/settings", labelKey: "nav.settings", icon: <Settings size={18} /> }
    ]
  }
];

const COLLAPSE_KEY = "dyehouse_sidebar_collapsed";

export default function Layout() {
  const navigate = useNavigate();
  const location = useLocation();
  const { t, lang, setLang, isRtl } = useI18n();
  const { theme, setTheme } = useTheme();

  const storedUser = localStorage.getItem("dyehouse_user");
  const user = storedUser ? JSON.parse(storedUser) : null;

  // Desktop: expanded (256px) or collapsed (72px, icons only).
  // Mobile: off-canvas drawer that reserves no space in the flow.
  const [collapsed, setCollapsed] = useState<boolean>(() => localStorage.getItem(COLLAPSE_KEY) === "1");
  const [drawerOpen, setDrawerOpen] = useState(false);

  const { data: settings } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  // Any navigation closes the mobile drawer, including query-string changes
  // such as ?tab=master that keep the same pathname.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname, location.search]);

  // Lock body scroll while the drawer is open so the page behind cannot move.
  useEffect(() => {
    document.body.style.overflow = drawerOpen ? "hidden" : "";
    return () => {
      document.body.style.overflow = "";
    };
  }, [drawerOpen]);

  useEffect(() => {
    // Escape closes the drawer - expected of any overlay.
    if (!drawerOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setDrawerOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [drawerOpen]);

  const toggleCollapsed = () => {
    setCollapsed((prev) => {
      const next = !prev;
      localStorage.setItem(COLLAPSE_KEY, next ? "1" : "0");
      return next;
    });
  };

  const logout = () => {
    localStorage.removeItem("dyehouse_token");
    localStorage.removeItem("dyehouse_user");
    navigate("/login");
  };

  const companyName =
    (lang === "ar" ? settings?.companyNameAr : settings?.companyNameEn) ||
    settings?.companyNameAr ||
    "DyeHouse ERP";

  // Breadcrumb built from the active nav entry, so the top bar always names
  // where the user is without every page having to supply it.
  const breadcrumb = useMemo(() => {
    const path = location.pathname;
    const match = navGroups
      .flatMap((g) => g.items)
      .find((i) => (i.end ? path === i.to : path.startsWith(i.to.split("?")[0])));
    if (match) return { group: navGroups.find((g) => g.items.includes(match))?.titleKey, item: match.labelKey };
    return null;
  }, [location.pathname]);

  const Chevron = isRtl ? ChevronLeft : ChevronRight;
  const CollapseIcon = collapsed ? PanelLeftOpen : PanelLeftClose;

  const nav = (
    <nav className="flex-1 space-y-5 overflow-y-auto px-3 py-4" aria-label={t("app.menu", "Menu")}>
      {navGroups.map((group) => (
        <div key={group.titleKey}>
          {!collapsed && (
            <div className="mb-1.5 px-3 text-2xs font-semibold uppercase tracking-wide text-ink-subtle">
              {t(group.titleKey)}
            </div>
          )}
          {collapsed && <div className="mx-2 mb-2 border-t border-line" />}
          <div className="space-y-0.5">
            {group.items.map((item) => (
              <NavLink
                key={`${item.to}-${item.labelKey}`}
                to={item.to}
                end={item.end}
                title={collapsed ? t(item.labelKey) : undefined}
                className={({ isActive }) =>
                  `nav-link ${isActive ? "nav-link-active" : ""} ${collapsed ? "justify-center px-0" : ""}`
                }
              >
                <span className="shrink-0">{item.icon}</span>
                {!collapsed && <span className="truncate">{t(item.labelKey)}</span>}
              </NavLink>
            ))}
          </div>
        </div>
      ))}
    </nav>
  );

  const sidebarInner = (
    <>
      <div className="flex items-center gap-3 border-b border-line px-4 py-4">
        {settings?.logoDataUrl ? (
          <img src={settings.logoDataUrl} alt="" className="h-10 w-10 shrink-0 rounded-lg bg-white object-contain p-1" />
        ) : (
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-brand-600 text-white">
            <Factory size={20} />
          </div>
        )}
        {!collapsed && (
          <div className="min-w-0">
            <div className="truncate text-sm font-bold leading-tight text-ink">{companyName}</div>
            <div className="mt-0.5 truncate text-2xs text-ink-subtle">{t("app.subtitle")}</div>
          </div>
        )}
        <button
          onClick={() => setDrawerOpen(false)}
          className="btn btn-ghost btn-sm ms-auto lg:hidden"
          aria-label={t("app.close", "Close")}
        >
          <X size={18} />
        </button>
      </div>

      {nav}

      <div className="border-t border-line px-3 py-3">
        {!collapsed && (
          <div className="mb-2 flex items-center gap-2 px-2">
            <Languages size={15} className="shrink-0 text-ink-subtle" />
            <span className="text-2xs text-ink-subtle">{t("app.language")}</span>
            <div className="ms-auto flex overflow-hidden rounded-lg border border-line-strong">
              <button
                onClick={() => setLang("ar")}
                className={`px-2 py-1 text-2xs font-semibold transition-colors ${
                  lang === "ar" ? "bg-brand-600 text-white" : "text-ink-muted hover:bg-surface-sunken"
                }`}
              >
                ع
              </button>
              <button
                onClick={() => setLang("en")}
                className={`px-2 py-1 text-2xs font-semibold transition-colors ${
                  lang === "en" ? "bg-brand-600 text-white" : "text-ink-muted hover:bg-surface-sunken"
                }`}
              >
                EN
              </button>
            </div>
          </div>
        )}
        <div className={`flex items-center gap-2 ${collapsed ? "justify-center" : "justify-between px-2"}`}>
          {!collapsed && <span className="truncate text-xs text-ink-muted">{user?.displayName ?? user?.username ?? "-"}</span>}
          <button
            onClick={logout}
            title={t("app.logout")}
            aria-label={t("app.logout")}
            className="btn btn-ghost btn-sm"
          >
            <LogOut size={16} />
            {!collapsed && <span>{t("app.logout")}</span>}
          </button>
        </div>
      </div>
    </>
  );

  return (
    <div className="flex h-screen w-full overflow-hidden bg-surface-sunken">
      {/* ---------- Desktop sidebar (persistent) ---------- */}
      <aside
        className={`no-print hidden shrink-0 border-e border-line bg-surface transition-[width] duration-200 lg:flex lg:flex-col ${
          collapsed ? "w-[72px]" : "w-64"
        }`}
      >
        {sidebarInner}
        <button
          onClick={toggleCollapsed}
          className="btn btn-ghost btn-sm absolute bottom-4 hidden lg:inline-flex"
          style={{ insetInlineEnd: collapsed ? "0.75rem" : undefined }}
          aria-label={collapsed ? t("app.expandMenu", "Expand menu") : t("app.collapseMenu", "Collapse menu")}
          title={collapsed ? t("app.expandMenu", "Expand menu") : t("app.collapseMenu", "Collapse menu")}
        >
          <CollapseIcon size={16} />
        </button>
      </aside>

      {/* ---------- Mobile drawer ---------- */}
      {drawerOpen && (
        <div className="no-print fixed inset-0 z-50 lg:hidden">
          <button
            className="absolute inset-0 h-full w-full animate-fade-in bg-black/50"
            onClick={() => setDrawerOpen(false)}
            aria-label={t("app.close", "Close")}
          />
          <aside
            className="absolute inset-y-0 start-0 flex w-72 animate-drawer-in flex-col border-e border-line bg-surface shadow-pop"
            style={{ ["--slide-from" as string]: isRtl ? "100%" : "-100%" }}
            role="dialog"
            aria-modal="true"
            aria-label={t("app.menu", "Menu")}
          >
            {sidebarInner}
          </aside>
        </div>
      )}

      {/* ---------- Main column ---------- */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="no-print z-20 flex h-14 shrink-0 items-center gap-3 border-b border-line bg-surface px-4">
          <button
            onClick={() => setDrawerOpen(true)}
            className="btn btn-ghost btn-sm lg:hidden"
            aria-label={t("app.menu", "Menu")}
          >
            <Menu size={20} />
          </button>

          <nav aria-label="breadcrumb" className="min-w-0 flex-1">
            <ol className="flex min-w-0 items-center gap-1 text-sm">
              {breadcrumb ? (
                <>
                  <li className="hidden shrink-0 text-ink-subtle sm:block">{t(breadcrumb.group!)}</li>
                  <li className="hidden shrink-0 text-ink-subtle sm:block">
                    <Chevron size={14} />
                  </li>
                  <li className="truncate font-semibold text-ink">{t(breadcrumb.item)}</li>
                </>
              ) : (
                <li className="truncate font-semibold text-ink">{t("nav.dashboard")}</li>
              )}
            </ol>
          </nav>

          <div className="flex shrink-0 items-center gap-1">
            {/* Search is a jump-to-module control: it filters the existing nav
                rather than querying the API, so it adds no backend surface. */}
            <NavSearch groups={navGroups} t={t} />
            <ThemeToggle theme={theme} setTheme={setTheme} t={t} />
          </div>
        </header>

        <main className="min-w-0 flex-1 overflow-y-auto overflow-x-hidden">
          <div className="page p-4 sm:p-6 lg:p-8">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ search */
function NavSearch({ groups, t }: { groups: NavGroup[]; t: (k: string, f?: string) => string }) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const navigate = useNavigate();

  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return [];
    return groups
      .flatMap((g) => g.items.map((i) => ({ item: i, group: g.titleKey })))
      .filter(({ item }) => t(item.labelKey).toLowerCase().includes(q) || item.to.toLowerCase().includes(q))
      .slice(0, 8);
  }, [query, groups, t]);

  return (
    <div className="relative">
      <button
        onClick={() => setOpen((v) => !v)}
        className="btn btn-ghost btn-sm"
        aria-label={t("app.search", "Search")}
        title={t("app.search", "Search")}
      >
        <Search size={18} />
      </button>
      {open && (
        <>
          <button className="fixed inset-0 z-30 cursor-default" onClick={() => setOpen(false)} aria-hidden="true" />
          <div className="absolute end-0 z-40 mt-2 w-72 rounded-lg border border-line bg-surface p-2 shadow-pop">
            <input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t("app.searchPlaceholder", "Search modules")}
              className="input"
              aria-label={t("app.search", "Search")}
            />
            <ul className="mt-2 max-h-72 overflow-y-auto">
              {results.length === 0 && (
                <li className="px-2 py-3 text-center text-xs text-ink-subtle">
                  {t("app.noResults", "No results")}
                </li>
              )}
              {results.map(({ item, group }) => (
                <li key={`${item.to}-${item.labelKey}`}>
                  <button
                    onClick={() => {
                      navigate(item.to);
                      setOpen(false);
                      setQuery("");
                    }}
                    className="flex w-full items-center gap-2 rounded-md px-2 py-2 text-start text-sm text-ink hover:bg-surface-sunken"
                  >
                    <span className="shrink-0 text-ink-subtle">{item.icon}</span>
                    <span className="min-w-0 flex-1 truncate">{t(item.labelKey)}</span>
                    <span className="shrink-0 text-2xs text-ink-subtle">{t(group)}</span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        </>
      )}
    </div>
  );
}

/* -------------------------------------------------------------- theme toggle */
function ThemeToggle({
  theme,
  setTheme,
  t
}: {
  theme: Theme;
  setTheme: (t: Theme) => void;
  t: (k: string, f?: string) => string;
}) {
  const options: { value: Theme; icon: React.ReactNode; label: string }[] = [
    { value: "light", icon: <Sun size={16} />, label: t("theme.light", "Light") },
    { value: "dark", icon: <Moon size={16} />, label: t("theme.dark", "Dark") },
    { value: "system", icon: <Monitor size={16} />, label: t("theme.system", "System") }
  ];
  const current = options.find((o) => o.value === theme)!;

  return (
    <div className="flex items-center overflow-hidden rounded-lg border border-line-strong">
      {options.map((o) => (
        <button
          key={o.value}
          onClick={() => setTheme(o.value)}
          className={`flex h-8 w-8 items-center justify-center transition-colors ${
            theme === o.value ? "bg-brand-600 text-white" : "text-ink-muted hover:bg-surface-sunken"
          }`}
          aria-label={o.label}
          aria-pressed={theme === o.value}
          title={o.label}
        >
          {o.icon}
        </button>
      ))}
      <span className="sr-only">{current.label}</span>
    </div>
  );
}
