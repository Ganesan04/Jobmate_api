using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobmate.Infrastructure.Services;

/// <summary>
/// Development OTP delivery. It writes the code to the log and echoes it in the API
/// response so the Flutter app can be exercised without an SMS vendor. Swapping in
/// a real gateway means implementing <see cref="IOtpService"/> and changing one
/// DI registration — no handler changes, because the Application layer only ever
/// sees the interface.
/// </summary>
public sealed class MockOtpService(
    ILogger<MockOtpService> logger,
    IHostEnvironment environment) : IOtpService
{
    public Task<OtpDispatchResult> SendAsync(
        string phoneNumber,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var masked = MaskPhone(phoneNumber);

        logger.LogInformation(
            "Mock OTP delivery: purpose={Purpose} phone={Phone} code={Code}",
            purpose,
            masked,
            code);

        // Echoing the code in production would let anyone who can call the endpoint
        // sign in as anyone, so it is confined to non-production environments.
        var exposeCode = !environment.IsProduction();

        return Task.FromResult(new OtpDispatchResult(
            Success: true,
            Code: exposeCode ? code : null,
            Message: exposeCode
                ? $"Development OTP: {code}"
                : "A verification code has been sent."));
    }

    private static string MaskPhone(string phoneNumber) =>
        phoneNumber.Length < 4 ? "***" : $"***{phoneNumber[^4..]}";
}

/// <summary>
/// Development password-reset delivery. The token is the same OTP code the
/// forgot-password flow issued, so the two paths share one expiry and attempt budget.
/// </summary>
public sealed class MockPasswordResetService(
    ILogger<MockPasswordResetService> logger,
    IHostEnvironment environment) : IPasswordResetService
{
    public Task<PasswordResetDispatchResult> SendAsync(
        string phoneNumber,
        string token,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Mock password reset delivery: phone=***{PhoneSuffix} token={Token}",
            phoneNumber[^Math.Min(4, phoneNumber.Length)..],
            token);

        var exposeToken = !environment.IsProduction();

        return Task.FromResult(new PasswordResetDispatchResult(
            Success: true,
            Token: exposeToken ? token : null,
            Message: exposeToken
                ? $"Development reset code: {token}"
                : "A reset code is on its way."));
    }
}
