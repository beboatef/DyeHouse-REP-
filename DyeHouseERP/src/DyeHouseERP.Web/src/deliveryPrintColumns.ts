/**
 * The printable QUANTITY columns of a delivery permit (إذن تسليم) - KG, Meter and the roll count.
 *
 * One source of truth, because two surfaces must agree on exactly the same choice: the in-app print
 * preview (`pages/print/DeliveryPrintPage`) and the server-generated PDF the register downloads. The
 * choice:
 *   * affects the PRINT-OUT ONLY - it never edits or deletes a recorded quantity, and every figure
 *     stays stored exactly as it was entered;
 *   * never touches the identifying data (company identity, recipient, job order, item and colour),
 *     which always prints;
 *   * is per document format: a sub-customer permit's format is stored separately from an ordinary
 *     permit's, so setting up one never changes the other;
 *   * is remembered like the language and theme switches (localStorage), defaulting to all columns.
 */

export type DeliveryQuantityColumns = { kg: boolean; meter: boolean; rolls: boolean };

/** Everything prints by default - hiding a column is an explicit act. */
export const ALL_DELIVERY_QUANTITY_COLUMNS: DeliveryQuantityColumns = { kg: true, meter: true, rolls: true };

/** A sub-customer permit keeps its own print format, independent of every other document. */
export const deliveryColumnsStorageKey = (isSubCustomerPermit: boolean) =>
  isSubCustomerPermit ? "dyehouse_delivery_print_columns.sub" : "dyehouse_delivery_print_columns";

/** Reads the remembered choice, falling back to the defaults when nothing is stored (or is corrupt). */
export function readDeliveryQuantityColumns(storageKey: string): DeliveryQuantityColumns {
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return ALL_DELIVERY_QUANTITY_COLUMNS;
    const parsed = JSON.parse(raw) as Partial<DeliveryQuantityColumns>;
    return {
      kg: typeof parsed.kg === "boolean" ? parsed.kg : ALL_DELIVERY_QUANTITY_COLUMNS.kg,
      meter: typeof parsed.meter === "boolean" ? parsed.meter : ALL_DELIVERY_QUANTITY_COLUMNS.meter,
      rolls: typeof parsed.rolls === "boolean" ? parsed.rolls : ALL_DELIVERY_QUANTITY_COLUMNS.rolls
    };
  } catch {
    // Private mode or a corrupt value - the defaults still produce a complete document.
    return ALL_DELIVERY_QUANTITY_COLUMNS;
  }
}

export function saveDeliveryQuantityColumns(storageKey: string, columns: DeliveryQuantityColumns): void {
  try {
    localStorage.setItem(storageKey, JSON.stringify(columns));
  } catch {
    // Private mode - the choice still applies for this session.
  }
}
