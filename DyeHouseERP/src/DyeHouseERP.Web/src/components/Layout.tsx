import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { SettingsApi } from "@/api/client";
import {
  LayoutDashboard, Users, Package, Warehouse, Inbox, ArrowLeftRight, Repeat,
  ClipboardList, Layers3, GitBranch, FlaskConical, PackageCheck, Truck, Receipt,
  Wallet, FileBarChart, ScrollText, UserCog, Settings, LogOut, Factory, Send
} from "lucide-react";

const navGroups: { title: string; items: { to: string; label: string; icon: React.ReactNode; end?: boolean }[] }[] = [
  {
    title: "الرئيسية",
    items: [
      { to: "/", label: "لوحة التحكم", icon: <LayoutDashboard size={17} />, end: true },
      { to: "/production-floor", label: "شاشة أرضية المصنع", icon: <Factory size={17} /> }
    ]
  },
  {
    title: "البيانات الأساسية",
    items: [
      { to: "/customers", label: "العملاء", icon: <Users size={17} /> },
      { to: "/items", label: "الأصناف", icon: <Package size={17} /> },
      { to: "/warehouses", label: "المخازن", icon: <Warehouse size={17} /> },
      { to: "/production-stages", label: "مراحل التشغيل", icon: <Layers3 size={17} /> }
    ]
  },
  {
    title: "الخامات",
    items: [
      { to: "/raw-messages", label: "استلام الخام / الرسائل", icon: <Inbox size={17} /> },
      { to: "/raw-external-releases", label: "إفراج الخام الخارجي", icon: <Send size={17} /> },
      { to: "/customer-transfers", label: "تحويل الخام بين العملاء", icon: <ArrowLeftRight size={17} /> },
      { to: "/stock-adjustments", label: "تسويات المخزون", icon: <Repeat size={17} /> }
    ]
  },
  {
    title: "الإنتاج",
    items: [
      { to: "/production-orders", label: "أوامر التشغيل", icon: <ClipboardList size={17} /> },
      { to: "/separates", label: "المنفصلات وإعادة التشغيل", icon: <GitBranch size={17} /> },
      { to: "/materials", label: "المواد والكيماويات", icon: <FlaskConical size={17} /> },
      { to: "/ready-goods", label: "المخزون الجاهز", icon: <PackageCheck size={17} /> }
    ]
  },
  {
    title: "المالية",
    items: [
      { to: "/deliveries", label: "التسليمات", icon: <Truck size={17} /> },
      { to: "/invoices", label: "الفواتير وحساب العميل", icon: <Receipt size={17} /> },
      { to: "/treasury", label: "الخزينة", icon: <Wallet size={17} /> }
    ]
  },
  {
    title: "أخرى",
    items: [
      { to: "/customer-portal", label: "بوابة العملاء", icon: <Users size={17} /> },
      { to: "/reports", label: "التقارير", icon: <FileBarChart size={17} /> },
      { to: "/reports/builder", label: "منشئ التقارير المخصص", icon: <FileBarChart size={17} /> },
      { to: "/audit-log", label: "سجل التدقيق", icon: <ScrollText size={17} /> },
      { to: "/users", label: "المستخدمون", icon: <UserCog size={17} /> },
      { to: "/settings", label: "إعدادات الشركة", icon: <Settings size={17} /> },
      { to: "/period-closing", label: "إقفال الفترات", icon: <Settings size={17} /> }
    ]
  }
];

export default function Layout() {
  const navigate = useNavigate();
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
        style={{ background: "linear-gradient(180deg, #312e81 0%, #4c1d95 100%)" }}
      >
        <div className="px-5 py-6 border-b border-white/10 flex items-center gap-3">
          {settings?.logoDataUrl ? (
            <img src={settings.logoDataUrl} alt="" className="w-10 h-10 rounded-lg object-contain bg-white/90 p-1" />
          ) : (
            <div className="w-10 h-10 rounded-lg bg-white/15 flex items-center justify-center">
              <Factory size={20} />
            </div>
          )}
          <div>
            <div className="text-base font-bold leading-tight">{settings?.companyNameAr || "DyeHouse ERP"}</div>
            <div className="text-[11px] text-white/60 mt-0.5">نظام إدارة مصنع الصباغة</div>
          </div>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-5 overflow-y-auto">
          {navGroups.map((group) => (
            <div key={group.title}>
              <div className="px-3 text-[10px] font-semibold text-white/40 uppercase tracking-wide mb-1.5">{group.title}</div>
              <div className="space-y-0.5">
                {group.items.map((item) => (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    end={item.end}
                    className={({ isActive }) =>
                      `flex items-center gap-2.5 rounded-lg px-3 py-2 text-[13px] font-medium transition-colors ${
                        isActive ? "bg-white/15 text-white" : "text-white/75 hover:bg-white/10 hover:text-white"
                      }`
                    }
                  >
                    {item.icon}
                    {item.label}
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </nav>

        <div className="px-5 py-4 text-xs text-white/70 border-t border-white/10 flex items-center justify-between">
          <span className="truncate">{user?.displayName ?? user?.username ?? "-"}</span>
          <button onClick={logout} className="flex items-center gap-1 text-white/60 hover:text-white">
            <LogOut size={14} />
            خروج
          </button>
        </div>
      </aside>

      <main className="flex-1 overflow-y-auto bg-gray-50">
        <div className="max-w-6xl mx-auto p-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
