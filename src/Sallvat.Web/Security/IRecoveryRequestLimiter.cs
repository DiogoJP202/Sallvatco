namespace Sallvat.Web.Security;

public interface IRecoveryRequestLimiter
{
    bool TryAcquire(string email);
}
