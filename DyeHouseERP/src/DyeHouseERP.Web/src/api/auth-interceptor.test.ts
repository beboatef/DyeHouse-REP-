/**
 * Logout policy of the axios session interceptor (client.ts):
 * - the user is thrown to /login ONLY when the server explicitly rejects the session
 *   (the refresh call answers 401/403);
 * - a refresh that never gets an answer (network error, timeout, 5xx, ...) keeps the user
 *   signed in and surfaces the failure to the caller instead.
 */
import { beforeEach, describe, expect, it } from "vitest";
import MockAdapter from "axios-mock-adapter";
import { api } from "./client";

const USER_KEY = "dyehouse_user";

function seedSignedInUser(): void {
  window.localStorage.setItem(
    USER_KEY,
    JSON.stringify({ username: "u", displayName: "U", roles: [], expiresAtUtc: "2030-01-01T00:00:00Z" })
  );
}

function installBrowserStubs(): void {
  // vitest runs in a node environment, so the browser globals the interceptor touches
  // (bare `window` / `localStorage` / `document`) are stubbed on globalThis per test.
  const store = new Map<string, string>();
  const localStorageStub = {
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
    clear: () => store.clear()
  };
  const g = globalThis as Record<string, unknown>;
  Object.defineProperty(g, "window", {
    value: { location: { pathname: "/dashboard", href: "" }, localStorage: localStorageStub },
    configurable: true,
    writable: true
  });
  Object.defineProperty(g, "localStorage", {
    value: localStorageStub,
    configurable: true,
    writable: true
  });
  Object.defineProperty(g, "document", {
    value: { cookie: "" },
    configurable: true,
    writable: true
  });
}

describe("session interceptor logout policy", () => {
  let mock: MockAdapter;

  beforeEach(() => {
    installBrowserStubs();
    mock = new MockAdapter(api);
    seedSignedInUser();
  });

  it("stays signed in when the refresh fails with a network error", async () => {
    mock.onGet("/protected").replyOnce(401);
    mock.onPost("/auth/refresh").networkErrorOnce();

    await expect(api.get("/protected")).rejects.toThrow();

    expect(window.location.href).not.toBe("/login");
    expect(window.localStorage.getItem(USER_KEY)).not.toBeNull();
  });

  it("stays signed in when the refresh fails with a 500", async () => {
    mock.onGet("/protected").replyOnce(401);
    mock.onPost("/auth/refresh").replyOnce(500);

    await expect(api.get("/protected")).rejects.toThrow();

    expect(window.location.href).not.toBe("/login");
    expect(window.localStorage.getItem(USER_KEY)).not.toBeNull();
  });

  it("stays signed in when the original request itself fails with a network error", async () => {
    mock.onGet("/protected").networkErrorOnce();

    await expect(api.get("/protected")).rejects.toThrow();

    expect(window.location.href).not.toBe("/login");
    expect(window.localStorage.getItem(USER_KEY)).not.toBeNull();
  });

  it("logs out when the refresh is rejected with 401", async () => {
    mock.onGet("/protected").replyOnce(401);
    mock.onPost("/auth/refresh").replyOnce(401);

    await expect(api.get("/protected")).rejects.toThrow();

    expect(window.localStorage.getItem(USER_KEY)).toBeNull();
    expect(window.location.href).toBe("/login");
  });

  it("logs out when the refresh is rejected with 403", async () => {
    mock.onGet("/protected").replyOnce(401);
    mock.onPost("/auth/refresh").replyOnce(403);

    await expect(api.get("/protected")).rejects.toThrow();

    expect(window.localStorage.getItem(USER_KEY)).toBeNull();
    expect(window.location.href).toBe("/login");
  });

  it("replays the request without logging out when the refresh succeeds", async () => {
    mock.onGet("/protected").replyOnce(401);
    mock.onGet("/protected").replyOnce(200, { ok: true });
    mock.onPost("/auth/refresh").replyOnce(200, {
      token: "",
      expiresAtUtc: "2030-01-01T00:00:00Z",
      username: "u",
      displayName: "U",
      roles: []
    });

    const res = await api.get("/protected");

    expect(res.data).toEqual({ ok: true });
    expect(window.location.href).not.toBe("/login");
    expect(window.localStorage.getItem(USER_KEY)).not.toBeNull();
  });
});
