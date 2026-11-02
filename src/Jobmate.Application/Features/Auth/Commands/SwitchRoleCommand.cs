using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Auth.Commands;

/// <summary>
/// Moves the caller into worker or work giver mode. POST /api/v1/auth/switch-role.
///
/// The role claim in the access token is what <c>[Authorize(Roles = ...)]</c> checks,
/// so a switch has to come with a new token pair; the old access token keeps the old
/// mode until it expires. Switching to the mode you are already in is a no-op that still
/// returns fresh tokens, so the app can use it to resync after a reinstall.
/// </summary>
public sealed record SwitchRoleCommand(UserRole? Role) : IRequest<AuthResultDto>;

public sealed class SwitchRoleCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITokenService tokenService,
    IClock clock) : IRequestHandler<SwitchRoleCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(SwitchRoleCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        // Null is refused by the validator; this only satisfies the compiler.
        var role = request.Role ?? throw new BusinessRuleException("Choose either the worker or work giver role.", "RoleRequired");

        var user = await db.Users
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.Skills)
            .FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new UnauthorizedException("Your session is no longer valid. Please sign in again.");

        if (!user.IsActive)
        {
            throw new ForbiddenException("This account has been deactivated.");
        }

        if (user.ActiveRole != role)
        {
            user.ActiveRole = role;
            user.UpdatedAt = now;
        }

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, user, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(user, tokens, user.ActiveRole, requiresOtp: !user.IsVerified);
    }
}
