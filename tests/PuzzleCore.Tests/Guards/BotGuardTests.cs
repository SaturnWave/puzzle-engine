using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PuzzleCore.Bot;
using PuzzleCore.Tests.Support;
using Xunit;

namespace PuzzleCore.Tests.Guards
{
    /// <summary>
    /// The bots' guards (Q75, TC21): the shared bot namespace stays free of game types, and among Covered Lot's bots only
    /// the colour view, Greedy and Expert read a vehicle's own colour or anything worked out from it.
    /// </summary>
    public sealed class BotGuardTests
    {
        private const string SharedNamespace = "PuzzleCore.Bot";

        // A game's types: Jam's and Sort's modules, and any namespace under PuzzleCore.Bot other than the shared root.
        private static readonly Regex GameType = new(@"\bPuzzleCore\.(Jam|Sort)\.|\bPuzzleCore\.Bot\.\w+\.", RegexOptions.CultureInvariant);

        private static readonly string[] MayReadTrueColours =
        {
            "PuzzleCore.Bot.Jam.ColourView", "PuzzleCore.Bot.Jam.GreedyPolicy", "PuzzleCore.Bot.Jam.ExpertPolicy",
        };

        /// <summary>
        /// What shows a hidden vehicle's own colour, or something worked out from it, and who else may use it. "*" is any
        /// member of the type. Besides the listed callers, the colour view, Greedy and Expert may use all of them.
        /// </summary>
        private static readonly (string Type, string Member, string[] AlsoAllowed)[] ColourRevealing =
        {
            ("PuzzleCore.Jam.Vehicle", "get_Colour", Array.Empty<string>()),
            ("PuzzleCore.Jam.Vehicle", "Equals", Array.Empty<string>()),
            ("PuzzleCore.Jam.Vehicle", "op_Equality", Array.Empty<string>()),
            ("PuzzleCore.Jam.Vehicle", "op_Inequality", Array.Empty<string>()),
            ("PuzzleCore.Jam.LotText", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.JamRules", "TryApply", Array.Empty<string>()),
            ("PuzzleCore.Rules.IPuzzleRules`3", "TryApply", Array.Empty<string>()),
            // A move played on the rules shows the lifted cover's colour: only the shared run loop plays them.
            ("PuzzleCore.Play.Attempt`3", "Move", new[] { "PuzzleCore.Bot.BotRunner" }),
            // Full-information solves and grades.
            ("PuzzleCore.Play.FailAnalysis", "*", new[] { "PuzzleCore.Bot.BotRun`3" }),
            ("PuzzleCore.Search.IPositionSolver`2", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.LotSolver", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.LotPositionSolver", "*", Array.Empty<string>()),
            ("PuzzleCore.Play.Replay", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.LotPlayouts", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.LotFeatures", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.LotGrades", "*", Array.Empty<string>()),
            ("PuzzleCore.Jam.BuiltLevel", "get_ParPath", new[] { "PuzzleCore.Bot.Jam.LotBots" }),
            // Greedy and Expert read true colours, so what they work out may reach no other bot.
            ("PuzzleCore.Bot.Jam.GreedyPolicy", "*", new[] { "PuzzleCore.Bot.Jam.LotBots" }),
            ("PuzzleCore.Bot.Jam.ExpertPolicy", "*", new[] { "PuzzleCore.Bot.Jam.LotBots" }),
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
        public void The_shared_bot_namespace_mentions_no_game_type_in_its_code()
        {
            // Calls, typeof, is, casts, locals and generic arguments: every type a method body names.
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath("PuzzleCore.Bot"));
            IReadOnlyList<(string CallerType, string Type)> mentions = inspection.TypeMentions();

            IEnumerable<string> fromShared = mentions
                .Where(mention => NamespaceOf(mention.CallerType) == SharedNamespace && GameType.IsMatch(mention.Type))
                .Select(mention => mention.CallerType + " mentions " + mention.Type);

            Assert.Empty(fromShared);
            Assert.Contains(mentions, mention => mention.CallerType == "PuzzleCore.Bot.Jam.PlannerPolicy" && mention.Type == "PuzzleCore.Jam.LotState");
            Assert.Contains(mentions, mention => mention.CallerType == "PuzzleCore.Bot.BotRunner" && mention.Type.StartsWith("PuzzleCore.Play.Attempt`3", StringComparison.Ordinal));
        }

        [Fact]
        public void Only_the_colour_view_greedy_and_expert_read_a_vehicles_own_colour()
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath("PuzzleCore.Bot"));
            List<(MemberReferenceSite Site, string[] AlsoAllowed)> revealing = inspection.MemberReferences()
                .Select(site => (Site: site, Rule: ColourRevealing.FirstOrDefault(rule => rule.Type == site.DeclaringType && (rule.Member == "*" || rule.Member == site.MemberName))))
                .Where(found => found.Rule.Type is not null)
                .Select(found => (found.Site, found.Rule.AlsoAllowed))
                .ToList();

            Assert.Empty(revealing.Where(found => !MayUse(found.Site.CallerType, found.AlsoAllowed)).Select(found => found.Site.ToString()));
            Assert.Contains(revealing, found => found.Site.CallerType == "PuzzleCore.Bot.Jam.ColourView" && found.Site.MemberName == "get_Colour");
            Assert.Contains(revealing, found => found.Site.CallerType == "PuzzleCore.Bot.Jam.GreedyPolicy" && found.Site.MemberName == "get_Colour");
            Assert.Contains(revealing, found => found.Site.CallerType == "PuzzleCore.Bot.BotRunner" && found.Site.MemberName == "Move");
            Assert.Contains(revealing, found => found.Site.CallerType == "PuzzleCore.Bot.Jam.LotBots" && found.Site.MemberName == "get_ParPath");
        }

        /// <summary>The colour view, Greedy, Expert and the rule's own callers, or a type nested in one (a lambda's closure class).</summary>
        private static bool MayUse(string callerType, string[] alsoAllowed) =>
            MayReadTrueColours.Concat(alsoAllowed).Any(allowed => callerType == allowed || callerType.StartsWith(allowed + "+", StringComparison.Ordinal));

        private static bool IsGameType(Type type) => type.FullName is string name && GameType.IsMatch(name);

        /// <summary>The namespace of a type named as the IL reports it ("A.B.Outer+Nested" is in "A.B").</summary>
        private static string NamespaceOf(string typeName)
        {
            string outer = typeName.Split('+')[0];
            int dot = outer.LastIndexOf('.');
            return dot < 0 ? string.Empty : outer.Substring(0, dot);
        }

        /// <summary>
        /// Every type a type's own declarations name: base, interfaces, the constraints on its generic parameters, fields,
        /// properties, events and methods, with their generic parameters' constraints.
        /// </summary>
        private static IEnumerable<Type> TypesInSignaturesOf(Type type)
        {
            const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var named = new List<Type>();
            if (type.BaseType is Type baseType)
            {
                named.Add(baseType);
            }

            named.AddRange(type.GetInterfaces());
            if (type.IsGenericTypeDefinition)
            {
                named.AddRange(type.GetGenericArguments().SelectMany(argument => argument.GetGenericParameterConstraints()));
            }

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
