import { Navigate, Outlet } from "react-router-dom";

export default function RequireAuth() {
  // The token is an HttpOnly cookie, so there is nothing here to read. The cached profile is only
  // the client-side hint that a session exists - it is not the security boundary: the API answers
  // 401 for a missing or expired cookie and the axios interceptor then sends the user to /login.
  const signedIn = Boolean(localStorage.getItem("dyehouse_user"));
  return signedIn ? <Outlet /> : <Navigate to="/login" replace />;
}
