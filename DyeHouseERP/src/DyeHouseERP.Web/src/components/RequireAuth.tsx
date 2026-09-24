import { Navigate, Outlet } from "react-router-dom";

export default function RequireAuth() {
  const token = localStorage.getItem("dyehouse_token");
  return token ? <Outlet /> : <Navigate to="/login" replace />;
}
