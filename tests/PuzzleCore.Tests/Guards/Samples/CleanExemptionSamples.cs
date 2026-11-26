using System;

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// A record with a text member. The PrintMembers the compiler writes for it appends the text as an object, a call the
    /// member bans refuse anywhere else: it passes only because the members the compiler writes for records are exempt.
    /// </summary>
    internal sealed record CleanNamedCell(string Name, int Row);

    /// <summary>
    /// A hand-written GetHashCode override that hashes its text. It passes only because a GetHashCode override may call
    /// the hashing members: a hash code it combines stays a hash code, which nothing orders or prints.
    /// </summary>
    internal sealed class CleanHashedName : IEquatable<CleanHashedName>
    {
        private readonly string _name;

        public CleanHashedName(string name) => _name = name;

        public bool Equals(CleanHashedName? other) => other is not null && string.Equals(_name, other._name, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as CleanHashedName);

        public override int GetHashCode() => _name.GetHashCode(StringComparison.Ordinal);
    }
}
