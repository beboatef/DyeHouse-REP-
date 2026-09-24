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
  baseUnit: UnitOfMeasure;
  isActive: boolean;
}

export interface Warehouse {
  id: string;
  code: string;
  name: string;
  kind: "RawMaterial" | "Materials" | "ReadyGoods";
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
  lines: RawMessageLine[];
}

export const CustomersApi = {
  list: (params?: { activeOnly?: boolean; search?: string }) =>
    api.get<Customer[]>("/customers", { params }).then((r) => r.data),
  create: (body: { code: string; name: string }) =>
    api.post<Customer>("/customers", body).then((r) => r.data)
};

export const ItemsApi = {
  list: (params?: { activeOnly?: boolean; search?: string }) =>
    api.get<Item[]>("/items", { params }).then((r) => r.data),
  create: (body: { code: string; name: string; baseUnit: UnitOfMeasure }) =>
    api.post<Item>("/items", body).then((r) => r.data)
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
  recordInspection: (id: string, body: { result: string; notes?: string }) =>
    api.post(`/raw-messages/${id}/inspection`, body)
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
  orderDate: string;
  status: ProductionOrderStatus;
  reprocessingOfProductionOrderId: string | null;
  rawAllocations: RawAllocationLine[];
  stageExecutions: StageExecution[];
}

export const ProductionOrdersApi = {
  list: (params?: { customerId?: string; status?: ProductionOrderStatus }) =>
    api.get<ProductionOrder[]>("/production-orders", { params }).then((r) => r.data),
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

