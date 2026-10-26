// Licensed under the MIT license. See LICENSE.TXT in the repository root.

using System;

namespace Xsl3.Xdm;

/// <summary>An evaluation's configured or provider-resolved timezone, resolved only when an operation needs it.</summary>
internal readonly struct ImplicitTimezone
{
    private readonly int _fixedMinutes;
    private readonly Lazy<int>? _provider;
    private readonly bool _hasSource;

    internal ImplicitTimezone(int minutes)
    {
        _fixedMinutes = minutes;
        _hasSource = true;
    }

    internal ImplicitTimezone(Func<int> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = new Lazy<int>(provider);
        _hasSource = true;
    }

    internal int Minutes => _hasSource ? _provider?.Value ?? _fixedMinutes
        : throw new InvalidOperationException("An implicit timezone requires a configured value or a provider");

    internal static ImplicitTimezone Local()
    {
        DateTime instant = DateTime.UtcNow;
        return new(() => (int)TimeZoneInfo.Local.GetUtcOffset(instant).TotalMinutes);
    }

    public static implicit operator ImplicitTimezone(int minutes) => new(minutes);
}
