/**
 * The scan flow returns to the scanned URL after sign-in, but the `returnUrl`
 * query parameter is attacker-controlled - these tests pin the rule that only
 * a same-origin application path is ever accepted.
 */
import { describe, expect, it } from "vitest";
import { loginHref, resolveReturnUrl } from "./returnUrl";

describe("resolveReturnUrl", () => {
  it("accepts the scan URL the QR code opened", () => {
    expect(resolveReturnUrl("/scan/production-order/3fa85f64-5717-4562-b3fc-2c963f66afa6")).toBe(
      "/scan/production-order/3fa85f64-5717-4562-b3fc-2c963f66afa6"
    );
  });

  it("accepts an application path with its own query string", () => {
    expect(resolveReturnUrl("/production-orders?page=2")).toBe("/production-orders?page=2");
  });

  it("falls back to the dashboard for a missing or empty value", () => {
    expect(resolveReturnUrl(null)).toBe("/");
    expect(resolveReturnUrl(undefined)).toBe("/");
    expect(resolveReturnUrl("")).toBe("/");
  });

  it("rejects an absolute external URL", () => {
    expect(resolveReturnUrl("https://evil.example/phish")).toBe("/");
    expect(resolveReturnUrl("http://evil.example/")).toBe("/");
  });

  it("rejects a protocol-relative URL", () => {
    expect(resolveReturnUrl("//evil.example/phish")).toBe("/");
  });

  it("rejects a javascript: URL", () => {
    expect(resolveReturnUrl("javascript:alert(1)")).toBe("/");
  });

  it("rejects backslash tricks", () => {
    expect(resolveReturnUrl("/\\/evil.example")).toBe("/");
    expect(resolveReturnUrl("\\/evil.example")).toBe("/");
  });

  it("rejects an encoded external URL", () => {
    expect(resolveReturnUrl(encodeURIComponent("https://evil.example/phish"))).toBe("/");
  });

  it("keeps a query value that merely mentions a scheme - navigation stays same-origin", () => {
    expect(resolveReturnUrl("/scan/x?next=javascript:alert(1)")).toBe("/scan/x?next=javascript:alert(1)");
  });
});

describe("loginHref", () => {
  it("points at the login page with the encoded target", () => {
    expect(loginHref("/scan/production-order/abc")).toBe(
      "/login?returnUrl=%2Fscan%2Fproduction-order%2Fabc"
    );
  });
});
