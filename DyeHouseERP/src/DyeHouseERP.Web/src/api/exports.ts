import { api } from "./client";

/**
 * Every file-producing endpoint in the API, in one place.
 *
 * Why this module exists: the backend exposes a real `?format=excel|pdf`
 * download on most list endpoints plus a `/pdf` on most single documents, but
 * each page used to hand-roll its own axios call. That made it easy for a
 * screen to *look* like it had an export button while actually posting JSON.
 * Here the request shape, the `format` parameter and the resulting file name
 * are declared once, and `ExportButtons` renders the matching buttons, so
 * Backend -> API -> UI -> file is one verifiable chain.
 *
 * Nothing here is generated from a fake data source: every call below hits a
 * controller action that streams a real .xlsx or .pdf built by
 * `IReportExportService`.
 */

export type ExportFormat = "excel" | "pdf";

export type QueryParams = Record<string, string | number | boolean | null | undefined>;

const extension = (format: ExportFormat) => (format === "pdf" ? "pdf" : "xlsx");

/** Drops null/undefined/"" so an unfilled filter is simply absent from the query string. */
export function toQuery(params?: QueryParams): Record<string, string> {
  const query: Record<string, string> = {};
  for (const [key, value] of Object.entries(params ?? {})) {
    if (value === null || value === undefined || value === "") continue;
    query[key] = typeof value === "boolean" ? (value ? "true" : "false") : String(value);
  }
  return query;
}

/** Streams a binary response to the user's downloads folder. */
export async function downloadFile(url: string, fileName: string, params?: QueryParams) {
  const response = await api.get(url, { params: toQuery(params), responseType: "blob" });
  const blobUrl = window.URL.createObjectURL(new Blob([response.data as Blob]));
  const link = document.createElement("a");
  link.href = blobUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(blobUrl);
}

/**
 * Builds a ready-to-click action. The returned function takes the filters at
 * call time, so a screen can bind `action: MaterialsExports.excel` directly
 * when there are no filters and `action: () => X.excel({ from, to })` when
 * there are.
 */
const download = (url: string, fileName: string) => (params?: QueryParams) => downloadFile(url, fileName, params);

/** A `{ excel, pdf }` pair for one `?format=` list endpoint. */
const both = (url: string, stem: string) => ({
  excel: download(url, `${stem}.xlsx`),
  pdf: download(url, `${stem}.pdf`)
});

// ---------------------------------------------------------------------------
// Master data
// ---------------------------------------------------------------------------

export const CustomersExports = {
  ...both("/customers/export", "customers"),
  excel: download("/customers/export/excel", "customers.xlsx"),
  template: download("/customers/import/template", "customers-import-template.xlsx")
};

export const ItemsExports = {
  ...both("/items/export", "items"),
  excel: download("/items/export/excel", "items.xlsx"),
  template: download("/items/import/template", "items-import-template.xlsx")
};

export const SuppliersExports = {
  ...both("/suppliers/export", "suppliers"),
  template: download("/suppliers/import/template", "suppliers-import-template.xlsx")
};

export const WarehousesExports = {
  ...both("/warehouses/export", "warehouses"),
  template: download("/warehouses/import/template", "warehouses-import-template.xlsx")
};

export const MaterialsExports = {
  ...both("/materials/export", "materials"),
  template: download("/materials/import/template", "materials-import-template.xlsx"),
  transfers: both("/material-transfers/export", "material-transfers"),
  issues: both("/material-issues/export", "material-issues"),
  preparations: both("/material-preparations/export", "material-preparations")
};

export const EmployeesExports = {
  excel: download("/payroll/employees/export", "employees.xlsx"),
  template: download("/payroll/employees/import/template", "employees-import-template.xlsx")
};

// ---------------------------------------------------------------------------
// Operational documents and registers
// ---------------------------------------------------------------------------

export const RawMessagesExports = {
  ...both("/raw-messages/export", "raw-messages"),
  documentPdf: (id: string) => () => downloadFile(`/raw-messages/${id}/pdf`, "raw-message.pdf")
};

export const ProductionOrdersExports = {
  ...both("/production-orders/export", "production-orders"),
  documentPdf: (id: string) => () => downloadFile(`/production-orders/${id}/pdf`, "production-order.pdf")
};

export const ExternalReleasesExports = both("/raw-external-releases/export", "external-releases");

export const CustomerTransfersExports = both("/customer-transfers/export", "customer-transfers");

export const StockAdjustmentsExports = both("/stock-adjustments/export", "stock-adjustments");

export const ReadyGoodsExports = {
  balance: both("/ready-goods/balance/export", "ready-goods-balance"),
  transfers: both("/ready-goods/transfers/export", "ready-goods-transfers")
};

export const DeliveriesExports = {
  ...both("/deliveries/export", "deliveries"),
  documentPdf: (id: string) => () => downloadFile(`/deliveries/${id}/pdf`, "delivery.pdf")
};

