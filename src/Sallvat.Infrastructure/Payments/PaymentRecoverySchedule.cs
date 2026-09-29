using Sallvat.Domain.Payments;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.Infrastructure.Payments;

internal static class PaymentRecoverySchedule
{
    // Query is reused for selection and inside the serializable claim. No in-memory retry counters.
    internal static IQueryable<Payment> Due(SallvatDbContext db, DateTimeOffset now)
    {
        var first = now.AddMinutes(-2);
        var second = now.AddMinutes(-5);
        var third = now.AddMinutes(-15);
        var oldest = now.AddHours(-24);
        return db.Payments.Where(p => p.Provider == "MercadoPago" && p.Environment == PaymentEnvironment.Sandbox
                && p.Status == PaymentStatus.Pending && p.DispatchState == PaymentDispatchState.Completed
                && p.ExternalOrderId != null && p.PreferenceId == null && p.ExternalPaymentId == null
                && p.DispatchStartedAtUtc >= oldest && p.DispatchStartedAtUtc <= first)
            .Where(p => !db.PaymentRecoveryExecutions.Any(e => e.PaymentId == p.Id
                && e.State == PaymentRecoveryExecutionState.Running && e.ExpiresAtUtc > now))
            .Select(p => new
            {
                Payment = p,
                Count = db.PaymentRecoveryExecutions.Count(e => e.PaymentId == p.Id && e.Source == PaymentRecoverySource.Automatic),
                Last = db.PaymentRecoveryExecutions.Where(e => e.PaymentId == p.Id && e.Source == PaymentRecoverySource.Automatic)
                    .Select(e => (DateTimeOffset?)e.StartedAtUtc).Max(),
            })
            .Where(p => p.Count == 0 || (p.Count == 1 && p.Last <= second) || (p.Count == 2 && p.Last <= third))
            .Select(p => p.Payment);
    }
}
