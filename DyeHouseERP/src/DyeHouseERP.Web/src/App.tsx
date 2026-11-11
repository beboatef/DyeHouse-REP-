import { Suspense, lazy } from "react";
import { Routes, Route, useLocation } from "react-router-dom";
import Layout from "@/components/Layout";
import RequireAuth from "@/components/RequireAuth";
import RouteFallback from "@/components/RouteFallback";
import ErrorBoundary from "@/components/ErrorBoundary";
import ServiceWorkerUpdatePrompt from "@/components/ServiceWorkerUpdatePrompt";

/*
 * Route-level code splitting. Every page is its own chunk, fetched when the
 * route is first visited, instead of one entry bundle containing all 50
 * screens. The shell (Layout, RequireAuth, the router and the providers in
 * main.tsx) stays eager so navigation keeps its instant frame, and Rollup
 * still hoists what two pages share into a common chunk rather than copying
 * it into both.
 *
 * The print and scan views are lazy for the same reason: they are reached by
 * deep link (a printed sheet, a QR scan), so a shop-floor phone should not
 * download them to open the dashboard.
 */
const LoginPage = lazy(() => import("@/pages/LoginPage"));
const ChangePasswordPage = lazy(() => import("@/pages/ChangePasswordPage"));
const DashboardPage = lazy(() => import("@/pages/DashboardPage"));
const CustomersPage = lazy(() => import("@/pages/CustomersPage"));
const SuppliersPage = lazy(() => import("@/pages/SuppliersPage"));
const ItemsPage = lazy(() => import("@/pages/ItemsPage"));
const WarehousesPage = lazy(() => import("@/pages/WarehousesPage"));
const WarehouseHubPage = lazy(() => import("@/pages/WarehouseHubPage"));
const RawMessagesPage = lazy(() => import("@/pages/RawMessagesPage"));
const ProductionStagesPage = lazy(() => import("@/pages/ProductionStagesPage"));
const ProductionOrdersPage = lazy(() => import("@/pages/ProductionOrdersPage"));
const ProductionOrderDetailPage = lazy(() => import("@/pages/ProductionOrderDetailPage"));
const RawExternalReleasesPage = lazy(() => import("@/pages/RawExternalReleasesPage"));
const CustomerTransfersPage = lazy(() => import("@/pages/CustomerTransfersPage"));
const StockAdjustmentsPage = lazy(() => import("@/pages/StockAdjustmentsPage"));
const SeparatesPage = lazy(() => import("@/pages/SeparatesPage"));
const MaterialsPage = lazy(() => import("@/pages/MaterialsPage"));
const ReadyGoodsPage = lazy(() => import("@/pages/ReadyGoodsPage"));
const DeliveriesPage = lazy(() => import("@/pages/DeliveriesPage"));
const InvoicesPage = lazy(() => import("@/pages/InvoicesPage"));
const TreasuryPage = lazy(() => import("@/pages/TreasuryPage"));
const ChecksPage = lazy(() => import("@/pages/ChecksPage"));
const FormationRequestsPage = lazy(() => import("@/pages/FormationRequestsPage"));
const FormationRequestDetailPage = lazy(() => import("@/pages/FormationRequestDetailPage"));
const FormationSpecificationsPage = lazy(() => import("@/pages/FormationSpecificationsPage"));
const ProductionFloorPage = lazy(() => import("@/pages/ProductionFloorPage"));
const FloorMobilePage = lazy(() => import("@/pages/FloorMobilePage"));
const UsersPage = lazy(() => import("@/pages/UsersPage"));
const ReportsPage = lazy(() => import("@/pages/ReportsPage"));
const AuditLogPage = lazy(() => import("@/pages/AuditLogPage"));
const SettingsPage = lazy(() => import("@/pages/SettingsPage"));
const InvoicePrintPage = lazy(() => import("@/pages/print/InvoicePrintPage"));
const DeliveryPrintPage = lazy(() => import("@/pages/print/DeliveryPrintPage"));
const DeliveryStatementPrintPage = lazy(() => import("@/pages/print/DeliveryStatementPrintPage"));
const ProductionOrderPrintPage = lazy(() => import("@/pages/print/ProductionOrderPrintPage"));
const RawMessagePrintPage = lazy(() => import("@/pages/print/RawMessagePrintPage"));
const StockTraceabilityPrintPage = lazy(() => import("@/pages/print/StockTraceabilityPrintPage"));
const ScanViewPage = lazy(() => import("@/pages/scan/ScanViewPage"));
const ReportBuilderPage = lazy(() => import("@/pages/ReportBuilderPage"));
const PeriodClosingPage = lazy(() => import("@/pages/PeriodClosingPage"));
const NotFoundPage = lazy(() => import("@/pages/NotFoundPage"));
const PurchasesPage = lazy(() => import("@/pages/PurchasesPage"));
const PayrollPage = lazy(() => import("@/pages/PayrollPage"));
const HrPage = lazy(() => import("@/pages/HrPage"));
const ApprovalCenterPage = lazy(() => import("@/pages/ApprovalCenterPage"));
const SuppliesPage = lazy(() => import("@/pages/SuppliesPage"));
const MaterialSalesPage = lazy(() => import("@/pages/MaterialSalesPage"));
const PurchaseUnitsPage = lazy(() => import("@/pages/PurchaseUnitsPage"));
const CustomerReturnsPage = lazy(() => import("@/pages/CustomerReturnsPage"));
const PriceListsPage = lazy(() => import("@/pages/PriceListsPage"));

