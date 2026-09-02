using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Sallvat.Web.Security;

internal sealed class RecoveryRequestLimiter(IMemoryCache cache) :
    IRecoveryRequestLimiter
{
    private const int PermitLimit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public bool TryAcquire(string email)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var keyBytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalizedEmail));
        var cacheKey = $"account-recovery:{Convert.ToHexString(keyBytes)}";
        var state = cache.GetOrCreate(
            cacheKey,
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = Window;
                return new RecoveryWindow();
            });

        lock (state!)
        {
            if (state.Count >= PermitLimit)
            {
                return false;
            }

            state.Count++;
            return true;
        }
    }

    private sealed class RecoveryWindow
    {
        public int Count { get; set; }
    }
}
