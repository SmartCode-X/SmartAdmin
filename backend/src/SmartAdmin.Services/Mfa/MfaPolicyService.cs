using SmartAdmin.Core;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Services;

/// <summary>
/// <see cref="IMfaPolicyService"/> 默认实现。
/// <para>TOTP 能力 = SysConfig 运行时总闸 ∨ Options 部署地板;强制对象 = 账号 ForceTotp / 超管必绑。</para>
/// </summary>
public class MfaPolicyService(
    IPermissionProvider permissions,
    IRepository<SysHighSensitivityPermission> customHighSens,
    AdminSecurityOptions security,
    IConfigService? config = null,
    IRepository<SysUser>? users = null) : IMfaPolicyService
{
    /// <inheritdoc />
    public virtual async Task<bool> IsTotpFeatureEnabledAsync(CancellationToken cancellationToken = default)
    {
        // 部署地板:appsettings Totp:Enabled=true 时始终开(测试与硬开部署)
        if (security.Totp.Enabled) return true;
        // 运行时总闸:配置中心 sys.security.totp.enabled(与 captcha 同款)
        if (config is null) return false;
        var raw = await config.GetValueByKeyAsync(AdminTotpOptions.KEY_ENABLED);
        return bool.TryParse(raw, out var e) && e;
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsMfaRequiredAsync(SysUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!await IsTotpFeatureEnabledAsync(cancellationToken)) return false;

        if (user.ForceTotp) return true;
        return user.IsSuperAdmin && await ResolveRequireForSuperAdminAsync();
    }

    /// <summary>超管必绑:Options 地板 ∨ SysConfig 运行时开(与总闸同语义,避免种子 false 盖掉测试/部署 Options)。</summary>
    protected virtual async Task<bool> ResolveRequireForSuperAdminAsync()
    {
        if (security.Totp.RequireForSuperAdmin) return true;
        if (config is null) return false;
        var raw = await config.GetValueByKeyAsync(AdminTotpOptions.KEY_REQUIRE_FOR_SUPER_ADMIN);
        return bool.TryParse(raw, out var e) && e;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlySet<string>> GetEffectiveHighSensitivityPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        var set = new HashSet<string>(HighSensitivityPermissions.Default, StringComparer.OrdinalIgnoreCase);
        var customs = await customHighSens.AsQueryable().Select(x => x.PermissionCode).ToListAsync();
        foreach (var c in customs)
        {
            if (!string.IsNullOrWhiteSpace(c))
                set.Add(c.Trim());
        }
        return set;
    }

    /// <inheritdoc />
    public virtual async Task<bool> HoldsHighSensitivityPermissionAsync(
        long userId, CancellationToken cancellationToken = default)
    {
        // 超管不走权限码集合,但调用方应先判 IsSuperAdmin;此处仍安全地按码交集判定
        if (users is not null)
        {
            var u = await users.GetByIdAsync(userId);
            if (u?.IsSuperAdmin == true) return true;
        }

        var codes = await permissions.GetPermissionCodesAsync(userId, cancellationToken);
        if (codes.Count == 0) return false;

        var high = await GetEffectiveHighSensitivityPermissionsAsync(cancellationToken);
        foreach (var c in codes)
        {
            if (high.Contains(c)) return true;
        }
        return false;
    }
}
