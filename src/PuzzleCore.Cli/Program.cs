using System;

namespace PuzzleCore.Cli
{
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            Console.Out.Write("puzzle-engine CLI: no commands yet\n");
            return arguments.Length == 0 ? 0 : 2;
        }
    }
}
