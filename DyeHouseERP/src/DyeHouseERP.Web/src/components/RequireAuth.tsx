import { Navigate, Outlet, useLocation } from "react-router-dom";
import { loginHref } from "@/utils/returnUrl";

export default function RequireAuth() {
  const location = useLocation();
  // The token is an HttpOnly cookie, so there is nothing here to read. The cached profile is only
  // the client-side hint that a session exists - it is not the security boundary: the API answers
  // 401 for a missing or expired cookie and the axios interceptor then sends the user to /login.
  const signedIn = Boolean(localStorage.getItem("dyehouse_user"));
  // A signed-out deep link (a scanned QR code, a bookmarked page) comes back here after sign-in:
  // the target is validated inside loginHref/resolveReturnUrl, so only a same-origin
  // application path survives and a crafted link can never bounce the user off-site.
  if (!signedIn) return <Navigate to={loginHref(location.pathname + location.search)} replace />;
  return <Outlet />;
}
