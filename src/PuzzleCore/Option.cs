using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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

        public override string ToString() => HasValue ? "Some(" + (_value?.ToString() ?? string.Empty) + ")" : "None";

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
