/**
 * English labels for the unified warehouse screen (dropdown entry + its sections).
 *
 * Kept in its own module so the main dictionary stays readable: the screen passes its Arabic wording as
 * the `t()` fallback, and these keys give the same labels in English. Arabic therefore needs no entry
 * here, and no untranslated key can leak onto the page.
 */
export const warehouseEn: Record<string, string> = {
  // What a request that FAILED says, everywhere it is rendered. The screen passes its Arabic wording
  // as the t() fallback, so only the English side is needed here.
  "req.failed.title": "Could not load the data",
  "req.failed.network": "The server could not be reached. Check the connection and try again.",
  "req.failed.session": "Your session ended or your permissions changed. Sign in again.",
  "req.failed.forbidden": "You do not have permission to view this data.",
  "req.failed.missing": "The requested record does not exist - it may have been deleted, or the link is wrong.",
  "req.failed.server": "The server could not complete the request. Try again, and check the server log if it keeps happening.",
  "req.failed.generic": "The request failed. Try again.",
  "req.failed.notEmpty": "This is not an empty list: the request itself failed, so nothing can be confirmed either way.",
  "req.failed.row": "Could not load the data - this is not an empty list",
  "req.failed.retry": "Try again",
  "wh.loadFailed": "Could not load the warehouses",
  "wh.movements.failed": "Could not load this warehouse's movements",
  "stock.loadFailed": "Could not load the balances and their movements",
  "wh.choose": "Choose a warehouse",
  "wh.chooseHint": "Every balance, movement, item card and report below belongs to this warehouse alone, read from its own ledger.",
  "wh.scopeLabel": "Ledger",
  "wh.ledgerMaterials": "Materials and supplies ledger",
  "wh.ledgerFabric": "Fabric ledger (raw / ready)",
  "wh.noStockWarehouse": "No stock warehouse is defined: define the raw, ready, materials or supplies warehouse first.",
  "wh.section.overview": "Overview",
  "wh.section.itemCard": "Item card",
  "wh.section.documents": "Documents: receipts / issues / adjustments",
  "wh.kpi.rows": "Balances shown",
  "wh.kpi.movements": "Movements in this warehouse",
  "wh.kpi.balance": "Total balance",
  "wh.kpi.balanceKg": "Total balance (KG)",
  "wh.kpi.balanceMeter": "Total balance (meter)",
  "wh.kpi.unit": "Unit",
  "wh.overviewHint": "The balance here is the sum of this warehouse's ledger rows - no balance is stored, so it cannot be stale.",
  "wh.documentHint": "Message / job order / issue note / adjustment",
  "wh.itemCardHint": "Opening balance, the period's movements and closing balance, from this warehouse's ledger only - KG and meter each in its own column.",
  "wh.itemCardScoped": "This card is for {{w}} only",
  "wh.itemCardPick": "Choose an item to see the warehouse card",
  "wh.itemCardError": "The card could not be built - check the warehouse and the item you chose.",
  "wh.itemCard.opening": "Opening balance",
  "wh.itemCard.in": "Received in period",
  "wh.itemCard.out": "Issued in period",
  "wh.itemCard.closing": "Closing balance",
  "wh.chooseItem": "Choose an item",
  "wh.doc.receipts": "Receipts",
  "wh.doc.issues": "Issues",
  "wh.doc.releases": "External release",
  "wh.doc.adjustments": "Stock counts / adjustments",
  "wh.doc.produced": "Ready-goods transfer",
  "wh.doc.delivered": "Deliveries",
  "wh.reportsHint": "Every report carries the same figures as this screen for the same warehouse - nothing is recomputed and no two warehouses are merged into one number.",
  "wh.report.balancesExcel": "This warehouse's balances (Excel)",
  "wh.report.balancesPdf": "This warehouse's balances (PDF)",
  "wh.report.warehouseBalancesExcel": "Warehouse balances report (Excel)",
  "wh.report.warehouseBalancesPdf": "Warehouse balances report (PDF)",
  "wh.report.scopeNote": "The warehouse balances report lists every warehouse in its own rows with a total for each, then a separate grand total per ledger and unit."
};
