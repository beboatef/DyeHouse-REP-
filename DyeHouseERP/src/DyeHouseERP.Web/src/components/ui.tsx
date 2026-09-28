import { PropsWithChildren, forwardRef } from "react";

/**
 * Shared UI primitives.
 *
 * These wrap the component classes in index.css rather than restating padding
 * and radius inline, so a change to the design tokens updates all 39 modules
 * at once. The exported names and props are unchanged from the previous
 * version, so existing pages keep working untouched.
 */

export function PageHeader({
  title,
  subtitle,
  action
}: {
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="mb-5 flex flex-col gap-3 sm:mb-6 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <h1 className="page-title">{title}</h1>
        {subtitle && <p className="page-subtitle">{subtitle}</p>}
      </div>
      {action && <div className="flex shrink-0 flex-wrap items-center gap-2">{action}</div>}
    </div>
  );
}

export function Card({ children, className = "" }: PropsWithChildren<{ className?: string }>) {
  return <div className={`card ${className}`}>{children}</div>;
}

type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "secondary" | "ghost" | "danger";
  size?: "sm" | "md" | "lg";
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { children, variant = "primary", size = "md", className = "", type = "button", ...props },
  ref
) {
  const variantClass = {
    primary: "btn-primary",
    secondary: "btn-secondary",
    ghost: "btn-ghost",
    danger: "btn-danger"
  }[variant];
  const sizeClass = { sm: "btn-sm", md: "", lg: "btn-lg" }[size];

  return (
    <button ref={ref} type={type} {...props} className={`btn ${variantClass} ${sizeClass} ${className}`}>
      {children}
    </button>
  );
});

export type BadgeTone = "neutral" | "success" | "warning" | "danger" | "info";

export function Badge({
  children,
  tone = "neutral"
}: PropsWithChildren<{
  // `gray` is the previous name for the neutral tone; both are accepted so no
  // existing page has to change.
  tone?: BadgeTone | "gray" | "green" | "yellow" | "red" | "blue";
}>) {
  const toneClass: Record<string, string> = {
    neutral: "badge-neutral",
    success: "badge-success",
    warning: "badge-warning",
    danger: "badge-danger",
    info: "badge-info",
    gray: "badge-neutral",
    green: "badge-success",
    yellow: "badge-warning",
    red: "badge-danger",
    blue: "badge-info"
  };
  return <span className={`badge ${toneClass[tone] ?? "badge-neutral"}`}>{children}</span>;
}

export const Input = forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  function Input({ className = "", ...props }, ref) {
    return <input ref={ref} {...props} className={`input ${className}`} />;
  }
);

export const Select = forwardRef<HTMLSelectElement, React.SelectHTMLAttributes<HTMLSelectElement>>(
  function Select({ className = "", children, ...props }, ref) {
    return (
      <select ref={ref} {...props} className={`select ${className}`}>
        {children}
      </select>
    );
  }
);

export const Textarea = forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  function Textarea({ className = "", ...props }, ref) {
    return <textarea ref={ref} {...props} className={`textarea ${className}`} />;
  }
);

/** Label + control + error, so form spacing is identical everywhere. */
export function Field({
  label,
  required,
  error,
  children
}: PropsWithChildren<{ label: string; required?: boolean; error?: string }>) {
  return (
    <div>
      <label className="field-label">
        {label}
        {required && (
          <span className="ms-1 text-danger" aria-hidden="true">
            *
          </span>
        )}
      </label>
      {children}
      {error && <p className="field-error">{error}</p>}
    </div>
  );
}

/** Table shell that scrolls horizontally inside itself, never the page. */
export function TableWrap({ children, className = "" }: PropsWithChildren<{ className?: string }>) {
  return <div className={`table-wrap ${className}`}>{children}</div>;
}
