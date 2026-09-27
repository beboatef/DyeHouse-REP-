import axios from "axios";

export const api = axios.create({
  baseURL: "/api",
  headers: { "Content-Type": "application/json" }
});

// Attach the bearer token, once a real login flow exists. For now it reads
// a token that a future login page would store here.
api.interceptors.request.use((config) => {
  const token = localStorage.getItem("dyehouse_token");
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401 && window.location.pathname !== "/login") {
      localStorage.removeItem("dyehouse_token");
      localStorage.removeItem("dyehouse_user");
      window.location.href = "/login";
    }
    return Promise.reject(error);
  }
);

export type UnitOfMeasure = "KG" | "Meter";

export interface Customer {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export interface Item {
  id: string;
  code: string;
  name: string;
  nameAr: string;
  nameEn: string;
  category: string | null;
  baseUnit: UnitOfMeasure;
  isActive: boolean;
}

export interface Warehouse {
  id: string;
  code: string;
  name: string;
  kind: "RawMaterial" | "ProductionWip" | "ReadyGoods" | "Materials" | "OperatingSupplies";
  isActive: boolean;
}

export interface RawMessageLine {
  id: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  quantityKg: number | null;
  quantityMeter: number | null;
  pieceCount: number | null;
  notes: string | null;
  /** Rejected at receiving inspection, if any (spec sections 8-9). Inspection is recorded info, never an approval gate. */
  rejectedQuantityKg: number | null;
  rejectedQuantityMeter: number | null;
  /** Received minus rejected - what stays the customer's allocatable stock. */
  acceptedQuantityKg: number | null;
  acceptedQuantityMeter: number | null;
  remainingKg: number | null;
  remainingMeter: number | null;
}

export interface RawMessage {
  id: string;
  messageNumber: string;
  receiptDate: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  warehouseId: string;
  warehouseName: string;
  receivingUser: string;
  notes: string | null;
  inspectionStatus: "PendingInspection" | "Accepted" | "AcceptedWithNotes" | "Rejected";
  status: "Open" | "PartiallyUsed" | "Depleted" | "Closed";
  hasRejections: boolean;
  lines: RawMessageLine[];
}

export const CustomersApi = {
  list: (params?: { activeOnly?: boolean; search?: string }) =>
    api.get<Customer[]>("/customers", { params }).then((r) => r.data),
  create: (body: { code: string; name: string }) =>
    api.post<Customer>("/customers", body).then((r) => r.data)
};

export const ItemsApi = {
  list: (params?: { activeOnly?: boolean; search?: string; category?: string }) =>
    api.get<Item[]>("/items", { params }).then((r) => r.data),
  create: (body: {
    code: string;
    name: string;
    baseUnit: UnitOfMeasure;
    nameAr?: string;
    nameEn?: string;
    category?: string;
  }) => api.post<Item>("/items", body).then((r) => r.data),
  update: (id: string, body: {
    nameAr?: string;
    nameEn?: string;
    category?: string;
    baseUnit?: UnitOfMeasure;
    isActive?: boolean;
  }) => api.put<Item>(`/items/${id}`, { id, ...body }).then((r) => r.data),
  exportExcel: (params?: { activeOnly?: boolean; search?: string }) => {
    const query = new URLSearchParams();
    if (params?.activeOnly) query.set("activeOnly", "true");
    if (params?.search) query.set("search", params.search);
    return downloadFile(`/items/export/excel?${query.toString()}`, "items.xlsx");
  },
  downloadTemplate: () => downloadFile("/items/import/template", "items-import-template.xlsx")
};

// ---------------- Items Excel import (spec section 7) ----------------

export interface ItemImportRowResult {
  rowNumber: number;
  code: string;
  name: string;
  nameAr: string;
  nameEn: string;
  category: string | null;
  baseUnit: string;
  isValid: boolean;
  isExisting: boolean;
  action: "Create" | "Update" | "Skip" | string;
  errors: string[];
}

export interface ItemImportPreview {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  newRows: number;
  existingRows: number;
  updateExistingAllowed: boolean;
  rows: ItemImportRowResult[];
}

export interface ItemImportExecuteResult {
  created: number;
  updated: number;
  skipped: number;
  errors: string[];
  rows: ItemImportRowResult[];
}

export const ItemImportApi = {
  /** allowExistingUpdate targets the items.edit-protected endpoints (never the default ones). */
  preview: (file: File, allowExistingUpdate = false) => {
    const form = new FormData();
    form.append("file", file);
    return api
      .post<ItemImportPreview>(allowExistingUpdate ? "/items/import/preview-update" : "/items/import/preview", form, {
        headers: { "Content-Type": "multipart/form-data" }
      })
      .then((r) => r.data);
  },
  execute: (file: File, allowExistingUpdate = false) => {
    const form = new FormData();
    form.append("file", file);
    return api
      .post<ItemImportExecuteResult>(allowExistingUpdate ? "/items/import/execute-update" : "/items/import/execute", form, {
        headers: { "Content-Type": "multipart/form-data" }
      })
      .then((r) => r.data);
  }
};

export const WarehousesApi = {
  list: (params?: { kind?: string }) =>
    api.get<Warehouse[]>("/warehouses", { params }).then((r) => r.data),
  create: (body: { code: string; name: string; kind: string }) =>
    api.post<Warehouse>("/warehouses", body).then((r) => r.data)
};

export const RawMessagesApi = {
  list: (params?: { customerId?: string; itemId?: string; onlyWithBalance?: boolean }) =>
    api.get<RawMessage[]>("/raw-messages", { params }).then((r) => r.data),
  get: (id: string) => api.get<RawMessage>(`/raw-messages/${id}`).then((r) => r.data),
  create: (body: {
    receiptDate: string;
    customerId: string;
    warehouseId: string;
    notes?: string;
    lines: { itemId: string; quantityKg?: number; quantityMeter?: number; pieceCount?: number; notes?: string }[];
  }) => api.post<RawMessage>("/raw-messages", body).then((r) => r.data),
  recordInspection: (
    id: string,
    body: {
      result: string;
      notes?: string;
      rejections?: { lineId: string; rejectedQuantityKg?: number; rejectedQuantityMeter?: number }[];
    }
  ) => api.post(`/raw-messages/${id}/inspection`, body)
};

// ---------------- Production Stages (configurable engine) ----------------

export interface ProductionStageDefinition {
  id: string;
  code: string;
  name: string;
  sequence: number;
  isActive: boolean;
  requiresInputQuantity: boolean;
  requiresOutputQuantity: boolean;
  requiresApproval: boolean;
  allowSkip: boolean;
  allowRepeat: boolean;
  allowRework: boolean;
  allowReturn: boolean;
  notes: string | null;
}

export const ProductionStagesApi = {
  list: (params?: { activeOnly?: boolean }) =>
    api.get<ProductionStageDefinition[]>("/production-stages", { params }).then((r) => r.data),
  create: (body: {
    code: string;
    name: string;
    sequence: number;
    requiresInputQuantity: boolean;
    requiresOutputQuantity: boolean;
    requiresApproval: boolean;
    allowSkip: boolean;
    allowRepeat: boolean;
    allowRework: boolean;
    allowReturn: boolean;
    notes?: string;
  }) => api.post<ProductionStageDefinition>("/production-stages", body).then((r) => r.data)
};

// ---------------- Production Orders ----------------

export type ProductionOrderStatus = "Draft" | "RawAllocated" | "InProduction" | "Completed" | "Cancelled";
export type ProductionPriority = "Low" | "Normal" | "High" | "Urgent";
/** Job order line type (spec section 16): closed line / open line. */
export type JobOrderType = "ClosedLine" | "OpenLine";
export type StageExecutionStatus = "Pending" | "InProgress" | "Completed" | "Skipped";

export interface RawAllocationLine {
  id: string;
  rawMessageId: string;
  messageNumber: string;
  quantityKg: number | null;
  quantityMeter: number | null;
  allocatedBy: string;
  allocatedAtUtc: string;
}

export interface StageExecution {
  id: string;
  stageDefinitionId: string;
  stageCode: string;
  stageName: string;
  sequence: number;
  status: StageExecutionStatus;
  inputKg: number | null;
  inputMeter: number | null;
  outputKg: number | null;
  outputMeter: number | null;
  lossKg: number | null;
  lossMeter: number | null;
  separatesKg: number | null;
  separatesMeter: number | null;
  operator: string | null;
  notes: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  requiresApproval: boolean;
  allowSkip: boolean;
}

export interface ProductionOrder {
  id: string;
  orderNumber: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  color: string | null;
  requestedQuantityKg: number | null;
  requestedQuantityMeter: number | null;
  customerReference: string | null;
  notes: string | null;
  priority: ProductionPriority;
  jobOrderType: JobOrderType;
  orderDate: string;
  status: ProductionOrderStatus;
  reprocessingOfProductionOrderId: string | null;
  formationRequestId: string | null;
  formationRequestNumber: string | null;
  formationGroupId: string | null;
  formationGroupNumber: number | null;
  rawAllocations: RawAllocationLine[];
  stageExecutions: StageExecution[];
}

export const ProductionOrdersApi = {
  list: (params?: {
    customerId?: string;
    status?: ProductionOrderStatus;
    jobOrderType?: JobOrderType;
    formationRequestId?: string;
  }) => api.get<ProductionOrder[]>("/production-orders", { params }).then((r) => r.data),
  get: (id: string) => api.get<ProductionOrder>(`/production-orders/${id}`).then((r) => r.data),
  create: (body: {
    customerId: string;
    itemId: string;
    orderDate: string;
    color?: string;
    requestedQuantityKg?: number;
    requestedQuantityMeter?: number;
    rawOrigin?: string;
    customerReference?: string;
    notes?: string;
    priority: ProductionPriority;
    jobOrderType?: JobOrderType;
  }) => api.post<ProductionOrder>("/production-orders", body).then((r) => r.data),
  allocateRaw: (
    id: string,
    body: { rawMessageId: string; quantityKg?: number; quantityMeter?: number; overrideNegativeStock?: boolean; overrideReason?: string }
  ) => api.post<ProductionOrder>(`/production-orders/${id}/raw-allocations`, body).then((r) => r.data),
  startStage: (stageExecutionId: string) =>
    api.post<ProductionOrder>(`/production-orders/stage-executions/${stageExecutionId}/start`).then((r) => r.data),
  completeStage: (
    stageExecutionId: string,
    body: {
      inputKg?: number; inputMeter?: number; outputKg?: number; outputMeter?: number;
      lossKg?: number; lossMeter?: number; separatesKg?: number; separatesMeter?: number;
      notes?: string; approvedBy?: string;
    }
  ) => api.post<ProductionOrder>(`/production-orders/stage-executions/${stageExecutionId}/complete`, body).then((r) => r.data),
  skipStage: (stageExecutionId: string, reason: string) =>
    api.post<ProductionOrder>(`/production-orders/stage-executions/${stageExecutionId}/skip`, { reason }).then((r) => r.data),
  complete: (id: string) => api.post<ProductionOrder>(`/production-orders/${id}/complete`).then((r) => r.data)
};

// ---------------- Separates & Reprocessing ----------------

export type SeparateStatus = "PendingReprocessing" | "Reprocessing" | "Reprocessed" | "Ready" | "Scrapped";

export interface Separate {
  id: string;
  originalProductionOrderId: string;
  originalOrderNumber: string;
  stageExecutionId: string;
  stageName: string;
  customerId: string;
  customerCode: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  quantityKg: number | null;
  quantityMeter: number | null;
  reason: string | null;
  notes: string | null;
  status: SeparateStatus;
  reprocessingProductionOrderId: string | null;
  reprocessingOrderNumber: string | null;
}

export const SeparatesApi = {
  list: (params?: { status?: SeparateStatus }) => api.get<Separate[]>("/separates", { params }).then((r) => r.data),
  reprocess: (id: string, body: { orderDate: string; notes?: string }) =>
    api.post<ProductionOrder>(`/separates/${id}/reprocess`, body).then((r) => r.data),
  scrap: (id: string, reason: string) => api.post<Separate>(`/separates/${id}/scrap`, { reason }).then((r) => r.data)
};

// ---------------- Raw External Releases (return / external processing / sale) ----------------

export type RawReleaseReason = "ReturnToCustomer" | "ExternalProcessing" | "Sale";

export interface RawExternalRelease {
  id: string;
  releaseNumber: string;
  releaseDate: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  rawMessageId: string;
  messageNumber: string;
  quantityKg: number | null;
  quantityMeter: number | null;
  reason: RawReleaseReason;
  externalParty: string | null;
  notes: string | null;
  createdBy: string;
  createdAtUtc: string;
}

export const RawExternalReleasesApi = {
  list: (params?: { customerId?: string }) =>
    api.get<RawExternalRelease[]>("/raw-external-releases", { params }).then((r) => r.data),
  create: (body: {
    customerId: string; itemId: string; rawMessageId: string;
    quantityKg?: number; quantityMeter?: number; reason: RawReleaseReason;
    externalParty?: string; notes?: string;
    overrideNegativeStock?: boolean; overrideReason?: string;
  }) => api.post<RawExternalRelease>("/raw-external-releases", body).then((r) => r.data)
};

// ---------------- Customer-to-Customer Transfers ----------------

export interface CustomerTransfer {
  id: string;
  transferNumber: string;
  transferDate: string;
  fromCustomerId: string;
  fromCustomerCode: string;
  fromCustomerName: string;
  toCustomerId: string;
  toCustomerCode: string;
  toCustomerName: string;
  rawMessageId: string;
  messageNumber: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  quantityKg: number | null;
  quantityMeter: number | null;
  reason: string;
  notes: string | null;
  createdBy: string;
  createdAtUtc: string;
}

export const CustomerTransfersApi = {
  list: (params?: { customerId?: string }) =>
    api.get<CustomerTransfer[]>("/customer-transfers", { params }).then((r) => r.data),
  create: (body: {
    fromCustomerId: string; toCustomerId: string; rawMessageId: string; itemId: string;
    quantityKg?: number; quantityMeter?: number; reason: string; notes?: string;
    overrideNegativeStock?: boolean; overrideReason?: string;
  }) => api.post<CustomerTransfer>("/customer-transfers", body).then((r) => r.data)
};

// ---------------- Stock Adjustments ----------------

export type AdjustmentType = "Increase" | "Decrease";

export interface StockAdjustment {
  id: string;
  adjustmentNumber: string;
  adjustmentDate: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  rawMessageId: string;
  messageNumber: string;
  type: AdjustmentType;
  quantityBeforeKg: number | null;
  quantityBeforeMeter: number | null;
  adjustmentQuantityKg: number | null;
  adjustmentQuantityMeter: number | null;
  quantityAfterKg: number | null;
  quantityAfterMeter: number | null;
  reason: string;
  notes: string | null;
  approvedBy: string | null;
  createdBy: string;
  createdAtUtc: string;
}

export const StockAdjustmentsApi = {
  list: (params?: { customerId?: string }) =>
    api.get<StockAdjustment[]>("/stock-adjustments", { params }).then((r) => r.data),
  create: (body: {
    customerId: string; itemId: string; rawMessageId: string; type: AdjustmentType;
    quantityKg?: number; quantityMeter?: number; reason: string; notes?: string; approvedBy?: string;
    overrideNegativeStock?: boolean; overrideReason?: string;
  }) => api.post<StockAdjustment>("/stock-adjustments", body).then((r) => r.data)
};

// ---------------- Materials / Chemicals ----------------

export type MaterialUnit = "KG" | "Gram" | "Liter";

export interface Material {
  id: string; code: string; name: string; unit: MaterialUnit; purchasePrice: number; isActive: boolean;
}

export const MaterialsApi = {
  list: (params?: { activeOnly?: boolean }) => api.get<Material[]>("/materials", { params }).then((r) => r.data),
  create: (body: { code: string; name: string; unit: MaterialUnit; purchasePrice: number }) =>
    api.post<Material>("/materials", body).then((r) => r.data)
};

export interface MaterialTransfer {
  id: string; transferNumber: string; transferDate: string; materialId: string; materialCode: string;
  fromWarehouseId: string; fromWarehouseName: string; toWarehouseId: string; toWarehouseName: string;
  quantity: number; notes: string | null;
}

export const MaterialTransfersApi = {
  list: () => api.get<MaterialTransfer[]>("/material-transfers").then((r) => r.data),
  create: (body: { materialId: string; fromWarehouseId: string; toWarehouseId: string; quantity: number; notes?: string }) =>
    api.post<MaterialTransfer>("/material-transfers", body).then((r) => r.data)
};

export interface MaterialIssue {
  id: string; issueNumber: string; issueDate: string; materialId: string; materialCode: string; materialName: string;
  warehouseId: string; productionOrderId: string; productionOrderNumber: string;
  quantity: number; unitCost: number; totalCost: number; notes: string | null;
}

export const MaterialIssuesApi = {
  list: (params?: { productionOrderId?: string }) => api.get<MaterialIssue[]>("/material-issues", { params }).then((r) => r.data),
  create: (body: { materialId: string; warehouseId: string; productionOrderId: string; quantity: number; unitCost?: number; notes?: string }) =>
    api.post<MaterialIssue>("/material-issues", body).then((r) => r.data)
};

export interface MaterialPreparation {
  id: string; preparationNumber: string; preparationDate: string; originalMaterialId: string; originalMaterialCode: string;
  originalQuantity: number; waterQuantity: number; resultingQuantity: number; concentration: number | null;
  cost: number | null; productionOrderId: string | null; notes: string | null;
}

export const MaterialPreparationsApi = {
  list: () => api.get<MaterialPreparation[]>("/material-preparations").then((r) => r.data),
  create: (body: {
    originalMaterialId: string; warehouseId: string; originalQuantity: number; waterQuantity: number;
    resultingQuantity: number; concentration?: number; productionOrderId?: string; notes?: string;
  }) => api.post<MaterialPreparation>("/material-preparations", body).then((r) => r.data)
};

// ---------------- Ready Goods ----------------

export interface ReadyGoodsTransfer {
  id: string; transferNumber: string; transferDate: string;
  productionOrderId: string; productionOrderNumber: string;
  customerId: string; customerCode: string; itemId: string; itemCode: string; color: string | null;
  quantityKg: number | null; quantityMeter: number | null; pieceCount: number | null;
}

export interface ReadyGoodsBalance {
  productionOrderId: string; productionOrderNumber: string;
  customerId: string; customerCode: string; customerName: string;
  itemId: string; itemCode: string; itemName: string; color: string | null;
  remainingKg: number; remainingMeter: number;
}

export const ReadyGoodsApi = {
  listTransfers: () => api.get<ReadyGoodsTransfer[]>("/ready-goods/transfers").then((r) => r.data),
  balance: (params?: { customerId?: string }) => api.get<ReadyGoodsBalance[]>("/ready-goods/balance", { params }).then((r) => r.data),
  createTransfer: (body: { productionOrderId: string; warehouseId: string; quantityKg?: number; quantityMeter?: number; pieceCount?: number; notes?: string }) =>
    api.post<ReadyGoodsTransfer>("/ready-goods/transfers", body).then((r) => r.data)
};

// ---------------- Deliveries ----------------

export type DeliveryStatus = "Draft" | "Prepared" | "Delivered" | "Cancelled";

export interface DeliveryLine {
  id: string; productionOrderId: string; productionOrderNumber: string; itemId: string; itemCode: string;
  color: string | null; quantityKg: number | null; quantityMeter: number | null; pieceCount: number | null; rawOrigin: string | null;
}

export interface Delivery {
  id: string; deliveryNumber: string; deliveryDate: string;
  customerId: string; customerCode: string; customerName: string;
  status: DeliveryStatus; notes: string | null; lines: DeliveryLine[];
}

export const DeliveriesApi = {
  list: (params?: { customerId?: string; status?: DeliveryStatus }) => api.get<Delivery[]>("/deliveries", { params }).then((r) => r.data),
  get: (id: string) => api.get<Delivery>(`/deliveries/${id}`).then((r) => r.data),
  create: (body: {
    customerId: string; deliveryDate: string; notes?: string;
    lines: { productionOrderId: string; itemId: string; color?: string; quantityKg?: number; quantityMeter?: number; pieceCount?: number; rawOrigin?: string }[];
  }) => api.post<Delivery>("/deliveries", body).then((r) => r.data),
  prepare: (id: string) => api.post<Delivery>(`/deliveries/${id}/prepare`).then((r) => r.data),
  deliver: (id: string) => api.post<Delivery>(`/deliveries/${id}/deliver`).then((r) => r.data),
  cancel: (id: string, reason: string) => api.post<Delivery>(`/deliveries/${id}/cancel`, { reason }).then((r) => r.data)
};

// ---------------- Invoices & Customer Statement ----------------

export type InvoiceStatus = "Draft" | "Issued" | "PartiallyPaid" | "Paid" | "Cancelled";

export interface InvoiceLine {
  id: string; productionOrderId: string | null; productionOrderNumber: string | null;
  itemId: string; itemCode: string; color: string | null; quantity: number; processingPrice: number; value: number;
}

export interface Invoice {
  id: string; invoiceNumber: string; invoiceDate: string;
  customerId: string; customerCode: string; customerName: string;
  status: InvoiceStatus; discount: number; tax: number; subTotal: number; total: number;
  notes: string | null; lines: InvoiceLine[];
}

export const InvoicesApi = {
  list: (params?: { customerId?: string; status?: InvoiceStatus }) => api.get<Invoice[]>("/invoices", { params }).then((r) => r.data),
  get: (id: string) => api.get<Invoice>(`/invoices/${id}`).then((r) => r.data),
  create: (body: {
    customerId: string; invoiceDate: string; deliveryId?: string; discount: number; tax: number; notes?: string;
    lines: { productionOrderId?: string; itemId: string; color?: string; quantity: number; processingPrice: number }[];
  }) => api.post<Invoice>("/invoices", body).then((r) => r.data),
  issue: (id: string) => api.post<Invoice>(`/invoices/${id}/issue`).then((r) => r.data),
  cancel: (id: string, reason: string) => api.post<Invoice>(`/invoices/${id}/cancel`, { reason }).then((r) => r.data)
};

export interface CustomerStatementLine {
  date: string; description: string; documentNumber: string; debit: number; credit: number; runningBalance: number;
}

export const CustomerStatementApi = {
  get: (customerId: string, params?: { from?: string; to?: string }) =>
    api.get<CustomerStatementLine[]>(`/customers/${customerId}/statement`, { params }).then((r) => r.data)
};

// ---------------- Treasury ----------------

export type TreasuryAccountKind = "Cash" | "Bank";

export interface TreasuryAccount {
  id: string; code: string; name: string; kind: string; isActive: boolean; balance: number;
}

export const TreasuryAccountsApi = {
  list: () => api.get<TreasuryAccount[]>("/treasury-accounts").then((r) => r.data),
  create: (body: { code: string; name: string; kind: TreasuryAccountKind }) =>
    api.post<TreasuryAccount>("/treasury-accounts", body).then((r) => r.data)
};

export interface Receipt {
  id: string; receiptNumber: string; receiptDate: string; customerId: string | null; customerCode: string | null;
  treasuryAccountId: string; treasuryAccountName: string; invoiceId: string | null; invoiceNumber: string | null;
  amount: number; paymentMethod: string | null; description: string | null;
}

export const ReceiptsApi = {
  list: (params?: { customerId?: string }) => api.get<Receipt[]>("/receipts", { params }).then((r) => r.data),
  create: (body: { receiptDate: string; treasuryAccountId: string; amount: number; customerId?: string; invoiceId?: string; paymentMethod?: string; description?: string }) =>
    api.post<Receipt>("/receipts", body).then((r) => r.data)
};

export interface Payment {
  id: string; paymentNumber: string; paymentDate: string; treasuryAccountId: string; treasuryAccountName: string;
  amount: number; payeeDescription: string; description: string | null;
}

export const PaymentsApi = {
  list: () => api.get<Payment[]>("/payments").then((r) => r.data),
  create: (body: { paymentDate: string; treasuryAccountId: string; amount: number; payeeDescription: string; paymentMethod?: string; description?: string }) =>
    api.post<Payment>("/payments", body).then((r) => r.data)
};

export interface TreasuryTransfer {
  id: string; transferNumber: string; transferDate: string;
  fromAccountId: string; fromAccountName: string; toAccountId: string; toAccountName: string; amount: number; description: string | null;
}

export const TreasuryTransfersApi = {
  list: () => api.get<TreasuryTransfer[]>("/treasury-transfers").then((r) => r.data),
  create: (body: { transferDate: string; fromAccountId: string; toAccountId: string; amount: number; description?: string }) =>
    api.post<TreasuryTransfer>("/treasury-transfers", body).then((r) => r.data)
};

// ---------------- Cost Accounting ----------------

export type CostCategory = "Labor" | "Electricity" | "Fuel" | "Maintenance" | "Other";

export interface ProductionOrderCost {
  productionOrderId: string; productionOrderNumber: string;
  materialCost: number; preparationCost: number; laborCost: number; electricityCost: number;
  fuelCost: number; maintenanceCost: number; otherCost: number; totalCost: number;
  outputKg: number | null; outputMeter: number | null; costPerKg: number | null; costPerMeter: number | null;
  processingValue: number; profit: number; marginPercent: number | null;
}

export const CostAccountingApi = {
  get: (productionOrderId: string) => api.get<ProductionOrderCost>(`/production-orders/${productionOrderId}/cost`).then((r) => r.data),
  addEntry: (productionOrderId: string, body: { category: CostCategory; amount: number; entryDate: string; description?: string }) =>
    api.post(`/production-orders/${productionOrderId}/cost/entries`, body)
};

// ---------------- Customer Portal (Production Requests) ----------------

export type ProductionRequestStatus = "Pending" | "Approved" | "Rejected" | "ConvertedToOrder";

export interface ProductionRequest {
  id: string; requestNumber: string; requestDate: string;
  customerId: string; customerCode: string; itemId: string; itemCode: string; itemName: string;
  color: string | null; requestedQuantityKg: number | null; requestedQuantityMeter: number | null;
  notes: string | null; status: ProductionRequestStatus;
  convertedProductionOrderId: string | null; convertedOrderNumber: string | null; staffNotes: string | null;
}

export const ProductionRequestsApi = {
  list: (params?: { customerId?: string; status?: ProductionRequestStatus }) =>
    api.get<ProductionRequest[]>("/production-requests", { params }).then((r) => r.data),
  create: (body: { customerId: string; itemId: string; requestDate: string; color?: string; requestedQuantityKg?: number; requestedQuantityMeter?: number; notes?: string }) =>
    api.post<ProductionRequest>("/production-requests", body).then((r) => r.data),
  approve: (id: string, staffNotes?: string) => api.post<ProductionRequest>(`/production-requests/${id}/approve`, { staffNotes }).then((r) => r.data),
  reject: (id: string, reason: string) => api.post<ProductionRequest>(`/production-requests/${id}/reject`, { reason }).then((r) => r.data),
  convert: (id: string, orderDate: string) => api.post<ProductionRequest>(`/production-requests/${id}/convert`, { orderDate }).then((r) => r.data)
};

// ---------------- Production Floor Dashboard ----------------

export interface ProductionFloorRow {
  productionOrderId: string; orderNumber: string; customerCode: string; itemCode: string; color: string | null;
  requestedQuantityKg: number | null; requestedQuantityMeter: number | null;
  currentStageName: string; currentStageStatus: string; orderStatus: string; priority: string;
  stageStartedAtUtc: string | null; minutesInStage: number | null; notes: string | null;
}

export const ProductionFloorApi = {
  get: (params?: { customerId?: string; itemId?: string; priority?: string }) =>
    api.get<ProductionFloorRow[]>("/production-floor", { params }).then((r) => r.data)
};

// ---------------- Auth ----------------

export interface LoginResult {
  token: string; expiresAtUtc: string; username: string; displayName: string; roles: string[];
}

export const AuthApi = {
  login: (username: string, password: string) => api.post<LoginResult>("/auth/login", { username, password }).then((r) => r.data)
};

// ---------------- Users (admin only) ----------------

export interface User {
  id: string; username: string; displayName: string; roles: string[]; isActive: boolean;
}

export const UsersApi = {
  list: () => api.get<User[]>("/users").then((r) => r.data),
  create: (body: { username: string; password: string; displayName: string; roles: string[] }) =>
    api.post<User>("/users", body).then((r) => r.data),
  deactivate: (id: string) => api.post(`/users/${id}/deactivate`),
  changePassword: (id: string, newPassword: string) => api.post(`/users/${id}/change-password`, { newPassword })
};

// ---------------- File export helper ----------------

export async function downloadFile(url: string, filename: string) {
  const response = await api.get(url, { responseType: "blob" });
  const blobUrl = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement("a");
  link.href = blobUrl;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(blobUrl);
}

// ---------------- Reports ----------------

export interface NegativeStockOverride {
  id: string; approvedAtUtc: string; messageNumber: string; customerCode: string; itemCode: string;
  requestedQuantity: number; balanceBefore: number; resultingBalance: number;
  reason: string; requestedBy: string; approvedBy: string;
}

// ---------------- Inventory ledger movements (spec sections 10, 14, 48) ----------------

export interface InventoryMovement {
  id: string;
  sourceDocumentType: string;
  sourceDocumentNumber: string;
  sourceDocumentId: string;
  transactionDate: string;
  warehouseId: string;
  warehouseName: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  rawMessageId: string | null;
  messageNumber: string | null;
  productionOrderId: string | null;
  orderNumber: string | null;
  quantityKg: number | null;
  quantityMeter: number | null;
  direction: "In" | "Out";
  signedQuantityKg: number | null;
  signedQuantityMeter: number | null;
  createdBy: string;
  createdAtUtc: string;
  reversesTransactionId: string | null;
}

export const InventoryLedgerApi = {
  movements: (params?: {
    customerId?: string; itemId?: string; warehouseId?: string; rawMessageId?: string;
    productionOrderId?: string; from?: string; to?: string; limit?: number;
  }) => api.get<InventoryMovement[]>("/reports/inventory-movements", { params }).then((r) => r.data),
  exportMovementsExcel: (params?: {
    customerId?: string; itemId?: string; warehouseId?: string; rawMessageId?: string; from?: string; to?: string;
  }) => {
    const query = new URLSearchParams();
    if (params?.customerId) query.set("customerId", params.customerId);
    if (params?.itemId) query.set("itemId", params.itemId);
    if (params?.warehouseId) query.set("warehouseId", params.warehouseId);
    if (params?.rawMessageId) query.set("rawMessageId", params.rawMessageId);
    if (params?.from) query.set("from", params.from);
    if (params?.to) query.set("to", params.to);
    return downloadFile(`/reports/inventory-movements/excel?${query.toString()}`, "inventory-movements.xlsx");
  }
};

export const ReportsApi = {
  negativeStockOverrides: () => api.get<NegativeStockOverride[]>("/reports/negative-stock-overrides").then((r) => r.data),
  downloadNegativeStockOverridesPdf: () => downloadFile("/reports/negative-stock-overrides/pdf", "negative-stock-overrides.pdf"),
  downloadNegativeStockOverridesExcel: () => downloadFile("/reports/negative-stock-overrides/excel", "negative-stock-overrides.xlsx")
};

// ---------------- Audit Log (admin only) ----------------

export interface AuditLogEntry {
  id: string; occurredAtUtc: string; userName: string; action: string;
  entityName: string; entityId: string | null; beforeDataJson: string | null; afterDataJson: string | null;
  ipAddress: string | null; reason: string | null;
}

export const AuditLogApi = {
  list: (params?: { entityName?: string; userName?: string; from?: string; to?: string }) =>
    api.get<AuditLogEntry[]>("/audit-log", { params }).then((r) => r.data)
};

// ---------------- Company Settings (branding / logo) ----------------

export interface CompanySettings {
  companyNameAr: string;
  companyNameEn: string;
  logoDataUrl: string | null;
}

export const SettingsApi = {
  get: () => api.get<CompanySettings>("/settings").then((r) => r.data),
  update: (body: { companyNameAr: string; companyNameEn: string; logoDataUrl: string | null }) =>
    api.put<CompanySettings>("/settings", body).then((r) => r.data)
};

// ---------------- Dashboard ----------------

export interface DashboardSummary {
  activeCustomers: number;
  activeProductionOrders: number;
  pendingInspections: number;
  pendingNegativeStockRisk: number;
  readyGoodsBalanceKg: number;
  openInvoicesCount: number;
  openInvoicesTotal: number;
  pendingFormationRequests: number;
  formationRequestsInProgress: number;
  checksInHandAmount: number;
  checksDueSoonCount: number;
  overdueChecksCount: number;
  activeSuppliers: number;
  productionOrdersByStatus: { status: string; count: number }[];
  rawReceiptsLast14Days: { date: string; totalKg: number }[];
}

export const DashboardApi = {
  summary: () => api.get<DashboardSummary>("/dashboard/summary").then((r) => r.data)
};

// ---------------- Document PDFs (print/download) ----------------

export const DocumentPdfApi = {
  invoice: (id: string, number: string) => downloadFile(`/invoices/${id}/pdf`, `invoice-${number}.pdf`),
  delivery: (id: string, number: string) => downloadFile(`/deliveries/${id}/pdf`, `delivery-${number}.pdf`),
  productionOrder: (id: string, number: string) => downloadFile(`/production-orders/${id}/pdf`, `production-order-${number}.pdf`),
  rawMessage: (id: string, number: string) => downloadFile(`/raw-messages/${id}/pdf`, `raw-message-${number}.pdf`)
};

// ---------------- Customers Excel import ----------------

export interface ImportRowResult {
  rowNumber: number;
  isValid: boolean;
  error: string | null;
  values: Record<string, string | null>;
}

export interface CustomerImportPreview {
  rows: ImportRowResult[];
  validCount: number;
  invalidCount: number;
}

export interface CustomerImportExecuteResult {
  createdCount: number;
  skippedInvalidCount: number;
  errors: ImportRowResult[];
}

export const CustomerImportApi = {
  preview: (file: File) => {
    const form = new FormData();
    form.append("file", file);
    return api.post<CustomerImportPreview>("/customers/import/preview", form, { headers: { "Content-Type": "multipart/form-data" } }).then((r) => r.data);
  },
  execute: (file: File) => {
    const form = new FormData();
    form.append("file", file);
    return api.post<CustomerImportExecuteResult>("/customers/import/execute", form, { headers: { "Content-Type": "multipart/form-data" } }).then((r) => r.data);
  },
  exportExcel: (params?: { activeOnly?: boolean; search?: string }) => {
    const query = new URLSearchParams();
    if (params?.activeOnly) query.set("activeOnly", "true");
    if (params?.search) query.set("search", params.search);
    return downloadFile(`/customers/export/excel?${query.toString()}`, "customers.xlsx");
  }
};

// ---------------- Period Closing ----------------

export interface PeriodClose {
  id: string; periodStart: string; periodEnd: string; notes: string | null;
  isReopened: boolean; reopenedBy: string | null; reopenedAtUtc: string | null;
  createdBy: string; createdAtUtc: string;
}

export const PeriodClosingApi = {
  list: () => api.get<PeriodClose[]>("/period-closes").then((r) => r.data),
  close: (body: { periodStart: string; periodEnd: string; notes?: string }) =>
    api.post<PeriodClose>("/period-closes", body).then((r) => r.data),
  reopen: (id: string, reason: string) => api.post<PeriodClose>(`/period-closes/${id}/reopen`, { reason }).then((r) => r.data)
};

// ---------------- Custom Report Builder ----------------

export interface ReportableEntity { entityKey: string; label: string; columns: string[]; }
export interface ReportFilters { customerId?: string; itemId?: string; from?: string; to?: string; status?: string; }
export interface ReportResult { headers: string[]; rows: (string | null)[][]; }
export interface SavedReportTemplate { id: string; nameAr: string; nameEn: string; entityKey: string; columns: string[]; }

export const ReportBuilderApi = {
  entities: () => api.get<ReportableEntity[]>("/report-builder/entities").then((r) => r.data),
  run: (body: { entityKey: string; columns: string[]; filters?: ReportFilters }) =>
    api.post<ReportResult>("/report-builder/run", body).then((r) => r.data),
  templates: () => api.get<SavedReportTemplate[]>("/report-builder/templates").then((r) => r.data),
  saveTemplate: (body: { nameAr: string; nameEn: string; entityKey: string; columns: string[] }) =>
    api.post<SavedReportTemplate>("/report-builder/templates", body).then((r) => r.data),
  runPdf: (body: { entityKey: string; columns: string[]; filters?: ReportFilters }) =>
    api.post("/report-builder/run/pdf", body, { responseType: "blob" }).then((r) => {
      const url = window.URL.createObjectURL(new Blob([r.data]));
      const link = document.createElement("a");
      link.href = url; link.download = "custom-report.pdf"; document.body.appendChild(link); link.click(); link.remove();
    }),
  runExcel: (body: { entityKey: string; columns: string[]; filters?: ReportFilters }) =>
    api.post("/report-builder/run/excel", body, { responseType: "blob" }).then((r) => {
      const url = window.URL.createObjectURL(new Blob([r.data]));
      const link = document.createElement("a");
      link.href = url; link.download = "custom-report.xlsx"; document.body.appendChild(link); link.click(); link.remove();
    })
};

// ---------------- Formation Requests / طلب تشكيل (spec sections 28-33) ----------------

export type FormationRequestStatus =
  | "Draft" | "Submitted" | "Approved" | "InProgress"
  | "PartiallyCompleted" | "Completed" | "Rejected" | "Cancelled";

/** One group / cell on a request - its specification values are a snapshot taken when it was created. */
export interface FormationGroup {
  id: string;
  groupNumber: number;
  name: string | null;
  plannedQuantity: number;
  producedQuantity: number;
  remainingQuantity: number;
  unit: UnitOfMeasure;
  tubCount: number | null;
  color: string | null;
  widthCm: number | null;
  metersPerKg: number | null;
  gsm: number | null;
  tubFormat: string | null;
  windingTapeFormat: string | null;
  qualityInstructions: string | null;
  labInstructions: string | null;
  internalInstructions: string | null;
  customerInstructions: string | null;
  notes: string | null;
  specificationTemplateId: string | null;
  specificationTemplateName: string | null;
  specificationSnapshotAtUtc: string | null;
}

export interface FormationRequest {
  id: string;
  requestNumber: string;
  requestDate: string;
  customerId: string;
  customerCode: string;
  customerName: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  rawMessageId: string | null;
  messageNumber: string | null;
  totalQuantity: number;
  unit: UnitOfMeasure;
  notes: string | null;
  status: FormationRequestStatus;
  productionOrderId: string | null;
  productionOrderNumber: string | null;
  submittedBy: string | null;
  submittedAtUtc: string | null;
  approvedBy: string | null;
  approvedAtUtc: string | null;
  rejectedBy: string | null;
  rejectedAtUtc: string | null;
  rejectionReason: string | null;
  cancelledBy: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  createdBy: string;
  createdAtUtc: string;
  producedQuantity: number;
  groups: FormationGroup[];
}

export interface FormationSpecificationInput {
  widthCm?: number | null;
  metersPerKg?: number | null;
  gsm?: number | null;
  tubFormat?: string | null;
  windingTapeFormat?: string | null;
  qualityInstructions?: string | null;
  labInstructions?: string | null;
  internalInstructions?: string | null;
  customerInstructions?: string | null;
}

export interface FormationGroupInput {
  id?: string | null;
  name?: string | null;
  plannedQuantity: number;
  unit: UnitOfMeasure;
  tubCount?: number | null;
  color?: string | null;
  specificationTemplateId?: string | null;
  specification?: FormationSpecificationInput | null;
  notes?: string | null;
}

export interface FormationSpecTemplate {
  id: string;
  code: string;
  nameAr: string;
  nameEn: string;
  widthCm: number | null;
  metersPerKg: number | null;
  gsm: number | null;
  tubFormat: string | null;
  windingTapeFormat: string | null;
  notes: string | null;
  qualityInstructions: string | null;
  labInstructions: string | null;
  internalInstructions: string | null;
  customerInstructions: string | null;
  isActive: boolean;
}

export interface FormationTraceabilityLink {
  stage: string;
  reference: string;
  detail: string | null;
  route: string | null;
  entityId: string | null;
  dateUtc: string | null;
}

export interface FormationTraceability {
  request: FormationRequest;
  links: FormationTraceabilityLink[];
}

export const FormationRequestsApi = {
  list: (params?: {
    customerId?: string; itemId?: string; rawMessageId?: string;
    status?: FormationRequestStatus; from?: string; to?: string;
  }) => api.get<FormationRequest[]>("/formation-requests", { params }).then((r) => r.data),
  get: (id: string) => api.get<FormationRequest>(`/formation-requests/${id}`).then((r) => r.data),
  traceability: (id: string) =>
    api.get<FormationTraceability>(`/formation-requests/${id}/traceability`).then((r) => r.data),
  create: (body: {
    customerId: string; itemId: string; requestDate: string; unit: UnitOfMeasure;
    groups: FormationGroupInput[]; rawMessageId?: string | null; notes?: string | null;
  }) => api.post<FormationRequest>("/formation-requests", body).then((r) => r.data),
  update: (id: string, body: {
    requestDate: string; unit: UnitOfMeasure; groups: FormationGroupInput[];
    rawMessageId?: string | null; notes?: string | null;
  }) => api.put<FormationRequest>(`/formation-requests/${id}`, { id, ...body }).then((r) => r.data),
  submit: (id: string) => api.post<FormationRequest>(`/formation-requests/${id}/submit`).then((r) => r.data),
  approve: (id: string) => api.post<FormationRequest>(`/formation-requests/${id}/approve`).then((r) => r.data),
  reject: (id: string, reason: string) =>
    api.post<FormationRequest>(`/formation-requests/${id}/reject`, { reason }).then((r) => r.data),
  cancel: (id: string, reason: string) =>
    api.post<FormationRequest>(`/formation-requests/${id}/cancel`, { reason }).then((r) => r.data),
  convertToJobOrder: (id: string, body: {
    groupId?: string | null; jobOrderType: JobOrderType; priority?: ProductionPriority;
    orderDate?: string; color?: string | null; notes?: string | null; customerReference?: string | null;
  }) => api.post<FormationRequest>(`/formation-requests/${id}/convert-to-job-order`, body).then((r) => r.data),
  exportExcel: (params?: { customerId?: string; status?: FormationRequestStatus }) => {
    const query = new URLSearchParams();
    if (params?.customerId) query.set("customerId", params.customerId);
    if (params?.status) query.set("status", params.status);
    return downloadFile(`/formation-requests/export/excel?${query.toString()}`, "formation-requests.xlsx");
  },
  downloadPdf: (id: string, requestNumber: string) =>
    downloadFile(`/formation-requests/${id}/pdf`, `formation-request-${requestNumber}.pdf`)
};

export const FormationSpecificationsApi = {
  list: (params?: { activeOnly?: boolean; search?: string }) =>
    api.get<FormationSpecTemplate[]>("/formation-specifications", { params }).then((r) => r.data),
  create: (body: {
    code: string; nameAr: string; nameEn: string;
    widthCm?: number | null; metersPerKg?: number | null; gsm?: number | null;
    tubFormat?: string | null; windingTapeFormat?: string | null; notes?: string | null;
    qualityInstructions?: string | null; labInstructions?: string | null;
    internalInstructions?: string | null; customerInstructions?: string | null;
  }) => api.post<FormationSpecTemplate>("/formation-specifications", body).then((r) => r.data),
  update: (id: string, body: Partial<FormationSpecTemplate>) =>
    api.put<FormationSpecTemplate>(`/formation-specifications/${id}`, { id, ...body }).then((r) => r.data),
  setActive: (id: string, isActive: boolean) =>
    api.post<FormationSpecTemplate>(`/formation-specifications/${id}/active`, { isActive }).then((r) => r.data)
};

// ---------------- Checks register (spec sections 37-40) ----------------

export type CheckDirection = "CustomerCheck" | "SupplierCheck";
export type CheckStatus =
  | "Received" | "InHand" | "Deposited" | "Endorsed" | "Cleared" | "Bounced" | "Cancelled";
export type CheckHolderType = "Customer" | "Company" | "Supplier" | "Bank";
export type CheckMovementType =
  | "Received" | "Issued" | "ReturnedToHolder" | "Endorsed" | "Deposited" | "Cleared" | "Bounced" | "Cancelled";

export interface CheckMovement {
  id: string;
  movementType: CheckMovementType;
  fromHolder: string;
  toHolder: string;
  toHolderType: CheckHolderType;
  movementDate: string;
  reason: string | null;
  supplierId: string | null;
  supplierName: string | null;
  treasuryAccountId: string | null;
  treasuryAccountName: string | null;
  notes: string | null;
  createdBy: string;
  createdAtUtc: string;
}

export interface Check {
  id: string;
  checkNumber: string;
  direction: CheckDirection;
  status: CheckStatus;
  bankName: string;
  branchName: string | null;
  amount: number;
  currency: string;
  issueDate: string;
  dueDate: string;
  issuer: string;
  originalHolder: string;
  currentHolder: string;
  currentHolderType: CheckHolderType;
  customerId: string | null;
  customerCode: string | null;
  customerName: string | null;
  supplierId: string | null;
  supplierCode: string | null;
  supplierName: string | null;
  treasuryAccountId: string | null;
  treasuryAccountName: string | null;
  customerReference: string | null;
  notes: string | null;
  receivedAtUtc: string | null;
  depositedAtUtc: string | null;
  clearedAtUtc: string | null;
  bouncedAtUtc: string | null;
  bounceReason: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  daysToDueDate: number | null;
  createdBy: string;
  createdAtUtc: string;
  movements: CheckMovement[];
}

export interface CheckRegisterSummary {
  totalChecks: number;
  totalAmount: number;
  customerChecksAmount: number;
  supplierChecksAmount: number;
  inHandAmount: number;
  depositedAmount: number;
  endorsedAmount: number;
  clearedAmount: number;
  bouncedAmount: number;
  dueSoonCount: number;
  dueSoonAmount: number;
  overdueCount: number;
  overdueAmount: number;
  byStatus: { status: CheckStatus; count: number; amount: number }[];
}

export const ChecksApi = {
  list: (params?: {
    direction?: CheckDirection; status?: CheckStatus; customerId?: string; supplierId?: string;
    from?: string; to?: string; dueBefore?: string; overdueOnly?: boolean; search?: string;
  }) => api.get<Check[]>("/checks", { params }).then((r) => r.data),
  get: (id: string) => api.get<Check>(`/checks/${id}`).then((r) => r.data),
  summary: (params?: { from?: string; to?: string }) =>
    api.get<CheckRegisterSummary>("/checks/summary", { params }).then((r) => r.data),
  registerCustomerCheck: (body: {
    checkNumber: string; bankName: string; amount: number; issueDate: string; dueDate: string;
    issuer: string; customerId: string; currency?: string; branchName?: string;
    customerReference?: string; notes?: string; confirmInHandNow?: boolean; receivedDate?: string;
  }) => api.post<Check>("/checks/customer-checks", body).then((r) => r.data),
  registerSupplierCheck: (body: {
    checkNumber: string; bankName: string; amount: number; issueDate: string; dueDate: string;
    issuer: string; supplierId: string; treasuryAccountId?: string; currency?: string;
    branchName?: string; notes?: string; handOverNow?: boolean; handOverDate?: string; reason?: string;
  }) => api.post<Check>("/checks/supplier-checks", body).then((r) => r.data),
  confirmReceipt: (id: string, movementDate?: string) =>
    api.post<Check>(`/checks/${id}/confirm-receipt`, { movementDate }).then((r) => r.data),
  endorse: (id: string, body: { supplierId: string; movementDate?: string; reason?: string }) =>
    api.post<Check>(`/checks/${id}/endorse`, body).then((r) => r.data),
  deposit: (id: string, body: { treasuryAccountId: string; movementDate?: string; notes?: string }) =>
    api.post<Check>(`/checks/${id}/deposit`, body).then((r) => r.data),
  clear: (id: string, body?: { movementDate?: string; notes?: string; treasuryAccountId?: string }) =>
    api.post<Check>(`/checks/${id}/clear`, body ?? {}).then((r) => r.data),
  bounce: (id: string, reason: string, movementDate?: string) =>
    api.post<Check>(`/checks/${id}/bounce`, { reason, movementDate }).then((r) => r.data),
  returnToCompany: (id: string, reason?: string) =>
    api.post<Check>(`/checks/${id}/return`, { reason }).then((r) => r.data),
  cancel: (id: string, reason: string) =>
    api.post<Check>(`/checks/${id}/cancel`, { reason }).then((r) => r.data),
  exportExcel: (params?: { direction?: CheckDirection; status?: CheckStatus; overdueOnly?: boolean }) => {
    const query = new URLSearchParams();
    if (params?.direction) query.set("direction", params.direction);
    if (params?.status) query.set("status", params.status);
    if (params?.overdueOnly) query.set("overdueOnly", "true");
    return downloadFile(`/checks/export/excel?${query.toString()}`, "checks.xlsx");
  },
  exportPdf: (params?: { direction?: CheckDirection; status?: CheckStatus; overdueOnly?: boolean }) => {
    const query = new URLSearchParams();
    if (params?.direction) query.set("direction", params.direction);
    if (params?.status) query.set("status", params.status);
    if (params?.overdueOnly) query.set("overdueOnly", "true");
    return downloadFile(`/checks/export/pdf?${query.toString()}`, "checks.pdf");
  }
};

// ---------------- Suppliers (spec section 35) ----------------

export interface Supplier {
  id: string;
  code: string;
  name: string;
  nameAr: string;
  nameEn: string;
  accountNumber: string;
  phone: string | null;
  address: string | null;
  contactPerson: string | null;
  taxNumber: string | null;
  isActive: boolean;
}

export const SuppliersApi = {
  list: (params?: { activeOnly?: boolean; search?: string }) =>
    api.get<Supplier[]>("/suppliers", { params }).then((r) => r.data),
  create: (body: {
    code: string; name: string; nameAr?: string; nameEn?: string; accountNumber?: string;
    phone?: string; address?: string; contactPerson?: string; taxNumber?: string;
  }) => api.post<Supplier>("/suppliers", body).then((r) => r.data),
  update: (id: string, body: Partial<Supplier> & { isActive?: boolean }) =>
    api.put<Supplier>(`/suppliers/${id}`, { id, ...body }).then((r) => r.data)
};

// ---------------- Purchases (spec section 35) ----------------

export type PurchaseOrderStatus = "Draft" | "Submitted" | "Approved" | "PartiallyReceived" | "Received" | "Cancelled";
export type SupplierInvoiceStatus = "Draft" | "Posted" | "Cancelled";

export interface PurchaseOrderLine {
  id: string;
  materialId: string;
  materialCode: string;
  materialName: string;
  quantity: number;
  unit: MaterialUnit;
  unitPrice: number;
  lineValue: number;
  receivedQuantity: number;
  outstandingQuantity: number;
  notes: string | null;
}

export interface PurchaseOrder {
  id: string;
  orderNumber: string;
  orderDate: string;
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  warehouseId: string;
  warehouseName: string;
  expectedDeliveryDate: string | null;
  notes: string | null;
  status: PurchaseOrderStatus;
  totalValue: number;
  receivedValue: number;
  submittedBy: string | null;
  approvedBy: string | null;
  cancelledBy: string | null;
  cancellationReason: string | null;
  createdAtUtc: string;
  createdBy: string;
  lines: PurchaseOrderLine[];
}

export interface PurchaseReceiptLine {
  id: string;
  materialId: string;
  materialCode: string;
  materialName: string;
  quantity: number;
  unit: MaterialUnit;
  unitCost: number;
  lineValue: number;
  notes: string | null;
}

export interface PurchaseReceipt {
  id: string;
  receiptNumber: string;
  receiptDate: string;
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  warehouseId: string;
  warehouseName: string;
  purchaseOrderId: string | null;
  orderNumber: string | null;
  receivedBy: string;
  supplierDocumentNumber: string | null;
  notes: string | null;
  totalValue: number;
  createdAtUtc: string;
  createdBy: string;
  lines: PurchaseReceiptLine[];
}

export interface SupplierInvoiceLine {
  id: string;
  materialId: string | null;
  materialCode: string | null;
  description: string;
  quantity: number;
  unit: MaterialUnit;
  unitPrice: number;
  lineValue: number;
}

export interface SupplierInvoice {
  id: string;
  invoiceNumber: string;
  internalNumber: string | null;
  invoiceDate: string;
  dueDate: string;
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  purchaseOrderId: string | null;
  orderNumber: string | null;
  purchaseReceiptId: string | null;
  receiptNumber: string | null;
  subTotal: number;
  discount: number;
  tax: number;
  total: number;
  currency: string;
  status: SupplierInvoiceStatus;
  notes: string | null;
  postedBy: string | null;
  postedAtUtc: string | null;
  cancellationReason: string | null;
  createdAtUtc: string;
  createdBy: string;
  lines: SupplierInvoiceLine[];
}

export interface SupplierPayment {
  id: string;
  paymentNumber: string;
  paymentDate: string;
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  treasuryAccountId: string;
  treasuryAccountName: string;
  amount: number;
  currency: string;
  paymentMethod: string | null;
  checkId: string | null;
  checkNumber: string | null;
  supplierInvoiceId: string | null;
  description: string | null;
  createdBy: string;
  createdAtUtc: string;
}

export interface SupplierLedgerEntry {
  id: string;
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  entryDate: string;
  sourceDocumentType: string;
  sourceDocumentNumber: string;
  sourceDocumentId: string;
  debit: number;
  credit: number;
  description: string;
  createdBy: string;
  createdAtUtc: string;
  runningBalance: number;
}

export interface SupplierBalance {
  supplierId: string;
  supplierCode: string;
  supplierName: string;
  currency: string;
  totalInvoiced: number;
  totalPaid: number;
  outstanding: number;
  openInvoiceCount: number;
  overdueAmount: number;
}

export const PurchasesApi = {
  orders: (params?: { supplierId?: string; status?: PurchaseOrderStatus; from?: string; to?: string }) =>
    api.get<PurchaseOrder[]>("/purchases/orders", { params }).then((r) => r.data),
  order: (id: string) => api.get<PurchaseOrder>(`/purchases/orders/${id}`).then((r) => r.data),
  createOrder: (body: {
    orderDate: string;
    supplierId: string;
    warehouseId: string;
    expectedDeliveryDate?: string;
    notes?: string;
    lines?: { materialId: string; quantity: number; unit: MaterialUnit; unitPrice: number; notes?: string }[];
  }) => api.post<PurchaseOrder>("/purchases/orders", body).then((r) => r.data),
  addOrderLine: (id: string, body: { materialId: string; quantity: number; unit: MaterialUnit; unitPrice: number; notes?: string }) =>
    api.post<PurchaseOrder>(`/purchases/orders/${id}/lines`, { orderId: id, ...body }).then((r) => r.data),
  removeOrderLine: (id: string, lineId: string) =>
    api.delete<PurchaseOrder>(`/purchases/orders/${id}/lines/${lineId}`).then((r) => r.data),
  submitOrder: (id: string) => api.post<PurchaseOrder>(`/purchases/orders/${id}/submit`).then((r) => r.data),
  approveOrder: (id: string) => api.post<PurchaseOrder>(`/purchases/orders/${id}/approve`).then((r) => r.data),
  cancelOrder: (id: string, reason: string) =>
    api.post<PurchaseOrder>(`/purchases/orders/${id}/cancel`, { reason }).then((r) => r.data),

  receipts: (params?: { supplierId?: string; purchaseOrderId?: string }) =>
    api.get<PurchaseReceipt[]>("/purchases/receipts", { params }).then((r) => r.data),
  createReceipt: (body: {
    receiptDate: string;
    supplierId: string;
    warehouseId: string;
    purchaseOrderId?: string;
    supplierDocumentNumber?: string;
    notes?: string;
    lines: { materialId: string; quantity: number; unit: MaterialUnit; unitCost: number; purchaseOrderLineId?: string }[];
  }) => api.post<PurchaseReceipt>("/purchases/receipts", body).then((r) => r.data),

  invoices: (params?: { supplierId?: string; status?: SupplierInvoiceStatus; overdueOnly?: boolean }) =>
    api.get<SupplierInvoice[]>("/purchases/supplier-invoices", { params }).then((r) => r.data),
  createInvoice: (body: {
    invoiceNumber: string;
    invoiceDate: string;
    dueDate: string;
    supplierId: string;
    purchaseOrderId?: string;
    purchaseReceiptId?: string;
    internalNumber?: string;
    notes?: string;
    currency?: string;
    discount?: number;
    tax?: number;
    lines?: { materialId?: string; description: string; quantity: number; unit: MaterialUnit; unitPrice: number }[];
  }) => api.post<SupplierInvoice>("/purchases/supplier-invoices", body).then((r) => r.data),
  postInvoice: (id: string) =>
    api.post<SupplierInvoice>(`/purchases/supplier-invoices/${id}/post`).then((r) => r.data),
  cancelInvoice: (id: string, reason: string) =>
    api.post<SupplierInvoice>(`/purchases/supplier-invoices/${id}/cancel`, { reason }).then((r) => r.data),

  payments: (params?: { supplierId?: string }) =>
    api.get<SupplierPayment[]>("/purchases/supplier-payments", { params }).then((r) => r.data),
  paySupplier: (body: {
    paymentDate: string;
    supplierId: string;
    treasuryAccountId: string;
    amount: number;
    paymentMethod?: string;
    checkId?: string;
    supplierInvoiceId?: string;
    description?: string;
  }) => api.post<SupplierPayment>("/purchases/supplier-payments", body).then((r) => r.data),

  ledger: (supplierId: string, params?: { from?: string; to?: string }) =>
    api.get<SupplierLedgerEntry[]>(`/purchases/suppliers/${supplierId}/ledger`, { params }).then((r) => r.data),
  balances: (params?: { supplierId?: string; withBalanceOnly?: boolean }) =>
    api.get<SupplierBalance[]>("/purchases/supplier-balances", { params }).then((r) => r.data)
};

// ---------------- Payroll & wages (spec section 36) ----------------

export type EmployeeStatus = "Active" | "Suspended" | "Terminated";
export type PayrollRunStatus = "Draft" | "Approved" | "Posted" | "Cancelled";

export interface Department {
  id: string;
  code: string;
  name: string;
  nameAr: string;
  nameEn: string;
  notes: string | null;
  isActive: boolean;
  employeeCount: number;
}

export interface Employee {
  id: string;
  code: string;
  name: string;
  nameAr: string;
  nameEn: string;
  departmentId: string;
  departmentCode: string;
  departmentName: string;
  jobTitle: string | null;
  basicSalary: number;
  status: EmployeeStatus;
  hireDate: string;
  terminationDate: string | null;
  phone: string | null;
  nationalId: string | null;
  bankAccountNumber: string | null;
  notes: string | null;
}

export interface PayrollRunLine {
  id: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  departmentId: string;
  departmentName: string | null;
  basicSalary: number;
  allowances: number;
  deductions: number;
  grossPay: number;
  netPay: number;
  notes: string | null;
}

export interface PayrollRun {
  id: string;
  runNumber: string;
  periodYear: number;
  periodMonth: number;
  treasuryAccountId: string | null;
  treasuryAccountName: string | null;
  notes: string | null;
  status: PayrollRunStatus;
  totalGross: number;
  totalDeductions: number;
  totalNet: number;
  employeeCount: number;
  approvedBy: string | null;
  postedBy: string | null;
  cancellationReason: string | null;
  createdBy: string;
  createdAtUtc: string;
  lines: PayrollRunLine[];
}

export const PayrollApi = {
  departments: (params?: { activeOnly?: boolean; search?: string }) =>
    api.get<Department[]>("/payroll/departments", { params }).then((r) => r.data),
  createDepartment: (body: { code: string; nameAr?: string; nameEn?: string; notes?: string }) =>
    api.post<Department>("/payroll/departments", body).then((r) => r.data),
  updateDepartment: (id: string, body: { nameAr?: string; nameEn?: string; notes?: string; isActive?: boolean }) =>
    api.put<Department>(`/payroll/departments/${id}`, { id, ...body }).then((r) => r.data),

  employees: (params?: { departmentId?: string; status?: EmployeeStatus; activeOnly?: boolean; search?: string }) =>
    api.get<Employee[]>("/payroll/employees", { params }).then((r) => r.data),
  createEmployee: (body: {
    code: string;
    departmentId: string;
    basicSalary: number;
    hireDate: string;
    nameAr?: string;
    nameEn?: string;
    jobTitle?: string;
    phone?: string;
    nationalId?: string;
    bankAccountNumber?: string;
    notes?: string;
  }) => api.post<Employee>("/payroll/employees", body).then((r) => r.data),
  updateEmployee: (id: string, body: Partial<Employee> & { status?: EmployeeStatus }) =>
    api.put<Employee>(`/payroll/employees/${id}`, { id, ...body }).then((r) => r.data),

  runs: (params?: { periodYear?: number; status?: PayrollRunStatus }) =>
    api.get<PayrollRun[]>("/payroll/runs", { params }).then((r) => r.data),
  run: (id: string) => api.get<PayrollRun>(`/payroll/runs/${id}`).then((r) => r.data),
  createRun: (body: { periodYear: number; periodMonth: number; treasuryAccountId?: string; notes?: string }) =>
    api.post<PayrollRun>("/payroll/runs", body).then((r) => r.data),
  updateRunLine: (id: string, lineId: string, body: { allowances: number; deductions: number; notes?: string }) =>
    api.put<PayrollRun>(`/payroll/runs/${id}/lines/${lineId}`, { runId: id, lineId, ...body }).then((r) => r.data),
  removeRunLine: (id: string, lineId: string) =>
    api.delete<PayrollRun>(`/payroll/runs/${id}/lines/${lineId}`).then((r) => r.data),
  setRunAccount: (id: string, treasuryAccountId: string | null) =>
    api.put<PayrollRun>(`/payroll/runs/${id}/account`, { treasuryAccountId }).then((r) => r.data),
  approveRun: (id: string) => api.post<PayrollRun>(`/payroll/runs/${id}/approve`).then((r) => r.data),
  postRun: (id: string) => api.post<PayrollRun>(`/payroll/runs/${id}/post`).then((r) => r.data),
  cancelRun: (id: string, reason: string) =>
    api.post<PayrollRun>(`/payroll/runs/${id}/cancel`, { reason }).then((r) => r.data)
};