export const InvoicesExports = {
  list: both("/invoices/export", "invoices"),
  documentPdf: (id: string) => () => downloadFile(`/invoices/${id}/pdf`, "invoice.pdf")
};

export const MaterialSalesExports = {
  ...both("/material-sales/export", "material-sales"),
  documentPdf: (id: string) => () => downloadFile(`/material-sales/${id}/pdf`, "material-sale.pdf")
};

export const SuppliesExports = {
  ...both("/supplies/export", "supply-issues"),
  documentPdf: (id: string) => () => downloadFile(`/supplies/${id}/pdf`, "supply-issue.pdf")
};

export const FormationRequestsExports = {
  ...both("/formation-requests/export", "formation-requests"),
  excel: download("/formation-requests/export/excel", "formation-requests.xlsx"),
  documentPdf: (id: string) => () => downloadFile(`/formation-requests/${id}/pdf`, "formation-request.pdf")
};

export const ChecksExports = {
  excel: download("/checks/export/excel", "checks.xlsx"),
  pdf: download("/checks/export/pdf", "checks.pdf"),
  documentPdf: (id: string) => () => downloadFile(`/checks/${id}/pdf`, "check.pdf")
};

export const PurchasesExports = {
  orders: both("/purchases/orders/export", "purchase-orders"),
  receipts: both("/purchases/receipts/export", "purchase-receipts"),
  supplierInvoices: both("/purchases/supplier-invoices/export", "supplier-invoices"),
  supplierBalances: both("/purchases/supplier-balances/export", "supplier-balances"),
  supplierPayments: both("/purchases/supplier-payments/export", "supplier-payments"),
  orderPdf: (id: string) => () => downloadFile(`/purchases/orders/${id}/pdf`, "purchase-order.pdf"),
  receiptPdf: (id: string) => () => downloadFile(`/purchases/receipts/${id}/pdf`, "purchase-receipt.pdf"),
  supplierInvoicePdf: (id: string) => () => downloadFile(`/purchases/supplier-invoices/${id}/pdf`, "supplier-invoice.pdf")
};

export const PayrollExports = {
  runPdf: (runId: string) => () => downloadFile(`/payroll/runs/${runId}/pdf`, "payroll-run.pdf"),
  runExcel: (runId: string) => () => downloadFile(`/payroll/runs/${runId}/excel`, "payroll-run.xlsx"),
  payslipPdf: (runId: string, lineId: string) => () =>
    downloadFile(`/payroll/runs/${runId}/payslips/${lineId}/pdf`, "payslip.pdf")
};

// ---------------------------------------------------------------------------
// Statements and reports
// ---------------------------------------------------------------------------

export const StatementsExports = {
  /** كشف حساب العميل - the customer ledger as a running statement, PDF and Excel. */
  customer: (customerId: string, from?: string, to?: string) => ({
    pdf: () => downloadFile(`/customers/${customerId}/statement/pdf`, "customer-statement.pdf", { from, to }),
    excel: () => downloadFile(`/customers/${customerId}/statement/excel`, "customer-statement.xlsx", { from, to })
  }),

  /** كشف حساب الخزنة - one treasury account's movements, PDF and Excel. */
  treasury: (accountId: string, from?: string, to?: string) => ({
    pdf: () => downloadFile(`/treasury-accounts/${accountId}/statement/pdf`, "treasury-statement.pdf", { from, to }),
    excel: () => downloadFile(`/treasury-accounts/${accountId}/statement/excel`, "treasury-statement.xlsx", { from, to })
  }),

  /** كشف حساب المورد - the supplier ledger for one supplier. */
  supplier: (supplierId: string, from?: string, to?: string) => ({
    pdf: () => downloadFile(`/purchases/suppliers/${supplierId}/ledger/export`, "supplier-statement.pdf", { from, to, format: "pdf" }),
    excel: () => downloadFile(`/purchases/suppliers/${supplierId}/ledger/export`, "supplier-statement.xlsx", { from, to, format: "excel" })
  })
};

export const ReportsExports = {
  inventoryMovements: {
    excel: download("/reports/inventory-movements/excel", "inventory-movements.xlsx"),
    pdf: download("/reports/inventory-movements/pdf", "inventory-movements.pdf")
  },
  warehouseBalances: (format: ExportFormat) =>
    (params?: QueryParams) =>
      downloadFile(
        "/reports/warehouse-balances/export",
        `warehouse-balances.${extension(format)}`,
        { ...params, format }
      ),
  negativeStockOverrides: {
    excel: download("/reports/negative-stock-overrides/excel", "negative-stock-overrides.xlsx"),
    pdf: download("/reports/negative-stock-overrides/pdf", "negative-stock-overrides.pdf")
  }
};

export const JobOrderCostingExports = {
  file: (format: ExportFormat) => (productionOrderId: string, number: string) =>
    downloadFile(`/production-orders/${productionOrderId}/cost/export`, `costing-${number}.${extension(format)}`, {
      productionOrderId,
      format
    })
};
