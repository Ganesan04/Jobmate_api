using FluentValidation;
using Jobmate.Application.Features.Payments.Commands;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Features.Payments.Validators;

/// <summary>
/// Validates a payment request. The field names are the body's, not the command's, so
/// the app can attach each message to the input the worker actually typed into.
///
/// The amounts are also checked by <see cref="PaymentRules.Calculate"/>, which is the
/// authority; this only turns the common mistakes into a 400 with a readable message
/// before the handler runs.
/// </summary>
public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty()
            .WithMessage("A job is required.")
            .WithName("jobId");

        RuleFor(x => x.WorkerId)
            .NotEmpty()
            .WithMessage("A worker is required.")
            .WithName("workerId");

        RuleFor(x => x.Request.BasePay)
            .GreaterThan(0)
            .WithMessage("Enter the pay for this worker.")
            .LessThanOrEqualTo(PaymentRules.MaxPaymentAmount)
            .WithMessage("That pay amount is too large.");

        RuleFor(x => x.Request.Overtime)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Overtime cannot be negative.")
            .When(x => x.Request.Overtime != 0);

        RuleFor(x => x.Request)
            .Must(r => (long)r.BasePay + r.Overtime <= PaymentRules.MaxPaymentAmount)
            .WithMessage("That payment total is too large.")
            .WithName("basePay");

        RuleFor(x => x.Request)
            .Must(r => NetAfterFee(r.BasePay, r.Overtime) > 0)
            .WithMessage("The platform fee is larger than this payment. Increase the amount.")
            .WithName("basePay");

        RuleFor(x => x.Request.Method)
            .IsInEnum()
            .WithMessage("Choose a valid payment method.");

        RuleFor(x => x.Request.IdempotencyKey)
            .MaximumLength(120)
            .WithMessage("That key is too long.")
            .When(x => x.Request.IdempotencyKey is not null);
    }

    /// <summary>
    /// The worker's net for the request, using the domain's own rounding so the
    /// validator and the rule agree on the boundary where the fee exactly eats the pay.
    /// </summary>
    private static int NetAfterFee(int basePay, int overtime)
    {
        try
        {
            return PaymentRules.Calculate(basePay, overtime).WorkerNet;
        }
        catch (BusinessRuleException)
        {
            // The amount itself is reported by the rules above.
            return 1;
        }
    }
}

/// <summary>The transaction reference is optional, but short and printable when supplied.</summary>
public sealed class ConfirmPaymentCommandValidator : AbstractValidator<ConfirmPaymentCommand>
{
    public ConfirmPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .WithMessage("A payment is required.")
            .WithName("id");

        RuleFor(x => x.Request.TransactionReference)
            .MaximumLength(120)
            .WithMessage("That reference is too long.")
            .When(x => x.Request.TransactionReference is not null);
    }
}
