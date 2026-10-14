/**
 * The shape of an error the API returns (Phase 8 / finding F.B.4).
 *
 * Every module's mutation `onError` callback reads the ProblemDetails-style
 * body the API sends (`title`, `detail`, `message`, `code`), plus the
 * `shortage` value the negative-stock responses carry. Declaring it once as an
 * ambient type lets those callbacks be typed (`(err: ApiErrorLike)`) instead of
 * `any`, so a misspelled field is a compile error rather than a silent
 * `undefined` in a user-facing message.
 *
 * The body is open (`[key: string]: unknown`) because different endpoints add
 * their own fields; the known ones are named so they stay checked.
 */
interface ApiErrorBody {
  title?: string;
  detail?: string;
  message?: string;
  code?: string;
  shortage?: number;
  [key: string]: unknown;
}

interface ApiErrorLike {
  response?: { data?: ApiErrorBody };
  message?: string;
  code?: string;
}
