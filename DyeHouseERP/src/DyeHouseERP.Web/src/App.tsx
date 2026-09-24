import { Routes, Route } from "react-router-dom";
import Layout from "@/components/Layout";
import RequireAuth from "@/components/RequireAuth";
import LoginPage from "@/pages/LoginPage";
import DashboardPage from "@/pages/DashboardPage";
import CustomersPage from "@/pages/CustomersPage";
import ItemsPage from "@/pages/ItemsPage";
import WarehousesPage from "@/pages/WarehousesPage";
import RawMessagesPage from "@/pages/RawMessagesPage";
import ProductionStagesPage from "@/pages/ProductionStagesPage";
import ProductionOrdersPage from "@/pages/ProductionOrdersPage";
import ProductionOrderDetailPage from "@/pages/ProductionOrderDetailPage";
import RawExternalReleasesPage from "@/pages/RawExternalReleasesPage";
import CustomerTransfersPage from "@/pages/CustomerTransfersPage";
import StockAdjustmentsPage from "@/pages/StockAdjustmentsPage";
import SeparatesPage from "@/pages/SeparatesPage";
import MaterialsPage from "@/pages/MaterialsPage";
import ReadyGoodsPage from "@/pages/ReadyGoodsPage";
import DeliveriesPage from "@/pages/DeliveriesPage";
import InvoicesPage from "@/pages/InvoicesPage";
import TreasuryPage from "@/pages/TreasuryPage";
import CustomerPortalPage from "@/pages/CustomerPortalPage";
import ProductionFloorPage from "@/pages/ProductionFloorPage";
import UsersPage from "@/pages/UsersPage";
import ReportsPage from "@/pages/ReportsPage";
import AuditLogPage from "@/pages/AuditLogPage";
import SettingsPage from "@/pages/SettingsPage";
import InvoicePrintPage from "@/pages/print/InvoicePrintPage";
import DeliveryPrintPage from "@/pages/print/DeliveryPrintPage";
import ProductionOrderPrintPage from "@/pages/print/ProductionOrderPrintPage";
import RawMessagePrintPage from "@/pages/print/RawMessagePrintPage";
import ScanViewPage from "@/pages/scan/ScanViewPage";
import ReportBuilderPage from "@/pages/ReportBuilderPage";
import PeriodClosingPage from "@/pages/PeriodClosingPage";

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<Layout />}>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/customers" element={<CustomersPage />} />
        <Route path="/items" element={<ItemsPage />} />
        <Route path="/warehouses" element={<WarehousesPage />} />
        <Route path="/raw-messages" element={<RawMessagesPage />} />
        <Route path="/raw-external-releases" element={<RawExternalReleasesPage />} />
        <Route path="/customer-transfers" element={<CustomerTransfersPage />} />
        <Route path="/stock-adjustments" element={<StockAdjustmentsPage />} />
        <Route path="/separates" element={<SeparatesPage />} />
        <Route path="/materials" element={<MaterialsPage />} />
        <Route path="/ready-goods" element={<ReadyGoodsPage />} />
        <Route path="/deliveries" element={<DeliveriesPage />} />
        <Route path="/invoices" element={<InvoicesPage />} />
        <Route path="/treasury" element={<TreasuryPage />} />
        <Route path="/customer-portal" element={<CustomerPortalPage />} />
        <Route path="/production-floor" element={<ProductionFloorPage />} />
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
        <Route path="/print/production-order/:id" element={<ProductionOrderPrintPage />} />
        <Route path="/print/raw-message/:id" element={<RawMessagePrintPage />} />
        <Route path="/scan/:type/:id" element={<ScanViewPage />} />
      </Route>
    </Routes>
  );
}
