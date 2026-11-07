using System.Diagnostics.CodeAnalysis;

// CA1716 flags type names that are keywords in other .NET languages (VB's Option, Exit, Not).
// The engine is consumed only from C# (Unity client, CLI, server adapter), and the design brief
// names these types, so the names stay. This is the one analyzer suppression in the assembly.
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Consumed only from C#; the design brief's names Option, Exit and Not are kept.")]
