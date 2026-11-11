# Password and Session Policy — Runbook

How authentication stays boring: every password set flows through one enforcement point
(`PasswordPolicy` + `PasswordChangeService`), every flagged account is gated by one
middleware (`MustChangePasswordMiddleware`), and sessions die by idle timeout measured
on the refresh-token row itself. This page is the operator's view of those rules.

## 1. The rules

| Rule | Value | Where |
|---|---|---|
| Minimum length | 12 characters (matches the bootstrap minimum) | `PasswordPolicy.MinLength` |
| Maximum length | 200 characters (hashing-cost ceiling) | `PasswordPolicy.MaxLength` |
| Username in password | Refused (case-insensitive) | `PasswordPolicy` |
| History | New password must differ from the current one and the last 5 (`PasswordHistory` rows, PBKDF2 hashes only, never plaintext) | `PasswordChangeService` |
| Forced change | Bootstrap admin, every admin-created user, every admin reset, expired passwords | `User.MustChangePassword` + gate middleware |
| Expiry | `PasswordMaxAgeDays`, default 0 = disabled | `CompanySettings`, `PUT /api/settings/password-policy` |
| Idle timeout | `SessionIdleMinutes`, default 30 | `CompanySettings`, enforced in `RefreshSessionCommand` |

PBKDF2 verification and the rehash behaviour are untouched by this part (separate
hardening task): the history comparison uses the same `Verify` path as login.

## 2. The forced-change gate

While `MustChangePassword` is set, the API answers **403 + `PASSWORD_CHANGE_REQUIRED`**
to everything except:

- `POST /api/auth/change-password` — the way out (proves the current password, enforces
  the full policy, clears the flag, revokes every session, writes an audit entry);
- `POST /api/auth/logout`, `GET /api/auth/me` — leave and self-read;
- `POST /api/auth/login`, `POST /api/auth/refresh` — minting the session that performs
  the change;
- `/health/*` — probes carry no identity.

The flag is read **live from the database on every request**, never from the JWT: an
administrator reset takes effect on the very next call even though the user's access
token is still valid. The frontend routes flagged sessions to `/change-password`
(login checks `mustChangePassword`; the API client redirects on the gate's 403).

## 3. Expiry and idle semantics (the two operators always ask about)

- **Expiry flags, never locks out.** When `PasswordMaxAgeDays > 0` and the password is
  older, login still returns 200 with tokens — but `mustChangePassword: true`, and the
  next business call is gated until a change. Passwords of unknown age (legacy rows
  created before this feature) were backfilled with the rollout date by migration
  `AddPasswordPolicy`, so expiry starts applying to them from the deploy, not from "never".
- **Idle is measured on the token row.** Each rotation stamps the new row's
  `LastActivityAtUtc`; a refresh presenting a token idle longer than `SessionIdleMinutes`
  is refused (`IdleTimeout` revocation, 401). No cookie flag, nothing the client can
  replay. Values below 5 minutes fall back to the 30-minute default rather than silently
  disabling the control.

## 4. Reset permission

Password resets are governed by the dedicated `users.reset_password` permission
(`POST /api/users/{id}/change-password`), so the right can be delegated to a helpdesk
role without granting full `users.manage`. Administrators keep working through the
`admin` bypass exactly as before. Deployments with non-admin `users.manage` holders who
used to reset passwords must also grant them `users.reset_password` (see CHANGELOG).

## 5. Incident checklist

1. **User reports "must change password" loop** — they keep landing on
   `/change-password`: the change itself is being refused (usually history reuse or the
   username rule). The 400 body names the rule; do NOT reset their password to the
   development default to "unblock" them — a reset flags them again by design.
2. **Suspected compromise** — reset via the Users page (kills every session immediately),
   then check the audit log for `PasswordChanged`/`PasswordReset` and
   `RefreshTokenRevokedForPasswordChange` events around the incident window.
3. **Mass expiry after enabling `PasswordMaxAgeDays`** — expected on first enable if
   many passwords predate the window. Announce the change first; users keep working
   until their next login, then change once.
4. **Refresh storms after lowering `SessionIdleMinutes`** — clients flash 401s and
   re-login. Raise it back if the shop floor's reality (long batches, shared tablets)
   needs longer sessions; idle timeout is a tradeoff, not a virtue.
