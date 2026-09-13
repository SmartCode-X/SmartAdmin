// 门户重建(enterInitial)失败的分诊。
//
// 只有会话真的没了才清会话回登录页:HTTP 401,或后端明说令牌失效的两个业务码。
// 网络抖一下、后端 502、被限流都不是会话问题 —— 清会话赶回登录页,重登时还会撞上同一个网络问题,
// 白丢当前页面又解决不了任何事,所以这类失败走可重试的错误页。
import { ApiError } from '#/api'

/** 令牌/刷新令牌失效(ErrorCode.TokenInvalid / RefreshTokenInvalid)。 */
export const TOKEN_INVALID_CODE = 40006
export const REFRESH_TOKEN_INVALID_CODE = 40007

const SESSION_DEAD_CODES = new Set([401, TOKEN_INVALID_CODE, REFRESH_TOKEN_INVALID_CODE])

/**
 * 该错误是否意味着会话已失效(可以清会话回登录页)。
 * 非 ApiError(网络中断、超时)与 5xx / 429 一律返回 false —— 留在原地重试。
 */
export function isSessionDead(err: unknown): boolean {
  return err instanceof ApiError && SESSION_DEAD_CODES.has(err.code)
}
