using System.Collections.Generic;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// The member bans of <see cref="UnityBoundApiPolicy"/> alone, with every platform type allowed: for net10.0 code whose
    /// output is compared byte for byte across machines but which may use files, threads and the clock, such as Gen's.
    /// Refused: the members the bans name for culture-dependent text and text order (an interpolated integer and an
    /// integer formatted through Object.ToString included), the platform newline, randomized hashing, thread identity,
    /// enumeration in hash order and unstable sorts; a null provider, culture or comparer and a StringComparison other
    /// than a constant Ordinal or OrdinalIgnoreCase, given to any member; and the hash-order rules of
    /// <see cref="UnorderedSequences"/>. The scan refuses native-sized integers and native calls under every policy.
    /// This policy refuses no type, so randomness, the clock and floating point pass it, and so do the members of types
    /// the allowlist leaves out that format numbers or order text by the device culture (see the remarks of
    /// <see cref="UnityBoundApiPolicy"/>).
    /// </summary>
    internal sealed class MemberBansPolicy : IPlatformApiPolicy
    {
        private MemberBansPolicy()
        {
        }

        public static MemberBansPolicy Default { get; } = new MemberBansPolicy();

        public string RefusedTypeReason => "is refused (never: this policy refuses no type)";

        public bool IsTypeAllowed(string typeName) => true;

        public IEnumerable<string> MemberViolations(MemberUse use, CallerContext caller) => UnityBoundApiPolicy.Default.BannedMemberUses(use, caller);
    }
}
