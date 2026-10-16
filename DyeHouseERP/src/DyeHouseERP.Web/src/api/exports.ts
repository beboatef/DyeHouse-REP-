import { api } from "./client";
import type { StockScope } from "./client";
import type { DeliveryQuantityColumns } from "../deliveryPrintColumns";

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
  /**
   * The item list as a printable PDF. `format=pdf` is sent EXPLICITLY: `/items/export`
   * defaults to `excel`, so asking for it without the parameter downloads a real .xlsx
   * spreadsheet under a .pdf name - a file that opens in the wrong program (or not at all).
   * The API's own `format=pdf` branch is what produces these bytes; nothing else changed.
   */
  pdf: () => downloadFile("/items/export", "items.pdf", { format: "pdf" }),
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
  /**
   * The permit PDF. The quantity columns the user chose for this document format travel with the
   * request, so the downloaded file shows the same columns as the on-screen print preview (hidden
   * columns are hidden on paper only - no recorded quantity changes).
   */
  documentPdf: (id: string, columns: DeliveryQuantityColumns) => () =>
    downloadFile(`/deliveries/${id}/pdf`, "delivery.pdf", {
      includeKg: columns.kg, includeMeter: columns.meter, includeRolls: columns.rolls
    }),
  /** The delivery statement (بيان تسليم) with the production accounting per line. */
  statementPdf: (id: string) => () => downloadFile(`/deliveries/${id}/statement/pdf`, "delivery-statement.pdf")
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

/**
 * A file-name-safe stem. Letters, digits, '.', '-' and '_' survive; anything else (an Arabic
 * character, a slash, a space) becomes '-', runs collapse and the ends are trimmed, so a download
 * name can never carry a path separator or come out empty.
 */
const safeStem = (value: string | undefined, fallback: string) => {
  const cleaned = (value ?? "")
    .replace(/[^A-Za-z0-9._-]+/g, "-")
    .replace(/-{2,}/g, "-")
    .replace(/^[-.]+|[-.]+$/g, "");
  return cleaned.length > 0 ? cleaned : fallback;
};

/**
 * The FULL identity of ONE lot, which is what the lot-card endpoint requires: the document that names
 * the lot in its warehouse (a receipt message, a job order, a material) PLUS the dimension that makes
 * it a single balance - the item inside that message or job order, or the warehouse a material is
 * stocked in.
 *
 * Why the full identity and not just the document id: one receipt message can carry SEVERAL items and
 * one material can sit in SEVERAL warehouses, and each of those is an independent balance with its own
 * stock. Naming only the document leaves the server unable to tell which one is meant - which is how a
 * card ends up showing one item's name beside a total of two items' stock.
 */
export interface LotCardIdentity {
  rawMessageId?: string;
  productionOrderId?: string;
  materialId?: string;
  /** Raw / ready: the item inside the message or job order - what makes the lot a single balance. */
  itemId?: string;
  /** Materials: the warehouse the material is stocked in - the other half of a materials lot. */
  warehouseId?: string;
}

/**
 * One lot's identity as the lot-card query string. Only the fields its own warehouse balances by are
 * sent: the server REFUSES an identifier that does not belong to the scope (a job order on a raw card)
 * instead of quietly ignoring it, so sending a meaningless one would turn a valid request into a 400.
 */
const lotCardParams = (scope: StockScope, lot: LotCardIdentity): QueryParams =>
  scope === "raw"
    ? { rawMessageId: lot.rawMessageId, itemId: lot.itemId }
    : scope === "ready"
      ? { productionOrderId: lot.productionOrderId, itemId: lot.itemId }
      : { materialId: lot.materialId, warehouseId: lot.warehouseId };

/**
 * One lot as it travels in a print URL: `?lots=<document id>` or `?lots=<document id>@<disambiguator>`,
 * where the disambiguator is the item id (raw/ready) or the warehouse id (materials). Null when the lot
 * has no identity at all - a historical ledger row with no link cannot be named on a card.
 *
 * The disambiguator is what keeps two balance rows of the SAME document apart: with only the message
 * id, selecting both items of one receipt would collapse into a single card for whichever came first.
 */
export const lotToken = (scope: StockScope, lot: LotCardIdentity): string | null => {
  const [primary, extra] = scope === "raw"
    ? [lot.rawMessageId, lot.itemId]
    : scope === "ready"
      ? [lot.productionOrderId, lot.itemId]
      : [lot.materialId, lot.warehouseId];
  if (!primary) return null;
  return extra ? `${primary}@${extra}` : primary;
};

/**
 * The identity a print URL names, back again. A token without a disambiguator stays valid (the server
 * then answers, or refuses an ambiguous one explicitly) so old links and hand-typed URLs still work.
 * A token that is not even a document id yields null rather than a request for some other lot.
 */
export const parseLotToken = (scope: StockScope, token: string): LotCardIdentity | null => {
  const [primary, extra] = token.split("@");
  if (!primary) return null;
  if (scope === "raw") return { rawMessageId: primary, itemId: extra };
  if (scope === "ready") return { productionOrderId: primary, itemId: extra };
  return { materialId: primary, warehouseId: extra };
};

export const InventoryTraceabilityExports = {
  /** The balances + movements list, honouring the same filters the screen shows. */
  stock: (format: ExportFormat) => (params?: QueryParams) =>
    downloadFile("/inventory/traceability/export", `stock.${extension(format)}`, { ...params, format }),

  /**
   * بطاقة تتبع اللوط as a SERVER-rendered PDF (spec sections 10/14/30): ONE lot's identity, its ledger
   * movements - each row naming its item - and the cumulative balance after each one, built by the same
   * traceability read the screen uses. The lot is named by its FULL identity, so the file can only ever
   * contain the lot that was asked for: the server refuses a request that does not resolve to exactly
   * one balance rather than printing the first one it finds.
   */
  lotCardPdf: (scope: StockScope, lot: LotCardIdentity, lotNumber?: string) => () =>
    downloadFile(
      "/inventory/traceability/lot-card",
      `lot-card-${safeStem(lotNumber, scope)}.pdf`,
      { scope, ...lotCardParams(scope, lot) }
    )
};

export const JobOrderCostingExports = {
  file: (format: ExportFormat) => (productionOrderId: string, number: string) =>
    downloadFile(`/production-orders/${productionOrderId}/cost/export`, `costing-${number}.${extension(format)}`, {
      productionOrderId,
      format
    })
};
