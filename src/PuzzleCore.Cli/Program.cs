using System;

namespace PuzzleCore.Cli
{
    internal static class Program
    {
        private const string Usage = "usage:\n  PuzzleCore.Cli demo\n  PuzzleCore.Cli replay <level.pc-lot> <record.json>\n";

        private static int Main(string[] arguments)
        {
            if (arguments.Length == 1 && arguments[0] == "demo")
            {
                return DemoCommand.Run(Console.In, Console.Out);
            }

            if (arguments.Length == 3 && arguments[0] == "replay")
            {
                return ReplayCommand.Run(arguments[1], arguments[2], Console.Out);
            }

            Console.Error.Write(Usage);
            return 2;
        }
    }
}
