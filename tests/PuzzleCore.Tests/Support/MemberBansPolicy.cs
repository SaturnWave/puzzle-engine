using System.Collections.Generic;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// The member bans of <see cref="UnityBoundApiPolicy"/> alone, with every platform type allowed: for net10.0 code whose
    /// output is compared byte for byte across machines but which may use files, threads and the clock, such as
    /// offline tools whose text output is compared byte for byte. Refused: culture-dependent text and text order (an
    /// interpolated integer included), the platform newline, randomized hashing, enumeration in hash order and unstable sorts.
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
