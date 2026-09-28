import { Link } from "react-router-dom";
import { FileSpreadsheet, FileText, Printer } from "lucide-react";
import { Button } from "./ui";

/**
 * The standard Excel / PDF / Print action group (spec sections 38 + 39).
 *
 * Every button here calls a real endpoint through `api/exports.ts`. A button
 * is only rendered when the caller passes a real action, so a screen can never
 * show an export control that quietly does nothing.
 */
export function ExportButtons({
  excel, pdf, printTo, extra
}: {
  /** Absolute API path, e.g. "/suppliers/export?format=excel". */
  excel?: { label?: string; action: () => void | Promise<void> };
  pdf?: { label?: string; action: () => void | Promise<void> };
  /** In-app print-preview route, e.g. "/print/invoice/<id>". */
  printTo?: { label?: string; to: string };
  extra?: React.ReactNode;
}) {
  if (!excel && !pdf && !printTo && !extra) return null;

  return (
    <div className="flex flex-wrap items-center gap-2">
      {excel && (
        <Button variant="secondary" type="button" onClick={() => void excel.action()}>
          <FileSpreadsheet size={15} /> {excel.label ?? "تصدير Excel"}
        </Button>
      )}
      {pdf && (
        <Button variant="secondary" type="button" onClick={() => void pdf.action()}>
          <FileText size={15} /> {pdf.label ?? "تصدير PDF"}
        </Button>
      )}
      {printTo && (
        <Link to={printTo.to} target="_blank" rel="noreferrer">
          <Button variant="ghost" type="button">
            <Printer size={15} /> {printTo.label ?? "معاينة قبل الطباعة"}
          </Button>
        </Link>
      )}
      {extra}
    </div>
  );
}

/** The same pair for a single document row (a table cell rather than a toolbar). */
export function DocumentActions({
  pdf, printTo
}: {
  pdf?: () => void | Promise<void>;
  printTo?: string;
}) {
  if (!pdf && !printTo) return null;
  return (
    <div className="flex items-center gap-1">
      {pdf && (
        <Button variant="ghost" type="button" title="PDF" onClick={() => void pdf()}>
          <FileText size={15} />
        </Button>
      )}
      {printTo && (
        <Link to={printTo} target="_blank" rel="noreferrer">
          <Button variant="ghost" type="button" title="طباعة">
            <Printer size={15} />
          </Button>
        </Link>
      )}
    </div>
  );
}
