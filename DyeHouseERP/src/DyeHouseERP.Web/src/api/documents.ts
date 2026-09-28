import { api, type ProductionOrderCost, type PurchaseOrder } from "./client";

/**
 * Draft-document *editing* endpoints.
 *
 * Everything that streams a file (Excel / PDF exports, print documents,
 * statements) now lives in `api/exports.ts`, so there is exactly one place
 * that knows how to fetch a binary response. What stays here is the half that
 * is genuinely about document editing - the draft header/line mutations that
 * the API refuses once a document has been received or posted.
 */

export const PurchaseDocumentsApi = {
  /** Header edit - the API only accepts this while the order is editable. */
  updateOrder: (id: string, body: {
    orderDate: string; supplierId: string; warehouseId: string;
    expectedDeliveryDate?: string | null; notes?: string | null;
  }) => api.put<PurchaseOrder>(`/purchases/orders/${id}`, { id, ...body }).then((r) => r.data),

  /** Line edit - refused by the API once the line has been received. */
  updateOrderLine: (id: string, lineId: string, body: { quantity: number; unitPrice: number; notes?: string | null }) =>
    api.put<PurchaseOrder>(`/purchases/orders/${id}/lines/${lineId}`, { orderId: id, lineId, ...body }).then((r) => r.data)
};

export const CostingApi = {
  /** Estimated cost - advisory and revisable while the order is open. */
  setEstimate: (productionOrderId: string, body: { estimatedCost: number | null; costingNotes?: string }) =>
    api.put<ProductionOrderCost>(`/production-orders/${productionOrderId}/cost/estimate`, body).then((r) => r.data),

  /** Approved cost - only for a completed order; records who signed it and when. */
  approve: (productionOrderId: string, body: { approvedCost: number; costingNotes?: string }) =>
    api.post<ProductionOrderCost>(`/production-orders/${productionOrderId}/cost/approve`, body).then((r) => r.data)
};
