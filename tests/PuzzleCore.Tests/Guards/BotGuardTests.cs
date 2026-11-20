using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PuzzleCore.Bot;
using PuzzleCore.Tests.Support;
using Xunit;

namespace PuzzleCore.Tests.Guards
{
    /// <summary>
    /// The bots' guards (Q75, TC21): the shared bot namespace stays free of game types, and among Covered Lot's bots only
    /// the colour view, Greedy and Expert read a vehicle's own colour.
    /// </summary>
    public sealed class BotGuardTests
    {
        private const string SharedNamespace = "PuzzleCore.Bot";

        private static readonly string[] GameNamespaces = { "PuzzleCore.Jam", "PuzzleCore.Bot.Jam", "PuzzleCore.Sort" };

        /// <summary>
        /// What shows a hidden vehicle's own colour: the vehicle's colour, the canonical level text, and a move played by the
        /// rules (its events and the bays after it show the colour). "*" is any member of the type.
        /// </summary>
        private static readonly (string Type, string Member)[] ColourRevealing =
        {
            ("PuzzleCore.Jam.Vehicle", "get_Colour"),
            ("PuzzleCore.Jam.LotText", "*"),
            ("PuzzleCore.Jam.JamRules", "TryApply"),
            ("PuzzleCore.Rules.IPuzzleRules`3", "TryApply"),
        };

        private static readonly string[] MayReadTrueColours =
        {
            "PuzzleCore.Bot.Jam.ColourView", "PuzzleCore.Bot.Jam.GreedyPolicy", "PuzzleCore.Bot.Jam.ExpertPolicy",
        };

        [Fact]
        public void The_shared_bot_namespace_names_no_game_type_in_a_signature()
        {
            IEnumerable<string> violations = typeof(BotRunner).Assembly.GetTypes()
                .Where(type => type.Namespace == SharedNamespace)
                .SelectMany(type => TypesInSignaturesOf(type).Where(IsGameType).Select(mentioned => type.FullName + " names " + mentioned.FullName))
                .Distinct();

            Assert.Empty(violations);
        }

        [Fact]
        public void The_shared_bot_namespace_calls_no_game_member()
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath("PuzzleCore.Bot"));
            IReadOnlyList<MemberReferenceSite> sites = inspection.MemberReferences();

            IEnumerable<MemberReferenceSite> fromShared = sites.Where(site => NamespaceOf(site.CallerType) == SharedNamespace && IsGameTypeName(site.DeclaringType));

            Assert.Empty(fromShared);
            Assert.Contains(sites, site => NamespaceOf(site.CallerType) == "PuzzleCore.Bot.Jam" && IsGameTypeName(site.DeclaringType));
        }

        [Fact]
        public void Only_the_colour_view_greedy_and_expert_read_a_vehicles_own_colour()
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath("PuzzleCore.Bot"));
            List<MemberReferenceSite> revealing = inspection.MemberReferences().Where(Reveals).ToList();

            Assert.Empty(revealing.Where(site => !MayReadTrueColoursFrom(site.CallerType)).Select(site => site.ToString()));
            Assert.Contains(revealing, site => site.CallerType == "PuzzleCore.Bot.Jam.ColourView");
            Assert.Contains(revealing, site => site.CallerType == "PuzzleCore.Bot.Jam.GreedyPolicy");
        }

        private static bool Reveals(MemberReferenceSite site) =>
            ColourRevealing.Any(member => member.Type == site.DeclaringType && (member.Member == "*" || member.Member == site.MemberName));

        /// <summary>The type itself or a type nested in it, such as a lambda's closure class.</summary>
        private static bool MayReadTrueColoursFrom(string callerType) =>
            MayReadTrueColours.Any(allowed => callerType == allowed || callerType.StartsWith(allowed + "+", StringComparison.Ordinal));

        private static bool IsGameType(Type type) => type.FullName is string name && IsGameTypeName(name);

        private static bool IsGameTypeName(string typeName) =>
            GameNamespaces.Any(game => typeName.StartsWith(game + ".", StringComparison.Ordinal));

        /// <summary>The namespace of a type named as the IL reports it ("A.B.Outer+Nested" is in "A.B").</summary>
        private static string NamespaceOf(string typeName)
        {
            string outer = typeName.Split('+')[0];
            int dot = outer.LastIndexOf('.');
            return dot < 0 ? string.Empty : outer.Substring(0, dot);
        }

        /// <summary>Every type a type's own declarations name: base, interfaces, fields, properties, events and methods.</summary>
        private static IEnumerable<Type> TypesInSignaturesOf(Type type)
        {
            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var named = new List<Type>();
            if (type.BaseType is Type baseType)
            {
                named.Add(baseType);
            }

            named.AddRange(type.GetInterfaces());
            named.AddRange(type.GetFields(Declared).Select(field => field.FieldType));
            named.AddRange(type.GetProperties(Declared).Select(property => property.PropertyType));
            named.AddRange(type.GetEvents(Declared).Select(declared => declared.EventHandlerType).OfType<Type>());
            foreach (MethodBase method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            {
                named.AddRange(method.GetParameters().Select(parameter => parameter.ParameterType));
                if (method is MethodInfo withReturn)
                {
                    named.Add(withReturn.ReturnType);
                }

                if (method.IsGenericMethodDefinition)
                {
                    named.AddRange(method.GetGenericArguments().SelectMany(argument => argument.GetGenericParameterConstraints()));
                }
            }

            return named.SelectMany(Unwrapped);
        }

        /// <summary>The type and every type inside it: array and reference element types, and generic arguments.</summary>
        private static IEnumerable<Type> Unwrapped(Type type)
        {
            yield return type;
            if (type.HasElementType && type.GetElementType() is Type element)
            {
                foreach (Type inner in Unwrapped(element))
                {
                    yield return inner;
                }
            }

            if (type.IsGenericType)
            {
                foreach (Type inner in type.GetGenericArguments().SelectMany(Unwrapped))
                {
                    yield return inner;
                }
            }
        }
    }
}
