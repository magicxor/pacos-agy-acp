using System.ComponentModel.DataAnnotations;

namespace Pacos.Models.Options;

/// <summary>
/// Accepts <see langword="null"/> or an absolute <c>https://</c> <see cref="Uri"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsoluteHttpsUriAttribute : ValidationAttribute
{
    public AbsoluteHttpsUriAttribute()
        : base("The {0} field must be an absolute https:// URL.")
    {
    }

    public override bool IsValid(object? value)
        => value is null or Uri { IsAbsoluteUri: true, Scheme: "https" };
}
