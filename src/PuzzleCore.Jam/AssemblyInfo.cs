using System.Diagnostics.CodeAnalysis;

// CA1716 flags names that are keywords in other .NET languages (VB's Exit, Not, On). The engine
// is consumed only from C# (Unity client, CLI, server adapter), and the design brief names these
// (Exit(PieceId), LotClue.Not, LotClue.On), so the names stay. The one suppression in the assembly.
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Consumed only from C#; the design brief's names Exit, Not and On are kept.")]
