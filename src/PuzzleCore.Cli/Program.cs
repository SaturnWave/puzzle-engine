using System;

namespace PuzzleCore.Cli
{
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            if (arguments.Length == 1 && arguments[0] == "demo")
            {
                return DemoCommand.Run(Console.In, Console.Out);
            }

            Console.Error.Write("usage: PuzzleCore.Cli demo\n");
            return 2;
        }
    }
}