export default function App() {
  // A navigation is the user's own retry: change the key and the boundary clears itself.
  const location = useLocation();

  return (
    /* Outer boundary: covers the standalone routes (/login, /print/*, /scan/*,
       and the catch-all) plus the shell's own first paint, so a page that throws
       while rendering never leaves a white screen. Pages inside the
       authenticated group also hit the closer boundary in Layout, so there the
       sidebar and header survive the crash and only the page area is replaced. */
    <ErrorBoundary resetKey={location.key}>
      <Suspense fallback={<RouteFallback />}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth />}>
          {/* Part 2: outside Layout (no nav/sidebar) so a flagged session cannot reach any
              business screen from here; the API gate enforces the same server-side. */}
          <Route path="/change-password" element={<ChangePasswordPage />} />
          <Route element={<Layout />}>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/suppliers" element={<SuppliersPage />} />
          <Route path="/items" element={<ItemsPage />} />
          <Route path="/warehouses" element={<WarehousesPage />} />
          {/* One Warehouse module (spec section 14): a DROPDOWN chooses the warehouse, and every
              balance, movement, item card and report on the page is scoped to it. The id in the URL
              makes the chosen warehouse shareable and refresh-safe; the individual document routes
              below stay working for the screens that create and print the documents themselves. */}
          <Route path="/warehouse" element={<WarehouseHubPage />} />
          <Route path="/warehouse/:warehouseId" element={<WarehouseHubPage />} />
          <Route path="/raw-messages" element={<RawMessagesPage />} />
          <Route path="/raw-external-releases" element={<RawExternalReleasesPage />} />
          <Route path="/customer-transfers" element={<CustomerTransfersPage />} />
          <Route path="/stock-adjustments" element={<StockAdjustmentsPage />} />
          <Route path="/customer-returns" element={<CustomerReturnsPage />} />
          <Route path="/purchase-units" element={<PurchaseUnitsPage />} />
          {/* Actual cost list + customer service price list (spec sections 34 + 36). */}
          <Route path="/price-lists" element={<PriceListsPage />} />
          <Route path="/separates" element={<SeparatesPage />} />
          <Route path="/materials" element={<MaterialsPage />} />
          <Route path="/ready-goods" element={<ReadyGoodsPage />} />
          <Route path="/deliveries" element={<DeliveriesPage />} />
          <Route path="/invoices" element={<InvoicesPage />} />
          <Route path="/treasury" element={<TreasuryPage />} />
          <Route path="/checks" element={<ChecksPage />} />
          <Route path="/formation-requests" element={<FormationRequestsPage />} />
          <Route path="/formation-requests/:id" element={<FormationRequestDetailPage />} />
          <Route path="/formation-specifications" element={<FormationSpecificationsPage />} />
          <Route path="/purchases" element={<PurchasesPage />} />
          <Route path="/payroll" element={<PayrollPage />} />
          <Route path="/hr" element={<HrPage />} />
          <Route path="/approvals" element={<ApprovalCenterPage />} />
          {/* Operating supplies and material sales live under Materials & supplies
              (spec sections 26 + 27) - they are factory-owned stock, not custodies. */}
          <Route path="/supplies" element={<SuppliesPage />} />
          <Route path="/material-sales" element={<MaterialSalesPage />} />
          <Route path="/production-floor" element={<ProductionFloorPage />} />
          {/* The phone-first floor screen: one card per live order with its
              next action, no admin tables. A separate route (not a breakpoint
              restyle) so a home-screen shortcut opens it directly. */}
          <Route path="/floor/m" element={<FloorMobilePage />} />
          <Route path="/production-stages" element={<ProductionStagesPage />} />
          <Route path="/production-orders" element={<ProductionOrdersPage />} />
          <Route path="/production-orders/:id" element={<ProductionOrderDetailPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/audit-log" element={<AuditLogPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/reports/builder" element={<ReportBuilderPage />} />
          <Route path="/period-closing" element={<PeriodClosingPage />} />
          </Route>

          {/* Full-page routes: no sidebar, so printing only includes the document itself */}
          <Route path="/print/invoice/:id" element={<InvoicePrintPage />} />
          <Route path="/print/delivery/:id" element={<DeliveryPrintPage />} />
          {/* The delivery STATEMENT (بيان تسليم) - the production accounting per delivered line,
              with configurable columns (spec section 31). */}
          <Route path="/print/delivery-statement/:id" element={<DeliveryStatementPrintPage />} />
          <Route path="/print/production-order/:id" element={<ProductionOrderPrintPage />} />
          <Route path="/print/raw-message/:id" element={<RawMessagePrintPage />} />
          {/* بطاقة تتبع اللوط - the lot's ledger movements with the cumulative balance after each one,
              as an official document. The :scope switch names the lot's identity the way the screen does
              (raw = receipt message, ready = job order, materials = material lot), and ?lots=id,id,id
              prints SEVERAL lots as independent cards, one per sheet. */}
          <Route path="/print/stock-traceability/:scope" element={<StockTraceabilityPrintPage />} />
          <Route path="/scan/:type/:id" element={<ScanViewPage />} />
        </Route>

        {/* Last route on purpose: without it an unknown URL renders a blank
            page inside the shell, with no message and no way back. Kept outside
            the authenticated group so a bad link is still recoverable when
            signed out. */}
        <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </Suspense>
      {/* New-deployment refresh prompt (Part 3): outside the routes so it can
          appear on any screen, including /login and /scan/*. */}
      <ServiceWorkerUpdatePrompt />
    </ErrorBoundary>
  );
}
