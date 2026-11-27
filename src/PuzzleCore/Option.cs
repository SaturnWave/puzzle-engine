using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PuzzleCore
{
    /// <summary>
    /// A value that may be absent, visible in the type. The engine never reports absence with null.
    /// default(Option&lt;T&gt;) is None.
    /// </summary>
    public readonly struct Option<T> : IEquatable<Option<T>>
    {
        private readonly T _value;

        internal Option(T value)
        {
            _value = value;
            HasValue = true;
        }

        public bool HasValue { get; }

        public bool TryGet([MaybeNullWhen(false)] out T value)
        {
            value = _value;
            return HasValue;
        }

        /// <summary>The value, or <paramref name="fallback"/> when absent.</summary>
        public T Or(T fallback) => HasValue ? _value : fallback;

        public bool Equals(Option<T> other) =>
            HasValue == other.HasValue && (!HasValue || EqualityComparer<T>.Default.Equals(_value, other._value));

        public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);

        public override int GetHashCode() => HasValue ? EqualityComparer<T>.Default.GetHashCode(_value!) : 0;

        /// <summary>
        /// "Some(value)" or "None". The value is formatted as string.Format with the invariant culture formats it: an
        /// IFormattable value, every number among them, through ToString(null, CultureInfo.InvariantCulture); any other
        /// value through ToString(); null as nothing. An enum ignores the culture, so a value outside its members prints
        /// the device's minus sign (a known gap of the engine's determinism guard).
        /// </summary>
        public override string ToString() => HasValue ? string.Format(CultureInfo.InvariantCulture, "Some({0})", _value) : "None";

        public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

        public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);
    }

    /// <summary>Builds options; Option.None&lt;T&gt;() and Option.Some(value).</summary>
    public static class Option
    {
        public static Option<T> Some<T>(T value) => new Option<T>(value);

        public static Option<T> None<T>() => default;
    }
}
