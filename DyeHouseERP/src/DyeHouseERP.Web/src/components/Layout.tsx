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
  FormInput, Ruler, BadgeDollarSign, LifeBuoy, Languages, ShieldCheck, PackageOpen,
  ShoppingCart, Banknote, Handshake, ClipboardCheck, Tags, Database,
  Menu, X, PanelLeftClose, PanelLeftOpen, Sun, Moon, Monitor,
  ChevronRight, ChevronLeft, ChevronDown, Search
} from "lucide-react";

type NavItem = { to: string; labelKey: string; icon: React.ReactNode; end?: boolean };
type NavGroup = { titleKey: string; icon: React.ReactNode; items: NavItem[] };

/**
 * Main navigation (spec section 4), plus the single Warehouse module required by
 * section 14: receipts, issues, returns, transfers, adjustments, external
 * processing, balances and reports all live inside /warehouse instead of each
 * having its own top-level sidebar entry. Their individual routes are kept
 * working so existing bookmarks and deep links never break.
 *
 * Each group carries its own icon now: it is the group row's leading glyph in
 * the sidebar (and the only thing shown when the rail is collapsed), so a group
 * is identifiable without reading its label.
 */
const navGroups: NavGroup[] = [
  {
    titleKey: "nav.group.main",
    icon: <LayoutDashboard size={18} />,
    items: [
      { to: "/", labelKey: "nav.dashboard", icon: <LayoutDashboard size={16} />, end: true },
      { to: "/approvals", labelKey: "nav.approvals", icon: <ClipboardCheck size={16} /> },
      { to: "/production-floor", labelKey: "nav.productionFloor", icon: <Factory size={16} /> }
    ]
  },
  {
    titleKey: "nav.group.master",
    icon: <Database size={18} />,
    items: [
      { to: "/customers", labelKey: "nav.customers", icon: <Users size={16} /> },
      { to: "/suppliers", labelKey: "nav.suppliers", icon: <Handshake size={16} /> },
      { to: "/items", labelKey: "nav.items", icon: <Package size={16} /> },
      { to: "/warehouses", labelKey: "nav.warehouses", icon: <Warehouse size={16} /> },
      { to: "/purchase-units", labelKey: "nav.purchaseUnits", icon: <Ruler size={16} /> },
      { to: "/production-stages", labelKey: "nav.productionStages", icon: <Layers3 size={16} /> }
    ]
  },
  {
    titleKey: "nav.group.warehouse",
    icon: <Warehouse size={18} />,
    items: [
      { to: "/warehouse", labelKey: "nav.warehouseHub", icon: <Warehouse size={16} /> },
      { to: "/warehouse?tab=ready", labelKey: "nav.readyGoods", icon: <PackageCheck size={16} /> },
      { to: "/warehouse?tab=materials", labelKey: "nav.materials", icon: <FlaskConical size={16} /> },
      { to: "/warehouse?tab=supplies", labelKey: "nav.operatingSupplies", icon: <LifeBuoy size={16} /> },
      { to: "/supplies", labelKey: "nav.supplies", icon: <LifeBuoy size={16} /> },
      // المنفصلات sits with the warehouses: a separate is MATERIAL HELD ON SITE, tracked by the
      // warehouse register (it is reprocessed into a new job or scrapped), not a production stage -
      // and the register is where the floor looks for the material it set aside.
      { to: "/separates", labelKey: "nav.separates", icon: <GitBranch size={16} /> },
      // Customer returns are a raw-material receipt (spec section 32), so they sit
      // with the warehouse module rather than with deliveries.
      { to: "/customer-returns", labelKey: "nav.customerReturns", icon: <PackageOpen size={16} /> }
    ]
  },
  {
    titleKey: "nav.group.formation",
    icon: <FormInput size={18} />,
    items: [
      { to: "/formation-requests", labelKey: "nav.formationRequests", icon: <FormInput size={16} /> },
      { to: "/formation-specifications", labelKey: "nav.formationSpecifications", icon: <Ruler size={16} /> }
    ]
  },
  {
    titleKey: "nav.group.production",
    icon: <Factory size={18} />,
    items: [
      { to: "/production-orders", labelKey: "nav.jobOrders", icon: <ClipboardList size={16} /> },
      { to: "/ready-goods", labelKey: "nav.readyGoods", icon: <PackageCheck size={16} /> }
    ]
  },

  {
    titleKey: "nav.group.finance",
    icon: <Wallet size={18} />,
    items: [
      { to: "/deliveries", labelKey: "nav.delivery", icon: <Truck size={16} /> },
      { to: "/invoices", labelKey: "nav.invoices", icon: <Receipt size={16} /> },
      { to: "/treasury", labelKey: "nav.treasury", icon: <Wallet size={16} /> },
      { to: "/checks", labelKey: "nav.checks", icon: <BadgeDollarSign size={16} /> },
      { to: "/material-sales", labelKey: "nav.materialSales", icon: <BadgeDollarSign size={16} /> },
      // The two commercial lists: what each stage costs US, and what the customer
      // is charged (spec sections 34 + 36).
      { to: "/price-lists", labelKey: "nav.priceLists", icon: <Tags size={16} /> }
    ]
  },
  {
    titleKey: "nav.group.purchases",
    icon: <ShoppingCart size={18} />,
    items: [{ to: "/purchases", labelKey: "nav.purchases", icon: <ShoppingCart size={16} /> }]
  },
  {
    titleKey: "nav.group.hr",
    icon: <Banknote size={18} />,
    items: [{ to: "/payroll", labelKey: "nav.payroll", icon: <Banknote size={16} /> }]
  },
  {
    titleKey: "nav.group.reports",
    icon: <FileBarChart size={18} />,
    items: [
      { to: "/reports", labelKey: "nav.reports", icon: <FileBarChart size={16} /> },
      { to: "/reports/builder", labelKey: "nav.reportBuilder", icon: <FileBarChart size={16} /> },
      { to: "/audit-log", labelKey: "nav.auditLog", icon: <ScrollText size={16} /> },
      { to: "/period-closing", labelKey: "nav.periodClosing", icon: <ShieldCheck size={16} /> }
    ]
  },
  {
    titleKey: "nav.group.other",
    icon: <Settings size={18} />,
    items: [
      { to: "/users", labelKey: "nav.users", icon: <UserCog size={16} /> },
      { to: "/settings", labelKey: "nav.settings", icon: <Settings size={16} /> }
    ]
  }
];

