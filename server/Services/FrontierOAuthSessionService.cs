using System.Text;
using GuildDashboard.Server.Data;
using GuildDashboard.Server.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GuildDashboard.Server.Services;

/// <summary>Origine du token utilisé pour un appel CAPI Frontier.</summary>
public enum FrontierTokenResolutionMode
{
    LiveMemory,
    LiveRestoredFromDatabase,
    LiveRefreshed,
    ReconnectRequired,
}

public readonly record struct FrontierTokenResolutionResult(
    FrontierTokenResult? Token,
    FrontierTokenResolutionMode Mode);

/// <summary>
/// Persistance sécurisée des tokens Frontier + résolution effective (mémoire → SQL → refresh).
/// Les secrets ne sont jamais écrits dans les logs.
/// </summary>
public class FrontierOAuthSessionService
{
    private const string ProtectorPurpose = "GuildDashboard.FrontierOAuth.Tokens.v1";

    private readonly GuildDashboardDbContext _db;
    private readonly FrontierAuthService _auth;
    private readonly FrontierTokenStore _store;
    private readonly IDataProtector _protector;
    private readonly ILogger<FrontierOAuthSessionService> _log;

    public FrontierOAuthSessionService(
        GuildDashboardDbContext db,
        FrontierAuthService auth,
        FrontierTokenStore store,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<FrontierOAuthSessionService> log)
    {
        _db = db;
        _auth = auth;
        _store = store;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _log = log;
    }

    /// <summary>Met à jour le store mémoire et persiste en base (callback OAuth, refresh).</summary>
    public async Task PersistAndSetAsync(FrontierTokenResult token, FrontierValidationReport? report, CancellationToken ct)
    {
        _store.SetToken(token, report);
        await PersistAsync(token, ct);
    }

    public async Task PersistAsync(FrontierTokenResult token, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expiresAt = ComputeExpiresAtUtc(token);

        var accessProt = _protector.Protect(Encoding.UTF8.GetBytes(token.AccessToken));
        var refreshProt = string.IsNullOrEmpty(token.RefreshToken)
            ? Array.Empty<byte>()
            : _protector.Protect(Encoding.UTF8.GetBytes(token.RefreshToken));

        var row = await _db.FrontierOAuthSessions.FirstOrDefaultAsync(x => x.Id == FrontierOAuthSession.SingletonId, ct);
        if (row == null)
        {
            row = new FrontierOAuthSession
            {
                Id = FrontierOAuthSession.SingletonId,
                CreatedAtUtc = now,
            };
            _db.FrontierOAuthSessions.Add(row);
        }

        row.AccessTokenProtected = accessProt;
        row.RefreshTokenProtected = refreshProt;
        row.AccessTokenExpiresAtUtc = expiresAt;
        row.TokenType = token.TokenType;
        row.Scope = token.Scope;
        row.UpdatedAtUtc = now;
        row.LastRefreshAtUtc = now;
        row.IsActive = true;

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("[FrontierOAuthSession] Session persistée (expiresUtc={Expires:o})", expiresAt);
    }

