using UvA.LTI;

namespace UvA.Workflow.Api.Authentication.CanvasLti;

public class CanvasClaimsResolver(
    IUserService userService,
    CanvasLaunchTargetResolver targetResolver)
    : ILtiClaimsResolver
{
    public async Task<Dictionary<string, object>> ResolveClaims(LtiPrincipal principal)
    {
        var launchInfo = CanvasLaunchInfo.FromPrincipal(principal);
        var user = await userService.AddOrUpdateUser(
            launchInfo.UvanetId,
            launchInfo.DisplayName,
            launchInfo.Email);
        var target = await targetResolver.ResolveTarget(user, launchInfo, CancellationToken.None);

        return new Dictionary<string, object>
        {
            [CanvasClaimTypes.Email] = user.Email,
            [CanvasClaimTypes.FamilyName] = "",
            [CanvasClaimTypes.GivenName] = user.DisplayName,
            [CanvasClaimTypes.Locale] = launchInfo.Locale,
            [CanvasClaimTypes.Name] = user.DisplayName,
            [CanvasClaimTypes.Role] = launchInfo.IsTeacher ? "Teacher" : "Student",
            [CanvasClaimTypes.Target] = target,
            [CanvasClaimTypes.UvanetId] = launchInfo.UvanetId
        };
    }
}