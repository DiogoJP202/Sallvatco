using System.ComponentModel.DataAnnotations;
using Sallvat.Application.Promotions;
using Sallvat.Domain.Promotions;

namespace Sallvat.Web.Models.Promotions;

public sealed class CouponEditorViewModel
{
    [Required(ErrorMessage = "Informe o código.")]
    [StringLength(40, MinimumLength = 3)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public CouponDiscountType DiscountType { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal Value { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal MinimumSubtotal { get; set; }

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    [Range(1, int.MaxValue)]
    public int? TotalUsageLimit { get; set; }

    [Range(1, int.MaxValue)]
    public int? UsageLimitPerIdentity { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid ConcurrencyVersion { get; set; }

    public CouponEditorInput ToInput() => new(
        Code,
        DiscountType,
        Value,
        MinimumSubtotal,
        ToUtc(StartsAtUtc),
        ToUtc(ExpiresAtUtc),
        TotalUsageLimit,
        UsageLimitPerIdentity,
        IsActive);

    public static CouponEditorViewModel From(AdminCouponDetails details) =>
        new()
        {
            Code = details.Coupon.Code,
            DiscountType = details.Coupon.DiscountType,
            Value = details.Coupon.Value,
            MinimumSubtotal = details.Coupon.MinimumSubtotal,
            StartsAtUtc = details.Coupon.StartsAtUtc?.UtcDateTime,
            ExpiresAtUtc = details.Coupon.ExpiresAtUtc?.UtcDateTime,
            TotalUsageLimit = details.Coupon.TotalUsageLimit,
            UsageLimitPerIdentity = details.Coupon.UsageLimitPerIdentity,
            IsActive = details.Coupon.IsActive,
            ConcurrencyVersion = details.ConcurrencyVersion,
        };

    private static DateTimeOffset? ToUtc(DateTime? value) => value.HasValue
        ? new DateTimeOffset(
            DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
        : null;
}