    private static DateTime ComputeExpiresAtUtc(FrontierTokenResult token)
    {
        if (token.AccessTokenExpiresAtUtc.HasValue)
            return token.AccessTokenExpiresAtUtc.Value;
        return DateTime.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60));
    }

    /// <summary>Au démarrage : recharge la session pour que le store ne soit pas vide après restart.</summary>
    public async Task RehydrateFromDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await ResolveEffectiveTokenAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierOAuthSession] Rehydratation au démarrage ignorée");
        }
    }

    public async Task<FrontierTokenResult?> GetEffectiveTokenAsync(CancellationToken ct)
    {
        var r = await ResolveEffectiveTokenAsync(ct);
        return r.Token;
    }

    /// <summary>
    /// Ordre : token mémoire valide → refresh si expiré → ligne SQL → refresh → sinon null.
    /// </summary>
    public async Task<FrontierTokenResolutionResult> ResolveEffectiveTokenAsync(CancellationToken ct)
    {
        var mem = _store.GetToken();
        if (mem != null && !IsAccessExpired(mem))
            return new FrontierTokenResolutionResult(mem, FrontierTokenResolutionMode.LiveMemory);

        if (mem != null && IsAccessExpired(mem) && !string.IsNullOrEmpty(mem.RefreshToken))
        {
            var refreshed = await _auth.RefreshTokenAsync(mem.RefreshToken, ct);
            if (refreshed != null)
            {
                await PersistAsync(refreshed, ct);
                _store.SetToken(refreshed);
                _log.LogInformation("[FrontierOAuthSession] Access token rafraîchi (mémoire expirée)");
                return new FrontierTokenResolutionResult(refreshed, FrontierTokenResolutionMode.LiveRefreshed);
            }
        }

        var row = await _db.FrontierOAuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == FrontierOAuthSession.SingletonId && x.IsActive, ct);

        if (row == null || row.AccessTokenProtected.Length == 0)
            return new FrontierTokenResolutionResult(null, FrontierTokenResolutionMode.ReconnectRequired);

        FrontierTokenResult fromDb;
        try
        {
            fromDb = DecryptRow(row);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[FrontierOAuthSession] Déchiffrement session persistée impossible");
            return new FrontierTokenResolutionResult(null, FrontierTokenResolutionMode.ReconnectRequired);
        }

        _store.SetToken(fromDb);

        if (!IsAccessExpired(fromDb))
        {
            _log.LogInformation("[FrontierOAuthSession] Session restaurée depuis la base (expiresUtc={Expires:o})", fromDb.AccessTokenExpiresAtUtc);
            return new FrontierTokenResolutionResult(fromDb, FrontierTokenResolutionMode.LiveRestoredFromDatabase);
        }

        if (!string.IsNullOrEmpty(fromDb.RefreshToken))
        {
            var refreshed = await _auth.RefreshTokenAsync(fromDb.RefreshToken, ct);
            if (refreshed != null)
            {
                await PersistAsync(refreshed, ct);
                _store.SetToken(refreshed);
                _log.LogInformation("[FrontierOAuthSession] Access token rafraîchi après restauration DB");
                return new FrontierTokenResolutionResult(refreshed, FrontierTokenResolutionMode.LiveRefreshed);
            }
        }

        _log.LogWarning("[FrontierOAuthSession] Session persistée expirée et refresh impossible");
        await ClearPersistedAsync(ct);
        _store.ClearToken();
        return new FrontierTokenResolutionResult(null, FrontierTokenResolutionMode.ReconnectRequired);
    }

    public async Task ClearPersistedAsync(CancellationToken ct)
    {
        var row = await _db.FrontierOAuthSessions.FirstOrDefaultAsync(x => x.Id == FrontierOAuthSession.SingletonId, ct);
        if (row != null)
        {
            _db.FrontierOAuthSessions.Remove(row);
            await _db.SaveChangesAsync(ct);
        }

        _log.LogInformation("[FrontierOAuthSession] Session persistée supprimée");
    }

    public FrontierOAuthPersistenceSnapshot GetPersistenceSnapshot()
    {
        var row = _db.FrontierOAuthSessions.AsNoTracking()
            .FirstOrDefault(x => x.Id == FrontierOAuthSession.SingletonId && x.IsActive);
        var mem = _store.GetToken();
        return new FrontierOAuthPersistenceSnapshot(
            PersistedRowExists: row != null,
            LastUpdatedUtc: row?.UpdatedAtUtc,
            AccessExpiresAtUtc: row?.AccessTokenExpiresAtUtc,
            MemoryTokenPresent: mem != null && !string.IsNullOrEmpty(mem.AccessToken));
    }

    private FrontierTokenResult DecryptRow(FrontierOAuthSession row)
    {
        var access = Encoding.UTF8.GetString(_protector.Unprotect(row.AccessTokenProtected));
        var refresh = row.RefreshTokenProtected.Length == 0
            ? ""
            : Encoding.UTF8.GetString(_protector.Unprotect(row.RefreshTokenProtected));
        var expiresIn = (int)Math.Clamp((row.AccessTokenExpiresAtUtc - DateTime.UtcNow).TotalSeconds, 0, int.MaxValue);
        return new FrontierTokenResult(
            access,
            refresh,
            Math.Max(expiresIn, 60),
            row.TokenType,
            row.Scope,
            row.AccessTokenExpiresAtUtc);
    }

    private static bool IsAccessExpired(FrontierTokenResult t)
    {
        if (t.AccessTokenExpiresAtUtc.HasValue)
            return DateTime.UtcNow >= t.AccessTokenExpiresAtUtc.Value.AddMinutes(-2);
        return false;
    }
}

/// <summary>Aperçu sans secret pour diagnostics.</summary>
public readonly record struct FrontierOAuthPersistenceSnapshot(
    bool PersistedRowExists,
    DateTime? LastUpdatedUtc,
    DateTime? AccessExpiresAtUtc,
    bool MemoryTokenPresent);
