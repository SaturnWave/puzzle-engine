using System.Collections.Generic;

namespace PuzzleCore.Json
{
    /// <summary>One thing wrong with a file: where (a JSON path such as $.settings.board.width) and what.</summary>
    public sealed record FileProblem(string Path, string Message)
    {
        /// <summary>"path: message", the form the tools print.</summary>
        public override string ToString() => Path + ": " + Message;
    }

    /// <summary>
    /// The result of loading a typed model from a file: the model, or every problem found.
    /// Loaders collect problems instead of throwing, so one run reports all of a file's
    /// mistakes. There are no defaults: a missing field is a problem, never a fallback.
    /// </summary>
    public abstract record FileLoadResult<T>
    {
        private FileLoadResult()
        {
        }

        public sealed record Loaded(T Value) : FileLoadResult<T>;

        /// <summary>At least one problem, in the order found.</summary>
        public sealed record Invalid(IReadOnlyList<FileProblem> Problems) : FileLoadResult<T>;
    }
}