const COLLAPSE_KEY = "dyehouse_sidebar_collapsed";
/** Which groups the user has collapsed, as a small JSON map of titleKey -> true. */
const GROUPS_KEY = "dyehouse_sidebar_closed_groups";

const readClosedGroups = (): Record<string, boolean> => {
  try {
    const raw = localStorage.getItem(GROUPS_KEY);
    return raw ? (JSON.parse(raw) as Record<string, boolean>) : {};
  } catch {
    // A corrupt value must never break the shell - fall back to "all open".
    return {};
  }
};

/** Does the current location belong to this group? Used to highlight it. */
const groupHolds = (group: NavGroup, path: string) =>
  group.items.some((i) => (i.end ? path === i.to : path.startsWith(i.to.split("?")[0])));

export default function Layout() {
  const navigate = useNavigate();
  const location = useLocation();
  const { t, lang, setLang, isRtl } = useI18n();
  const { theme, setTheme } = useTheme();

  const storedUser = localStorage.getItem("dyehouse_user");
  const user = storedUser ? JSON.parse(storedUser) : null;

  // Desktop: expanded (272px) or collapsed (76px, icons only).
  // Mobile: off-canvas drawer that reserves no space in the flow.
  const [collapsed, setCollapsed] = useState<boolean>(() => localStorage.getItem(COLLAPSE_KEY) === "1");
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [closedGroups, setClosedGroups] = useState<Record<string, boolean>>(readClosedGroups);

  const { data: settings } = useQuery({ queryKey: ["company-settings"], queryFn: () => SettingsApi.get() });

  // Any navigation closes the mobile drawer, including query-string changes
  // such as ?tab=master that keep the same pathname.
  useEffect(() => {
    setDrawerOpen(false);
  }, [location.pathname, location.search]);

  // Navigating into a collapsed group re-opens it: a route change should never
  // leave the active item hidden behind a fold the user forgot about.
  useEffect(() => {
    setClosedGroups((prev) => {
      const active = navGroups.find((g) => groupHolds(g, location.pathname));
      if (!active || !prev[active.titleKey]) return prev;
      const next = { ...prev };
      delete next[active.titleKey];
      try {
        localStorage.setItem(GROUPS_KEY, JSON.stringify(next));
      } catch {
        // storage unavailable - the visual state still applies for this session
      }
      return next;
    });
  }, [location.pathname]);

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

  const toggleGroup = (titleKey: string) => {
    setClosedGroups((prev) => {
      const next = { ...prev };
      if (next[titleKey]) delete next[titleKey];
      else next[titleKey] = true;
      try {
        localStorage.setItem(GROUPS_KEY, JSON.stringify(next));
      } catch {
        // storage unavailable - the visual state still applies for this session
      }
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

  /* ---------------------------------------------------------------- nav rail */
  const nav = (
    <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-3" aria-label={t("app.menu", "Menu")}>
      {navGroups.map((group) => {
        const active = groupHolds(group, location.pathname);

        /* Collapsed rail: one icon per group, with the group's first item as the
           destination. Tooltips carry the label so the destination is never a
           guess. The separators keep the groups readable at icon width. */
        if (collapsed) {
          const first = group.items[0];
          return (
            <div key={group.titleKey} className="pb-1">
              <NavLink
                to={first.to}
                end={first.end}
                title={t(group.titleKey)}
                className={({ isActive }) =>
                  `nav-link justify-center px-0 ${isActive || active ? "nav-link-active" : ""}`
                }
              >
                <span className="shrink-0">{group.icon}</span>
                <span className="sr-only">{t(group.titleKey)}</span>
              </NavLink>
            </div>
          );
        }

        const isClosed = !!closedGroups[group.titleKey];

        return (
          <div key={group.titleKey}>
            <button
              type="button"
              onClick={() => toggleGroup(group.titleKey)}
              className={`nav-group-toggle ${active ? "text-brand-700 dark:text-brand-200" : ""}`}
              aria-expanded={!isClosed}
              aria-controls={`nav-group-${group.titleKey}`}
            >
              <span className="shrink-0">{group.icon}</span>
              <span className="min-w-0 flex-1 truncate text-start">{t(group.titleKey)}</span>
              <ChevronDown
                size={16}
                aria-hidden="true"
                className={`shrink-0 text-ink-subtle transition-transform duration-200 ${isClosed ? "" : "rotate-180"}`}
              />
            </button>

            {!isClosed && (
              <div id={`nav-group-${group.titleKey}`} className="relative mt-0.5 space-y-0.5">
                <span className={`nav-sub-rail ${active ? "nav-sub-rail-active" : ""}`} aria-hidden="true" />
                {group.items.map((item) => (
                  <NavLink
                    key={`${item.to}-${item.labelKey}`}
                    to={item.to}
                    end={item.end}
                    className={({ isActive }) => `nav-sub-link ${isActive ? "nav-sub-link-active" : ""}`}
                  >
                    <span className="shrink-0">{item.icon}</span>
                    <span className="truncate">{t(item.labelKey)}</span>
                  </NavLink>
                ))}
              </div>
            )}
          </div>
        );
      })}
    </nav>
  );

  /* --------------------------------------------------------- profile + foot */
  const displayName = user?.displayName ?? user?.username ?? "-";
  const initials = String(displayName)
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part: string) => part[0])
    .join("")
    .toUpperCase();

  const sidebarInner = (
    <>
      <div className={`flex items-center gap-3 border-b border-line px-4 ${collapsed ? "py-4" : "py-4"}`}>
        {settings?.logoDataUrl ? (
          <img
            src={settings.logoDataUrl}
            alt=""
            className="h-10 w-10 shrink-0 rounded-xl border border-line bg-surface object-contain p-1"
          />
        ) : (
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-600 shadow-primary on-brand">
            <Factory size={20} />
          </div>
        )}
        {!collapsed && (
          <div className="min-w-0 animate-fade-in">
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

      {/* ---------- language + user profile, pinned to the bottom ---------- */}
      <div className="border-t border-line p-3">
        {!collapsed ? (
          <div className="rounded-2xl bg-surface-sunken p-2.5">
            <div className="flex items-center gap-2.5">
              <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-600 text-xs font-bold on-brand">
                {initials || <UserCog size={16} />}
              </span>
              <div className="min-w-0 flex-1">
                <div className="truncate text-xs font-semibold text-ink">{displayName}</div>
                <div className="truncate text-2xs text-ink-subtle">@{user?.username ?? "-"}</div>
              </div>
              <button
                onClick={logout}
                title={t("app.logout")}
                aria-label={t("app.logout")}
                className="btn btn-ghost btn-sm shrink-0"
              >
                <LogOut size={16} />
              </button>
            </div>

            <div className="mt-2.5 flex items-center gap-2 border-t border-line pt-2.5">
              <Languages size={14} className="shrink-0 text-ink-subtle" aria-hidden="true" />
              <span className="text-2xs font-medium text-ink-subtle">{t("app.language")}</span>
              <div className="ms-auto flex overflow-hidden rounded-lg border border-line bg-surface">
                <button
                  onClick={() => setLang("ar")}
                  className={`px-2 py-1 text-2xs font-semibold transition-colors ${
                    lang === "ar" ? "bg-brand-600 on-brand" : "text-ink-muted hover:bg-surface-sunken"
                  }`}
                  aria-pressed={lang === "ar"}
                  aria-label={t("app.arabic", "Arabic")}
                >
                  ع
                </button>
                <button
                  onClick={() => setLang("en")}
                  className={`px-2 py-1 text-2xs font-semibold transition-colors ${
                    lang === "en" ? "bg-brand-600 on-brand" : "text-ink-muted hover:bg-surface-sunken"
                  }`}
                  aria-pressed={lang === "en"}
                  aria-label={t("app.english", "English")}
                >
                  EN
                </button>
              </div>
            </div>
          </div>
        ) : (
          <div className="flex flex-col items-center gap-1">
            <button
              onClick={logout}
              title={t("app.logout")}
              aria-label={t("app.logout")}
              className="btn btn-ghost btn-sm"
            >
              <LogOut size={16} />
            </button>
          </div>
        )}
      </div>
    </>
  );

  return (
    <div className="flex h-screen w-full overflow-hidden bg-surface-sunken">
      {/* ---------- Desktop sidebar (persistent) ---------- */}
      <aside
        className={`no-print hidden shrink-0 flex-col border-e border-line bg-surface transition-[width] duration-200 lg:flex ${
          collapsed ? "w-[76px]" : "w-[272px]"
        }`}
      >
        {sidebarInner}
      </aside>

      {/* ---------- Mobile drawer ---------- */}
      {drawerOpen && (
        <div className="no-print fixed inset-0 z-50 lg:hidden">
          {/* The scrim is a literal colour: the `black` name is remapped onto
              --ink, which would turn this overlay light in dark mode. */}
          <button
            className="absolute inset-0 h-full w-full animate-fade-in bg-[#0f172a]/60"
            onClick={() => setDrawerOpen(false)}
            aria-label={t("app.close", "Close")}
          />
          <aside
            className="absolute inset-y-0 start-0 flex w-[272px] animate-drawer-in flex-col border-e border-line bg-surface shadow-pop"
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
        <header className="no-print z-20 flex h-16 shrink-0 items-center gap-3 border-b border-line bg-surface px-4 sm:px-6">
          <button
            onClick={() => setDrawerOpen(true)}
            className="btn btn-ghost btn-sm lg:hidden"
            aria-label={t("app.menu", "Menu")}
          >
            <Menu size={20} />
          </button>

          {/* Rail toggle (desktop only): lives in the top bar so the sidebar's
              own footer belongs entirely to the user profile. */}
          <button
            onClick={toggleCollapsed}
            className="btn btn-ghost btn-sm hidden lg:inline-flex"
            aria-label={collapsed ? t("app.expandMenu", "Expand menu") : t("app.collapseMenu", "Collapse menu")}
            title={collapsed ? t("app.expandMenu", "Expand menu") : t("app.collapseMenu", "Collapse menu")}
          >
            <CollapseIcon size={18} />
          </button>

          <nav aria-label="breadcrumb" className="min-w-0 flex-1">
            <ol className="flex min-w-0 items-center gap-1.5 text-sm">
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

          <div className="flex shrink-0 items-center gap-2">
            {/* Search is a jump-to-module control: it filters the existing nav
                rather than querying the API, so it adds no backend surface. */}
            <div className="hidden sm:block">
              <NavSearch groups={navGroups} t={t} isRtl={isRtl} />
            </div>
            <div className="sm:hidden">
              <NavSearch groups={navGroups} t={t} isRtl={isRtl} compact />
            </div>
            <ThemeToggle theme={theme} setTheme={setTheme} t={t} />
          </div>
        </header>

        <main className="min-w-0 flex-1 overflow-y-auto overflow-x-hidden">
          <div className="page p-4 sm:p-6 lg:p-8">
            {/* One page-transition point for all 42 pages. Keying on the
                pathname remounts the subtree on a real navigation, which
                replays the entrance; a query-only change (?tab=...) keeps the
                same pathname and correctly does NOT replay it. */}
            <div key={location.pathname} className="animate-page-enter">
              <Outlet />
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ search */
function NavSearch({
  groups,
  t,
  isRtl,
  compact = false
}: {
  groups: NavGroup[];
  t: (k: string, f?: string) => string;
  isRtl: boolean;
  /** Phone width: a single icon button instead of the full field. */
  compact?: boolean;
}) {
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

  const go = (to: string) => {
    navigate(to);
    setOpen(false);
    setQuery("");
  };

  return (
    <div className="relative">
      {compact ? (
        <button
          onClick={() => setOpen((v) => !v)}
          className="btn btn-ghost btn-sm"
          aria-label={t("app.search", "Search")}
          title={t("app.search", "Search")}
        >
          <Search size={18} />
        </button>
      ) : (
        /* The field is the trigger: focusing or typing opens the result panel,
           so a module lookup costs one click instead of two. */
        <div className="search-field w-52 lg:w-72">
          <Search size={16} aria-hidden="true" className="search-icon" />
          <input
            type="search"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setOpen(true);
            }}
            onFocus={() => setOpen(true)}
            placeholder={t("app.searchPlaceholder", "Search modules")}
            className="input"
            aria-label={t("app.search", "Search")}
          />
        </div>
      )}

      {open && (
        <>
          <button className="fixed inset-0 z-30 cursor-default" onClick={() => setOpen(false)} aria-hidden="true" />
          {/* Direction-aware dropdown: fade + slight scale from the corner it
              is anchored to, so it opens "out of" the trigger in both RTL
              (top-left) and LTR (top-right) without a hardcoded side. */}
          <div
            className="animate-pop-in absolute end-0 z-40 mt-2 w-72 rounded-2xl border border-line bg-surface p-2 shadow-pop"
            style={{ transformOrigin: isRtl ? "top left" : "top right" }}
          >
            {compact && (
              <input
                autoFocus
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder={t("app.searchPlaceholder", "Search modules")}
                className="input"
                aria-label={t("app.search", "Search")}
              />
            )}
            <ul className={`max-h-72 overflow-y-auto ${compact ? "mt-2" : ""}`}>
              {results.length === 0 && (
                <li className="px-2 py-3 text-center text-xs text-ink-subtle">
                  {t("app.noResults", "No results")}
                </li>
              )}
              {results.map(({ item, group }) => (
                <li key={`${item.to}-${item.labelKey}`}>
                  <button
                    onClick={() => go(item.to)}
                    className="flex w-full items-center gap-2 rounded-xl px-2 py-2 text-start text-sm text-ink hover:bg-surface-sunken"
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
    <div className="flex items-center gap-0.5 rounded-xl bg-surface-sunken p-0.5">
      {options.map((o) => (
        <button
          key={o.value}
          onClick={() => setTheme(o.value)}
          className={`flex h-8 w-8 items-center justify-center rounded-lg transition-colors ${
            theme === o.value
              ? "bg-surface text-brand-700 shadow-sm"
              : "text-ink-subtle hover:text-ink"
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
