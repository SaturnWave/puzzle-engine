using System;
using System.Collections;
using System.Collections.Generic;

namespace PuzzleCore
{
    /// <summary>
    /// An immutable array that wraps a private copy. The engine uses it instead of
    /// System.Collections.Immutable so Unity needs no extra DLL. default(ReadOnlyArray&lt;T&gt;)
    /// is the empty array. Equality is element-wise, in order.
    /// </summary>
    public readonly struct ReadOnlyArray<T> : IReadOnlyList<T>, IEquatable<ReadOnlyArray<T>>
    {
        private readonly T[]? _items;

        /// <summary>Takes ownership of <paramref name="items"/>; callers pass a fresh array only (see <see cref="ReadOnlyArray"/>).</summary>
        internal ReadOnlyArray(T[] items) => _items = items;

        public int Count => _items?.Length ?? 0;

        public bool IsEmpty => Count == 0;

        public T this[int index] => _items is null ? throw new ArgumentOutOfRangeException(nameof(index)) : _items[index];

        /// <summary>A copy as a plain array.</summary>
        public T[] ToArray() => _items is null ? Array.Empty<T>() : (T[])_items.Clone();

        public Enumerator GetEnumerator() => new Enumerator(_items ?? Array.Empty<T>());

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(ReadOnlyArray<T> other)
        {
            if (Count != other.Count)
            {
                return false;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int index = 0; index < Count; index++)
            {
                if (!comparer.Equals(this[index], other[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ReadOnlyArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            // Order-sensitive mix of the element hashes; only used by hash containers, never for results.
            unchecked
            {
                int hash = 17;
                foreach (T item in this)
                {
                    hash = (hash * 31) + (item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item));
                }

                return hash;
            }
        }

        public static bool operator ==(ReadOnlyArray<T> left, ReadOnlyArray<T> right) => left.Equals(right);

        public static bool operator !=(ReadOnlyArray<T> left, ReadOnlyArray<T> right) => !left.Equals(right);

        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] _items;
            private int _index;

            internal Enumerator(T[] items)
            {
                _items = items;
                _index = -1;
            }

            public T Current => _items[_index];

            object? IEnumerator.Current => Current;

            public bool MoveNext()
            {
                _index++;
                return _index < _items.Length;
            }

            public void Reset() => _index = -1;

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Builds read-only arrays; each builder copies its input.</summary>
    public static class ReadOnlyArray
    {
        /// <summary>A copy of <paramref name="items"/>; later changes to the source do not show.</summary>
        public static ReadOnlyArray<T> CopyOf<T>(IEnumerable<T> items) => new ReadOnlyArray<T>(new List<T>(items).ToArray());

        public static ReadOnlyArray<T> Of<T>(params T[] items) => new ReadOnlyArray<T>((T[])items.Clone());

        public static ReadOnlyArray<T> Empty<T>() => default;
    }
}
