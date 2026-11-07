using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace PuzzleCore.Tests.Support
{
    /// <summary>One rule violation found in a compiled assembly.</summary>
    internal sealed record Violation(string Location, string Reason)
    {
        public override string ToString() => Location + ": " + Reason;
    }

    /// <summary>
    /// A type as it appears in a signature: a readable name, the name of its definition
    /// (generic arguments stripped), the external types it mentions and two flags.
    /// </summary>
    internal sealed record TypeShape(
        string Display,
        string DefinitionName,
        ImmutableArray<string> ExternalTypes,
        bool UsesFloatingPoint,
        bool UsesNativeInteger)
    {
        /// <summary>The type arguments of a generic instantiation (String and Int32 of Dictionary`2&lt;String,Int32&gt;); empty otherwise.</summary>
        public ImmutableArray<TypeShape> TypeArguments { get; init; } = ImmutableArray<TypeShape>.Empty;

        /// <summary>The index of a generic parameter placeholder: 1 for "!1" (the type's) or "!!1" (the method's); null for any other type.</summary>
        public int? PlaceholderIndex(bool ofMethod)
        {
            string prefix = ofMethod ? "!!" : "!";
            bool isPlaceholder = Display.StartsWith(prefix, StringComparison.Ordinal) && (ofMethod || !Display.StartsWith("!!", StringComparison.Ordinal));
            return isPlaceholder && int.TryParse(Display.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index : null;
        }
    }

    /// <summary>
    /// A call, field access or token use found in a method body. For a call the IL walk adds what it knows of the values
    /// passed (<see cref="Instance"/>, <see cref="Arguments"/>), so a policy can refuse, for example, a HashSet handed to a
    /// parameter that enumerates it.
    /// </summary>
    internal sealed record MemberUse(TypeShape DeclaringType, string MemberName, IReadOnlyList<TypeShape> Parameters, bool UsesFloatingPoint)
    {
        /// <summary>The method's own type arguments (Cell and String of OrderBy&lt;Cell,String&gt;); empty for a non-generic method.</summary>
        public IReadOnlyList<TypeShape> MethodTypeArguments { get; init; } = Array.Empty<TypeShape>();

        /// <summary>The static type of the receiver of an instance call or callvirt; null when the walk cannot tell or the member is static.</summary>
        public TypeShape? Instance { get; init; }

        /// <summary>The static types of the values passed, one per parameter; null where the walk cannot tell. Empty for a field or token.</summary>
        public IReadOnlyList<TypeShape?> Arguments { get; init; } = Array.Empty<TypeShape?>();

        public string Display => DeclaringType.Display + "::" + MemberName
            + "(" + string.Join(", ", Parameters.Select(parameter => parameter.Display)) + ")";

        /// <summary>The type with the declaring type's (!n) and the method's (!!n) generic parameters replaced by their arguments, where known.</summary>
        public TypeShape Resolve(TypeShape type)
        {
            if (type.PlaceholderIndex(ofMethod: true) is int methodIndex)
            {
                return methodIndex < MethodTypeArguments.Count ? MethodTypeArguments[methodIndex] : type;
            }

            if (type.PlaceholderIndex(ofMethod: false) is int typeIndex)
            {
                return typeIndex < DeclaringType.TypeArguments.Length ? DeclaringType.TypeArguments[typeIndex] : type;
            }

            return type;
        }
    }

    /// <summary>
    /// One member a method body names: the calling type and method (nested and compiler-written
    /// types keep their full name, for example "PuzzleCore.Jam.X+&lt;&gt;c"), and the declaring type
    /// (generic arguments stripped) and name of the member, for example get_Streams.
    /// </summary>
    internal sealed record MemberReferenceSite(string CallerType, string CallerMethod, string DeclaringType, string MemberName)
    {
        public override string ToString() => CallerType + "::" + CallerMethod + " -> " + DeclaringType + "::" + MemberName;
    }

    /// <summary>
    /// Reads a compiled assembly's metadata and IL without loading it, to prove rules the
    /// compiler cannot enforce. Everything is checked on the compiled output, so a rule
    /// cannot be dodged with <c>var</c>, generics or a helper method.
    /// </summary>
    internal sealed class AssemblyInspection : IDisposable
    {
        private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

        private static readonly IReadOnlySet<short> FloatingPointOpCodes = new[]
        {
            OpCodes.Ldc_R4, OpCodes.Ldc_R8, OpCodes.Conv_R4, OpCodes.Conv_R8, OpCodes.Conv_R_Un,
            OpCodes.Ldind_R4, OpCodes.Ldind_R8, OpCodes.Stind_R4, OpCodes.Stind_R8,
            OpCodes.Ldelem_R4, OpCodes.Ldelem_R8, OpCodes.Stelem_R4, OpCodes.Stelem_R8, OpCodes.Ckfinite,
        }.Select(opCode => opCode.Value).ToHashSet();

        /// <summary>Types whose values or arithmetic are floating point even when no float appears in a signature.</summary>
        private static readonly IReadOnlySet<string> FloatingPointTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Single", "System.Double", "System.Decimal", "System.Half",
            "System.Numerics.Vector2", "System.Numerics.Vector3", "System.Numerics.Vector4",
            "System.Numerics.Matrix3x2", "System.Numerics.Matrix4x4", "System.Numerics.Quaternion",
            "System.Numerics.Plane", "System.Numerics.Complex",
        };

        private const string CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";

        /// <summary>
        /// Members the compiler writes for records. Only these count as synthesized: local
        /// functions are also marked [CompilerGenerated] but contain hand-written code.
        /// </summary>
        private static readonly IReadOnlySet<string> RecordSynthesizedMemberNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ToString", "PrintMembers", "GetHashCode", "Equals", "get_EqualityContract", "<Clone>$", "Deconstruct",
            "op_Equality", "op_Inequality",
        };

        private readonly FileStream _stream;
        private readonly PEReader _peReader;
        private readonly MetadataReader _reader;
        private readonly ShapeProvider _shapes;

        public AssemblyInspection(string assemblyPath)
        {
            _stream = File.OpenRead(assemblyPath);
            _peReader = new PEReader(_stream);
            _reader = _peReader.GetMetadataReader();
            _shapes = new ShapeProvider(this);
        }

        public void Dispose()
        {
            _peReader.Dispose();
            _stream.Dispose();
        }

        public IReadOnlyList<string> ReferencedAssemblyNames() => _reader.AssemblyReferences
            .Select(handle => _reader.GetString(_reader.GetAssemblyReference(handle).Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        /// <summary>Every floating-point type, signature, local, literal or instruction, with its location.</summary>
        public IReadOnlyList<Violation> FloatingPointUses()
        {
            var found = new List<Violation>();

            found.AddRange(_reader.TypeReferences
                .Select(FullName)
                .Where(FloatingPointTypeNames.Contains)
                .Select(name => new Violation("type reference " + name, "floating-point type")));

            found.AddRange(_reader.MemberReferences
                .Select(DescribeMemberReference)
                .Where(use => use.UsesFloatingPoint)
                .Select(use => new Violation("member reference " + use.Display, "floating-point signature")));

            foreach (TypeDefinitionHandle typeHandle in _reader.TypeDefinitions)
            {
                found.AddRange(FloatingPointUsesInDefinitions(typeHandle));
            }

            foreach (MethodDefinitionHandle method in AllMethods())
            {
                found.AddRange(FloatingPointUsesInBody(method));
            }

            return found.Distinct().ToList();
        }

        /// <summary>Whether any type reference names the type (for example System.Random).</summary>
        public bool References(string typeFullName) =>
            _reader.TypeReferences.Select(FullName).Contains(typeFullName, StringComparer.Ordinal);

        /// <summary>
        /// Every member that every method body names (calls, field accesses and member tokens,
        /// in this assembly or another), with the method that names it; for guards that limit
        /// which code may touch which member.
        /// </summary>
        public IReadOnlyList<MemberReferenceSite> MemberReferences()
        {
            var found = new List<MemberReferenceSite>();
            foreach (MethodDefinitionHandle methodHandle in AllMethods())
            {
                MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
                string callerType = FullName(method.GetDeclaringType());
                string callerMethod = _reader.GetString(method.Name);
                IEnumerable<MemberUse> uses = Instructions(methodHandle)
                    .Where(instruction => !instruction.Operand.IsNil)
                    .Select(instruction => MemberUseOf(instruction.Operand))
                    .OfType<MemberUse>();
                found.AddRange(uses.Select(use => new MemberReferenceSite(callerType, callerMethod, use.DeclaringType.DefinitionName, use.MemberName)));
            }

            return found.Distinct().ToList();
        }

        /// <summary>
        /// Catch blocks that swallow everything (catch (Exception), a bare catch, or such a catch
        /// with a when-filter) and catch blocks with an empty body (CLAUDE.md, rule 6).
        /// </summary>
        public IReadOnlyList<Violation> GenericOrEmptyCatchBlocks()
        {
            var found = new List<Violation>();
            foreach (MethodDefinitionHandle methodHandle in AllMethods())
            {
                MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                MethodBodyBlock body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
                byte[] il = body.GetILBytes() ?? Array.Empty<byte>();
                string location = Describe(methodHandle);
                foreach (ExceptionRegion region in body.ExceptionRegions)
                {
                    found.AddRange(CatchViolations(region, il, location));
                }
            }

            return found.Distinct().ToList();
        }

        private IEnumerable<Violation> CatchViolations(ExceptionRegion region, byte[] il, string location)
        {
            string at = location + "+IL_" + region.HandlerOffset.ToString("X4", CultureInfo.InvariantCulture);
            bool isHandler = region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter;
            if (!isHandler)
            {
                yield break;
            }

            if (region.Kind == ExceptionRegionKind.Catch && IsCatchAllType(ShapeOf(region.CatchType).DefinitionName))
            {
                yield return new Violation(at, "generic catch (" + ShapeOf(region.CatchType).Display + ")");
            }

            if (region.Kind == ExceptionRegionKind.Filter)
            {
                IReadOnlyList<string> filteredTypes = DecodeInstructions(il, region.FilterOffset, region.HandlerOffset, location)
                    .Where(instruction => instruction.OpCode == OpCodes.Isinst && !instruction.Operand.IsNil)
                    .Select(instruction => ShapeOf(instruction.Operand).DefinitionName)
                    .ToList();
                if (filteredTypes.Count == 0 || filteredTypes.Any(IsCatchAllType))
                {
                    yield return new Violation(at, "generic catch with a when-filter");
                }
            }

            bool isEmpty = DecodeInstructions(il, region.HandlerOffset, region.HandlerOffset + region.HandlerLength, location)
                .All(instruction => EmptyHandlerOpCodes.Contains(instruction.OpCode.Value));
            if (isEmpty)
            {
                yield return new Violation(at, "empty catch block");
            }
        }

        private static bool IsCatchAllType(string typeName) => typeName is "System.Exception" or "System.Object";

        /// <summary>Instructions an empty catch compiles to: drop or store the exception, then leave.</summary>
        private static readonly IReadOnlySet<short> EmptyHandlerOpCodes = new[]
        {
            OpCodes.Nop, OpCodes.Pop, OpCodes.Leave, OpCodes.Leave_S,
            OpCodes.Stloc, OpCodes.Stloc_S, OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3,
        }.Select(opCode => opCode.Value).ToHashSet();

        /// <summary>
        /// Checks every platform API the assembly touches against a policy: the Unity-bound
        /// allowlist and its member bans, or the generator content policy. Types of the
        /// Unity-bound sibling assemblies (PuzzleCore.Jam using PuzzleCore) are not platform APIs:
        /// those assemblies are scanned on their own.
        /// </summary>
        public IReadOnlyList<Violation> ApiPolicyViolations(IPlatformApiPolicy policy)
        {
            var found = new List<Violation>();

            found.AddRange(_reader.TypeReferences
                .Where(handle => !IsUnityBoundSiblingType(handle))
                .Select(FullName)
                .Where(name => !policy.IsTypeAllowed(name))
                .Select(name => new Violation("type reference " + name, "type " + policy.RefusedTypeReason)));

            foreach (TypeDefinitionHandle typeHandle in _reader.TypeDefinitions)
            {
                found.AddRange(NativeIntegerUsesInDefinitions(typeHandle));
            }

            foreach (MethodDefinitionHandle methodHandle in AllMethods())
            {
                MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
                bool isNativeCall = (method.Attributes & MethodAttributes.PinvokeImpl) != 0 || !method.GetImport().Name.IsNil;
                if (isNativeCall)
                {
                    found.Add(new Violation(Describe(methodHandle), "native call (P/Invoke) is not allowed"));
                }

                string methodName = _reader.GetString(method.Name);
                bool isSynthesizedRecordMember = RecordSynthesizedMemberNames.Contains(methodName) && IsCompilerGenerated(method);
                var caller = new CallerContext(methodName, isSynthesizedRecordMember, IsIteratorPlumbing(method, methodName));
                foreach (FlowStep step in Flow(methodHandle))
                {
                    found.AddRange(PolicyViolationsAt(methodHandle, step, caller, policy));
                }
            }

            return found.Distinct().ToList();
        }

        private IEnumerable<Violation> PolicyViolationsAt(MethodDefinitionHandle method, FlowStep step, CallerContext caller, IPlatformApiPolicy policy)
        {
            Instruction instruction = step.Instruction;
            if (instruction.Operand.IsNil)
            {
                yield break;
            }

            string location = Describe(method) + "+IL_" + instruction.Offset.ToString("X4", CultureInfo.InvariantCulture);

            foreach (string type in ExternalTypesOf(instruction.Operand).Where(type => !policy.IsTypeAllowed(type)))
            {
                yield return new Violation(location, "uses " + type + ", which " + policy.RefusedTypeReason);
            }

            MemberUse? use = MemberUseOf(instruction.Operand) is MemberUse member ? member with { Instance = step.Instance, Arguments = step.Arguments } : null;
            if (use is null)
            {
                yield break;
            }

            foreach (string reason in policy.MemberViolations(use, caller))
            {
                yield return new Violation(location, use.Display + ": " + reason);
            }
        }

        private IEnumerable<Violation> FloatingPointUsesInDefinitions(TypeDefinitionHandle typeHandle)
        {
            TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
            string typeName = FullName(typeHandle);

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = _reader.GetFieldDefinition(fieldHandle);
                if (field.DecodeSignature(_shapes, null).UsesFloatingPoint)
                {
                    yield return new Violation(typeName + "::" + _reader.GetString(field.Name), "floating-point field");
                }
            }

            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = _reader.GetPropertyDefinition(propertyHandle);
                if (UsesFloatingPoint(property.DecodeSignature(_shapes, null)))
                {
                    yield return new Violation(typeName + "::" + _reader.GetString(property.Name), "floating-point property");
                }
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (UsesFloatingPoint(_reader.GetMethodDefinition(methodHandle).DecodeSignature(_shapes, null)))
                {
                    yield return new Violation(Describe(methodHandle), "floating-point parameter or return type");
                }
            }
        }

        private IEnumerable<Violation> FloatingPointUsesInBody(MethodDefinitionHandle methodHandle)
        {
            string location = Describe(methodHandle);
            if (LocalTypes(methodHandle).Any(local => local.UsesFloatingPoint))
            {
                yield return new Violation(location, "floating-point local variable");
            }

            foreach (Instruction instruction in Instructions(methodHandle))
            {
                if (FloatingPointOpCodes.Contains(instruction.OpCode.Value))
                {
                    yield return new Violation(location, "floating-point instruction " + instruction.OpCode.Name);
                }
                else if (!instruction.Operand.IsNil && UsesFloatingPoint(instruction.Operand))
                {
                    yield return new Violation(location, "uses floating point via " + DescribeOperand(instruction.Operand));
                }
            }
        }

        private IEnumerable<Violation> NativeIntegerUsesInDefinitions(TypeDefinitionHandle typeHandle)
        {
            TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
            string typeName = FullName(typeHandle);
            const string Reason = "native-sized integer (nint/nuint/IntPtr) has a different width on 32-bit devices";

            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = _reader.GetFieldDefinition(fieldHandle);
                if (field.DecodeSignature(_shapes, null).UsesNativeInteger)
                {
                    yield return new Violation(typeName + "::" + _reader.GetString(field.Name), Reason);
                }
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodSignature<TypeShape> signature = _reader.GetMethodDefinition(methodHandle).DecodeSignature(_shapes, null);
                bool usesNativeInteger = signature.ReturnType.UsesNativeInteger
                    || signature.ParameterTypes.Any(parameter => parameter.UsesNativeInteger)
                    || LocalTypes(methodHandle).Any(local => local.UsesNativeInteger);
                if (usesNativeInteger)
                {
                    yield return new Violation(Describe(methodHandle), Reason);
                }
            }
        }

        private IEnumerable<TypeShape> LocalTypes(MethodDefinitionHandle methodHandle)
        {
            MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                return Array.Empty<TypeShape>();
            }

            MethodBodyBlock body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            return body.LocalSignature.IsNil
                ? Array.Empty<TypeShape>()
                : _reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(_shapes, null);
        }

        private bool UsesFloatingPoint(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeReference or HandleKind.TypeSpecification => ShapeOf(handle).UsesFloatingPoint,
            HandleKind.TypeDefinition => false,
            HandleKind.MemberReference or HandleKind.MethodSpecification or HandleKind.MethodDefinition or HandleKind.FieldDefinition =>
                MemberUseOf(handle)?.UsesFloatingPoint ?? false,
            HandleKind.StandaloneSignature => UsesFloatingPoint((StandaloneSignatureHandle)handle),
            _ => throw new InvalidDataException("Unexpected IL operand kind " + handle.Kind.ToString()),
        };

        private bool UsesFloatingPoint(StandaloneSignatureHandle handle)
        {
            StandaloneSignature signature = _reader.GetStandaloneSignature(handle);
            return signature.GetKind() == StandaloneSignatureKind.LocalVariables
                ? signature.DecodeLocalSignature(_shapes, null).Any(local => local.UsesFloatingPoint)
                : UsesFloatingPoint(signature.DecodeMethodSignature(_shapes, null));
        }

        private static bool UsesFloatingPoint(MethodSignature<TypeShape> signature) =>
            signature.ReturnType.UsesFloatingPoint || signature.ParameterTypes.Any(parameter => parameter.UsesFloatingPoint);

        /// <summary>The external (referenced) types an IL operand mentions, including generic arguments.</summary>
        private IEnumerable<string> ExternalTypesOf(EntityHandle operand)
        {
            switch (operand.Kind)
            {
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification:
                    return ShapeOf(operand).ExternalTypes;
                case HandleKind.MemberReference:
                    return ShapeOfParent(_reader.GetMemberReference((MemberReferenceHandle)operand).Parent).ExternalTypes;
                case HandleKind.MethodSpecification:
                    MethodSpecification specification = _reader.GetMethodSpecification((MethodSpecificationHandle)operand);
                    return specification.DecodeSignature(_shapes, null).SelectMany(argument => argument.ExternalTypes)
                        .Concat(ExternalTypesOf(specification.Method));
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>The member an IL operand names, or null when the operand is a type, a string or a signature.</summary>
        private MemberUse? MemberUseOf(EntityHandle operand)
        {
            switch (operand.Kind)
            {
                case HandleKind.MemberReference:
                    return DescribeMemberReference((MemberReferenceHandle)operand);
                case HandleKind.MethodSpecification:
                    MethodSpecification specification = _reader.GetMethodSpecification((MethodSpecificationHandle)operand);
                    MemberUse? method = MemberUseOf(specification.Method);
                    ImmutableArray<TypeShape> typeArguments = specification.DecodeSignature(_shapes, null);
                    bool argumentsUseFloat = typeArguments.Any(argument => argument.UsesFloatingPoint);
                    return method is null ? null : method with { UsesFloatingPoint = method.UsesFloatingPoint || argumentsUseFloat, MethodTypeArguments = typeArguments };
                case HandleKind.MethodDefinition:
                    MethodDefinition definition = _reader.GetMethodDefinition((MethodDefinitionHandle)operand);
                    MethodSignature<TypeShape> signature = definition.DecodeSignature(_shapes, null);
                    return new MemberUse(ShapeOfDefinition(definition.GetDeclaringType()), _reader.GetString(definition.Name),
                        signature.ParameterTypes, UsesFloatingPoint(signature));
                case HandleKind.FieldDefinition:
                    FieldDefinition field = _reader.GetFieldDefinition((FieldDefinitionHandle)operand);
                    return new MemberUse(ShapeOfDefinition(field.GetDeclaringType()), _reader.GetString(field.Name),
                        Array.Empty<TypeShape>(), field.DecodeSignature(_shapes, null).UsesFloatingPoint);
                default:
                    return null;
            }
        }

        private MemberUse DescribeMemberReference(MemberReferenceHandle handle)
        {
            MemberReference member = _reader.GetMemberReference(handle);
            TypeShape parent = ShapeOfParent(member.Parent);
            string name = _reader.GetString(member.Name);
            if (member.GetKind() == MemberReferenceKind.Field)
            {
                TypeShape fieldType = member.DecodeFieldSignature(_shapes, null);
                return new MemberUse(parent, name, Array.Empty<TypeShape>(), parent.UsesFloatingPoint || fieldType.UsesFloatingPoint);
            }

            MethodSignature<TypeShape> signature = member.DecodeMethodSignature(_shapes, null);
            return new MemberUse(parent, name, signature.ParameterTypes, parent.UsesFloatingPoint || UsesFloatingPoint(signature));
        }

        private TypeShape ShapeOfParent(EntityHandle parent) => parent.Kind switch
        {
            HandleKind.TypeReference or HandleKind.TypeSpecification => ShapeOf(parent),
            HandleKind.TypeDefinition => ShapeOfDefinition((TypeDefinitionHandle)parent),
            HandleKind.MethodDefinition => ShapeOfDefinition(_reader.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType()),
            HandleKind.ModuleReference => new TypeShape("<module>", "<module>", ImmutableArray<string>.Empty, false, false),
            _ => throw new InvalidDataException("Unexpected member parent kind " + parent.Kind.ToString()),
        };

        private TypeShape ShapeOf(EntityHandle type) => type.Kind switch
        {
            HandleKind.TypeReference => _shapes.GetTypeFromReference(_reader, (TypeReferenceHandle)type, 0),
            HandleKind.TypeSpecification => _reader.GetTypeSpecification((TypeSpecificationHandle)type).DecodeSignature(_shapes, null),
            HandleKind.TypeDefinition => ShapeOfDefinition((TypeDefinitionHandle)type),
            _ => throw new InvalidDataException("Not a type handle: " + type.Kind.ToString()),
        };

        private TypeShape ShapeOfDefinition(TypeDefinitionHandle handle) =>
            _shapes.GetTypeFromDefinition(_reader, handle, 0);

        private string DescribeOperand(EntityHandle operand) =>
            MemberUseOf(operand)?.Display ?? ShapeOf(operand).Display;

        private bool IsCompilerGenerated(MethodDefinition method) => method.GetCustomAttributes()
            .Select(handle => _reader.GetCustomAttribute(handle).Constructor)
            .Where(constructor => constructor.Kind == HandleKind.MemberReference)
            .Select(constructor => ShapeOfParent(_reader.GetMemberReference((MemberReferenceHandle)constructor).Parent).DefinitionName)
            .Contains(CompilerGeneratedAttribute, StringComparer.Ordinal);

        /// <summary>
        /// The constructor or GetEnumerator of a compiler-written iterator state machine
        /// (a type named &lt;Method&gt;d__N marked [CompilerGenerated]). Lambda closures
        /// (&lt;&gt;c, &lt;&gt;c__DisplayClass) and local functions do not match.
        /// </summary>
        private bool IsIteratorPlumbing(MethodDefinition method, string methodName)
        {
            TypeDefinition type = _reader.GetTypeDefinition(method.GetDeclaringType());
            string typeName = _reader.GetString(type.Name);
            bool isIteratorType = typeName.StartsWith('<') && typeName.Contains(">d__", StringComparison.Ordinal)
                && type.GetCustomAttributes()
                    .Select(handle => _reader.GetCustomAttribute(handle).Constructor)
                    .Where(constructor => constructor.Kind == HandleKind.MemberReference)
                    .Select(constructor => ShapeOfParent(_reader.GetMemberReference((MemberReferenceHandle)constructor).Parent).DefinitionName)
                    .Contains(CompilerGeneratedAttribute, StringComparer.Ordinal);
            bool isPlumbingMethod = methodName == ".ctor" || methodName.EndsWith("GetEnumerator", StringComparison.Ordinal);
            return isIteratorType && isPlumbingMethod;
        }

        private IEnumerable<MethodDefinitionHandle> AllMethods() =>
            _reader.TypeDefinitions.SelectMany(type => _reader.GetTypeDefinition(type).GetMethods());

        private IEnumerable<Instruction> Instructions(MethodDefinitionHandle methodHandle)
        {
            MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                yield break;
            }

            byte[] il = _peReader.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? Array.Empty<byte>();
            foreach (Instruction instruction in DecodeInstructions(il, 0, il.Length, Describe(methodHandle)))
            {
                yield return instruction;
            }
        }

        private static IEnumerable<Instruction> DecodeInstructions(byte[] il, int startOffset, int endOffset, string where)
        {
            int offset = startOffset;
            while (offset < endOffset)
            {
                int start = offset;
                bool isTwoByte = il[offset] == 0xFE;
                short value = isTwoByte ? unchecked((short)(0xFE00 | il[offset + 1])) : il[offset];
                offset += isTwoByte ? 2 : 1;
                if (!OpCodesByValue.TryGetValue(value, out OpCode opCode))
                {
                    throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                        $"Unknown IL opcode 0x{value:X4} in {where} at IL_{start:X4}."));
                }

                EntityHandle operand = HasMetadataToken(opCode.OperandType)
                    ? MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset)))
                    : default;
                int operandStart = offset;
                offset += OperandSize(opCode.OperandType, il, offset);
                yield return new Instruction(start, opCode, operand)
                {
                    Variable = VariableIndex(opCode, il, operandStart),
                    Targets = BranchTargets(opCode.OperandType, il, operandStart, next: offset),
                };
            }
        }

        /// <summary>The argument or local an ldarg, ldloc, starg or stloc names (implied by ldarg.0…ldloc.3); -1 for any other instruction.</summary>
        private static int VariableIndex(OpCode opCode, byte[] il, int operandStart)
        {
            int implied = Array.IndexOf(ImpliedVariableOpCodes, opCode.Value);
            if (implied >= 0)
            {
                return implied % 4;
            }

            return opCode.OperandType switch
            {
                OperandType.ShortInlineVar => il[operandStart],
                OperandType.InlineVar => BinaryPrimitives.ReadUInt16LittleEndian(il.AsSpan(operandStart)),
                _ => -1,
            };
        }

        private static readonly short[] ImpliedVariableOpCodes =
        {
            OpCodes.Ldarg_0.Value, OpCodes.Ldarg_1.Value, OpCodes.Ldarg_2.Value, OpCodes.Ldarg_3.Value,
            OpCodes.Ldloc_0.Value, OpCodes.Ldloc_1.Value, OpCodes.Ldloc_2.Value, OpCodes.Ldloc_3.Value,
        };

        /// <summary>The offsets a branch or switch may jump to; empty for any other instruction.</summary>
        private static ImmutableArray<int> BranchTargets(OperandType operandType, byte[] il, int operandStart, int next)
        {
            switch (operandType)
            {
                case OperandType.ShortInlineBrTarget:
                    return ImmutableArray.Create(next + unchecked((sbyte)il[operandStart]));
                case OperandType.InlineBrTarget:
                    return ImmutableArray.Create(next + BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(operandStart)));
                case OperandType.InlineSwitch:
                    int count = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(operandStart));
                    return Enumerable.Range(0, count)
                        .Select(index => next + BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(operandStart + 4 + (4 * index))))
                        .ToImmutableArray();
                default:
                    return ImmutableArray<int>.Empty;
            }
        }

        private static bool HasMetadataToken(OperandType operandType) => operandType is
            OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok
            or OperandType.InlineType or OperandType.InlineSig;

        private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
                or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
                or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset))),
            _ => throw new InvalidDataException("Unsupported IL operand type " + operandType.ToString()),
        };

        private string Describe(MethodDefinitionHandle handle)
        {
            MethodDefinition method = _reader.GetMethodDefinition(handle);
            return FullName(method.GetDeclaringType()) + "::" + _reader.GetString(method.Name);
        }

        /// <summary>Whether a type reference resolves to a Unity-bound assembly of this repository (nested types through their outermost type).</summary>
        private bool IsUnityBoundSiblingType(TypeReferenceHandle handle)
        {
            EntityHandle scope = _reader.GetTypeReference(handle).ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
            {
                scope = _reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
            }

            return scope.Kind == HandleKind.AssemblyReference
                && ProjectRules.UnityBoundProjects.Contains(
                    _reader.GetString(_reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name), StringComparer.Ordinal);
        }

        private string FullName(TypeReferenceHandle handle)
        {
            TypeReference type = _reader.GetTypeReference(handle);
            string name = _reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return FullName((TypeReferenceHandle)type.ResolutionScope) + "+" + name;
            }

            string typeNamespace = _reader.GetString(type.Namespace);
            return typeNamespace.Length == 0 ? name : typeNamespace + "." + name;
        }

        private string FullName(TypeDefinitionHandle handle)
        {
            TypeDefinition type = _reader.GetTypeDefinition(handle);
            string name = _reader.GetString(type.Name);
            TypeDefinitionHandle declaringType = type.GetDeclaringType();
            if (!declaringType.IsNil)
            {
                return FullName(declaringType) + "+" + name;
            }

            string typeNamespace = _reader.GetString(type.Namespace);
            return typeNamespace.Length == 0 ? name : typeNamespace + "." + name;
        }

        private readonly record struct Instruction(int Offset, OpCode OpCode, EntityHandle Operand)
        {
            /// <summary>The argument or local index of ldarg/ldloc/starg/stloc and their short forms; -1 otherwise.</summary>
            public int Variable { get; init; }

            /// <summary>Where a branch or switch may jump; empty otherwise.</summary>
            public ImmutableArray<int> Targets { get; init; }
        }

        /// <summary>One instruction with the static types of the receiver and arguments it takes, when it is a call, callvirt or newobj.</summary>
        private readonly record struct FlowStep(Instruction Instruction, TypeShape? Instance, IReadOnlyList<TypeShape?> Arguments);

        /// <summary>
        /// Every instruction of the method in order, each call, callvirt and newobj with the static types of the values it
        /// takes. One forward walk tracks the evaluation stack: a value loaded from an argument, a local or a field, returned
        /// by a call, made by newobj or cast keeps its declared type; every other value is unknown (null). Where control
        /// merges, a slot keeps its type only when every path that reached it so far agrees. After an unconditional jump the
        /// walk resumes with the stack a forward branch recorded for the target, else with an empty stack (C# leaves the stack
        /// empty at statement boundaries such as loop heads). A value stored into a local of another type loses its own
        /// type, so the checks built on this see a value only where it is passed straight on.
        /// </summary>
        private IEnumerable<FlowStep> Flow(MethodDefinitionHandle methodHandle)
        {
            MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                yield break;
            }

            MethodBodyBlock body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            MethodSignature<TypeShape> signature = method.DecodeSignature(_shapes, null);
            IReadOnlyList<TypeShape> arguments = signature.Header.IsInstance
                ? signature.ParameterTypes.Prepend(ShapeOfDefinition(method.GetDeclaringType())).ToList()
                : signature.ParameterTypes;
            IReadOnlyList<TypeShape> locals = LocalTypes(methodHandle).ToList();
            var recorded = new Dictionary<int, IReadOnlyList<TypeShape?>>();
            foreach (ExceptionRegion region in body.ExceptionRegions)
            {
                // A try starts with an empty stack, a catch or filter with the exception, a finally or fault with nothing.
                Record(recorded, region.TryOffset, Array.Empty<TypeShape?>());
                bool receivesTheException = region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter;
                Record(recorded, region.HandlerOffset, receivesTheException ? new TypeShape?[] { null } : Array.Empty<TypeShape?>());
                if (region.Kind == ExceptionRegionKind.Filter)
                {
                    Record(recorded, region.FilterOffset, new TypeShape?[] { null });
                }
            }

            var stack = new List<TypeShape?>();
            bool fallsThrough = true;
            byte[] il = body.GetILBytes() ?? Array.Empty<byte>();
            foreach (Instruction instruction in DecodeInstructions(il, 0, il.Length, Describe(methodHandle)))
            {
                if (recorded.TryGetValue(instruction.Offset, out IReadOnlyList<TypeShape?>? atTarget))
                {
                    stack = (fallsThrough ? Merge(stack, atTarget) : atTarget).ToList();
                }
                else if (!fallsThrough)
                {
                    stack.Clear();
                }

                yield return Step(instruction, stack, arguments, locals);

                bool isLeave = instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S;
                foreach (int target in instruction.Targets)
                {
                    Record(recorded, target, isLeave ? Array.Empty<TypeShape?>() : stack.ToList());
                }

                fallsThrough = instruction.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw);
            }
        }

        /// <summary>Applies one instruction to the stack and returns what a call, callvirt or newobj took from it.</summary>
        private FlowStep Step(Instruction instruction, List<TypeShape?> stack, IReadOnlyList<TypeShape> arguments, IReadOnlyList<TypeShape> locals)
        {
            OpCode opCode = instruction.OpCode;
            bool isCall = opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj;
            if (isCall)
            {
                MethodSignature<TypeShape> called = CallSignature(instruction.Operand)
                    ?? throw new InvalidDataException("A call names no method: " + instruction.Operand.Kind.ToString());
                bool takesInstance = called.Header.IsInstance && opCode != OpCodes.Newobj;
                IReadOnlyList<TypeShape?> taken = Pop(stack, called.ParameterTypes.Length + (takesInstance ? 1 : 0));
                if (opCode == OpCodes.Newobj)
                {
                    stack.Add(ShapeOfParent(ParentOf(instruction.Operand)));
                }
                else if (called.ReturnType.DefinitionName != "System.Void")
                {
                    stack.Add(called.ReturnType);
                }

                return new FlowStep(instruction, takesInstance ? taken[0] : null, takesInstance ? taken.Skip(1).ToList() : taken);
            }

            if (opCode == OpCodes.Calli)
            {
                // The function pointer comes on top of the arguments.
                MethodSignature<TypeShape> pointed = _reader.GetStandaloneSignature((StandaloneSignatureHandle)instruction.Operand).DecodeMethodSignature(_shapes, null);
                Pop(stack, pointed.ParameterTypes.Length + (pointed.Header.IsInstance ? 1 : 0) + 1);
                if (pointed.ReturnType.DefinitionName != "System.Void")
                {
                    stack.Add(pointed.ReturnType);
                }

                return new FlowStep(instruction, null, Array.Empty<TypeShape?>());
            }

            if (opCode == OpCodes.Ret || opCode == OpCodes.Jmp)
            {
                // Control leaves the method; the walk resumes at the next jump target.
                stack.Clear();
                return new FlowStep(instruction, null, Array.Empty<TypeShape?>());
            }

            // Read before the pops: dup (Pop1, Push1_push1) pushes the value it pops twice.
            TypeShape? pushed = PushedType(instruction, stack, arguments, locals);
            Pop(stack, PopCount(opCode.StackBehaviourPop));
            int pushes = PushCount(opCode.StackBehaviourPush);
            for (int push = 0; push < pushes; push++)
            {
                stack.Add(pushed);
            }

            return new FlowStep(instruction, null, Array.Empty<TypeShape?>());
        }

        /// <summary>The declared type an instruction other than a call pushes, given the stack before it pops; null when unknown.</summary>
        private TypeShape? PushedType(Instruction instruction, List<TypeShape?> stack, IReadOnlyList<TypeShape> arguments, IReadOnlyList<TypeShape> locals)
        {
            OpCode opCode = instruction.OpCode;
            if (opCode == OpCodes.Dup)
            {
                return stack.Count > 0 ? stack[^1] : null;
            }

            bool loadsArgument = opCode == OpCodes.Ldarg_0 || opCode == OpCodes.Ldarg_1 || opCode == OpCodes.Ldarg_2 || opCode == OpCodes.Ldarg_3
                || opCode == OpCodes.Ldarg_S || opCode == OpCodes.Ldarg;
            bool loadsLocal = opCode == OpCodes.Ldloc_0 || opCode == OpCodes.Ldloc_1 || opCode == OpCodes.Ldloc_2 || opCode == OpCodes.Ldloc_3
                || opCode == OpCodes.Ldloc_S || opCode == OpCodes.Ldloc;
            if (loadsArgument || loadsLocal)
            {
                IReadOnlyList<TypeShape> variables = loadsArgument ? arguments : locals;
                return instruction.Variable >= 0 && instruction.Variable < variables.Count ? variables[instruction.Variable] : null;
            }

            if (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldsfld)
            {
                return FieldType(instruction.Operand);
            }

            bool castsTo = opCode == OpCodes.Castclass || opCode == OpCodes.Isinst || opCode == OpCodes.Unbox_Any;
            return castsTo ? ShapeOf(instruction.Operand) : null;
        }

        /// <summary>The signature of the method a call, callvirt or newobj names (generic parameters as placeholders); null for another operand.</summary>
        private MethodSignature<TypeShape>? CallSignature(EntityHandle operand) => operand.Kind switch
        {
            HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)operand).DecodeSignature(_shapes, null),
            HandleKind.MemberReference when _reader.GetMemberReference((MemberReferenceHandle)operand).GetKind() == MemberReferenceKind.Method =>
                _reader.GetMemberReference((MemberReferenceHandle)operand).DecodeMethodSignature(_shapes, null),
            HandleKind.MethodSpecification => CallSignature(_reader.GetMethodSpecification((MethodSpecificationHandle)operand).Method),
            _ => null,
        };

        /// <summary>The type that declares the method a newobj names.</summary>
        private EntityHandle ParentOf(EntityHandle operand) => operand.Kind switch
        {
            HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)operand).GetDeclaringType(),
            HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)operand).Parent,
            HandleKind.MethodSpecification => ParentOf(_reader.GetMethodSpecification((MethodSpecificationHandle)operand).Method),
            _ => throw new InvalidDataException("Not a method handle: " + operand.Kind.ToString()),
        };

        private TypeShape? FieldType(EntityHandle operand) => operand.Kind switch
        {
            HandleKind.FieldDefinition => _reader.GetFieldDefinition((FieldDefinitionHandle)operand).DecodeSignature(_shapes, null),
            HandleKind.MemberReference when _reader.GetMemberReference((MemberReferenceHandle)operand).GetKind() == MemberReferenceKind.Field =>
                _reader.GetMemberReference((MemberReferenceHandle)operand).DecodeFieldSignature(_shapes, null),
            _ => null,
        };

        /// <summary>Removes and returns the top <paramref name="count"/> values, deepest first; values the walk lost track of are null.</summary>
        private static IReadOnlyList<TypeShape?> Pop(List<TypeShape?> stack, int count)
        {
            int available = Math.Min(count, stack.Count);
            var taken = Enumerable.Repeat<TypeShape?>(null, count - available).Concat(stack.Skip(stack.Count - available)).ToList();
            stack.RemoveRange(stack.Count - available, available);
            return taken;
        }

        /// <summary>Keeps the stack recorded for a jump target, merged with any stack recorded for it before.</summary>
        private static void Record(Dictionary<int, IReadOnlyList<TypeShape?>> recorded, int target, IReadOnlyList<TypeShape?> stack) =>
            recorded[target] = recorded.TryGetValue(target, out IReadOnlyList<TypeShape?>? earlier) ? Merge(earlier, stack) : stack;

        /// <summary>The stack where two paths meet: a slot keeps its type when both agree; stacks of different depth (never in valid IL) become unknown.</summary>
        private static IReadOnlyList<TypeShape?> Merge(IReadOnlyList<TypeShape?> first, IReadOnlyList<TypeShape?> second) =>
            first.Count != second.Count
                ? Enumerable.Repeat<TypeShape?>(null, second.Count).ToList()
                : first.Zip(second, (one, other) => one is not null && other is not null && one.Display == other.Display ? one : null).ToList();

        private static int PopCount(StackBehaviour behaviour) => behaviour switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref or StackBehaviour.Popref_popi_pop1 => 3,
            _ => throw new InvalidDataException("Unexpected stack behaviour " + behaviour.ToString()),
        };

        private static int PushCount(StackBehaviour behaviour) => behaviour switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            _ => throw new InvalidDataException("Unexpected stack behaviour " + behaviour.ToString()),
        };

        /// <summary>Decodes signatures into <see cref="TypeShape"/>s.</summary>
        private sealed class ShapeProvider : ISignatureTypeProvider<TypeShape, object?>
        {
            private static readonly IReadOnlyDictionary<PrimitiveTypeCode, string> PrimitiveNames = new Dictionary<PrimitiveTypeCode, string>
            {
                [PrimitiveTypeCode.Boolean] = "System.Boolean", [PrimitiveTypeCode.Char] = "System.Char",
                [PrimitiveTypeCode.SByte] = "System.SByte", [PrimitiveTypeCode.Byte] = "System.Byte",
                [PrimitiveTypeCode.Int16] = "System.Int16", [PrimitiveTypeCode.UInt16] = "System.UInt16",
                [PrimitiveTypeCode.Int32] = "System.Int32", [PrimitiveTypeCode.UInt32] = "System.UInt32",
                [PrimitiveTypeCode.Int64] = "System.Int64", [PrimitiveTypeCode.UInt64] = "System.UInt64",
                [PrimitiveTypeCode.Single] = "System.Single", [PrimitiveTypeCode.Double] = "System.Double",
                [PrimitiveTypeCode.IntPtr] = "System.IntPtr", [PrimitiveTypeCode.UIntPtr] = "System.UIntPtr",
                [PrimitiveTypeCode.Object] = "System.Object", [PrimitiveTypeCode.String] = "System.String",
                [PrimitiveTypeCode.Void] = "System.Void", [PrimitiveTypeCode.TypedReference] = "System.TypedReference",
            };

            private readonly AssemblyInspection _owner;

            public ShapeProvider(AssemblyInspection owner) => _owner = owner;

            public TypeShape GetPrimitiveType(PrimitiveTypeCode typeCode)
            {
                string name = PrimitiveNames[typeCode];
                return new TypeShape(name, name, ImmutableArray<string>.Empty,
                    typeCode is PrimitiveTypeCode.Single or PrimitiveTypeCode.Double,
                    typeCode is PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr);
            }

            public TypeShape GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                string name = _owner.FullName(handle);
                // A sibling assembly's type is no external platform type (see ApiPolicyViolations).
                ImmutableArray<string> external = _owner.IsUnityBoundSiblingType(handle) ? ImmutableArray<string>.Empty : ImmutableArray.Create(name);
                return new TypeShape(name, name, external, FloatingPointTypeNames.Contains(name), false);
            }

            public TypeShape GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                string name = _owner.FullName(handle);
                return new TypeShape(name, name, ImmutableArray<string>.Empty, false, false);
            }

            public TypeShape GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
                reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

            public TypeShape GetSZArrayType(TypeShape elementType) => Wrap(elementType, elementType.Display + "[]", "System.Array");

            public TypeShape GetArrayType(TypeShape elementType, ArrayShape shape) =>
                Wrap(elementType, elementType.Display + "[" + new string(',', shape.Rank - 1) + "]", "System.Array");

            public TypeShape GetByReferenceType(TypeShape elementType) => Wrap(elementType, elementType.Display + "&", elementType.DefinitionName);

            public TypeShape GetPointerType(TypeShape elementType) => Wrap(elementType, elementType.Display + "*", elementType.DefinitionName);

            public TypeShape GetPinnedType(TypeShape elementType) => elementType;

            public TypeShape GetModifiedType(TypeShape modifier, TypeShape unmodifiedType, bool isRequired) => unmodifiedType;

            public TypeShape GetGenericInstantiation(TypeShape genericType, ImmutableArray<TypeShape> typeArguments) => new(
                genericType.Display + "<" + string.Join(",", typeArguments.Select(argument => argument.Display)) + ">",
                genericType.DefinitionName,
                genericType.ExternalTypes.AddRange(typeArguments.SelectMany(argument => argument.ExternalTypes)),
                genericType.UsesFloatingPoint || typeArguments.Any(argument => argument.UsesFloatingPoint),
                genericType.UsesNativeInteger || typeArguments.Any(argument => argument.UsesNativeInteger))
            {
                TypeArguments = typeArguments,
            };

            public TypeShape GetGenericMethodParameter(object? genericContext, int index) =>
                Placeholder("!!" + index.ToString(CultureInfo.InvariantCulture));

            public TypeShape GetGenericTypeParameter(object? genericContext, int index) =>
                Placeholder("!" + index.ToString(CultureInfo.InvariantCulture));

            public TypeShape GetFunctionPointerType(MethodSignature<TypeShape> signature) => new(
                "method", "method",
                signature.ParameterTypes.Append(signature.ReturnType).SelectMany(part => part.ExternalTypes).ToImmutableArray(),
                UsesFloatingPoint(signature),
                signature.ReturnType.UsesNativeInteger || signature.ParameterTypes.Any(part => part.UsesNativeInteger));

            private static TypeShape Wrap(TypeShape element, string display, string definitionName) =>
                element with { Display = display, DefinitionName = definitionName, TypeArguments = ImmutableArray<TypeShape>.Empty };

            private static TypeShape Placeholder(string name) => new(name, name, ImmutableArray<string>.Empty, false, false);
        }
    }

    /// <summary>The calling method, needed by bans that exempt GetHashCode overrides and record members the compiler writes.</summary>
    internal sealed record CallerContext(string MethodName, bool IsSynthesizedRecordMember, bool IsIteratorPlumbing);

    /// <summary>Which platform types and members an assembly may use: <see cref="UnityBoundApiPolicy"/> or <see cref="GeneratorContentPolicy"/>.</summary>
    internal interface IPlatformApiPolicy
    {
        /// <summary>Completes "type …" and "uses T, which …" for a refused type, for example "is not on the Unity-bound allowlist".</summary>
        string RefusedTypeReason { get; }

        bool IsTypeAllowed(string typeName);

        /// <summary>Why this use of an allowed type's member breaks the policy; nothing when it does not.</summary>
        IEnumerable<string> MemberViolations(MemberUse use, CallerContext caller);
    }
}
