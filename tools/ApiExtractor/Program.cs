using System;
using System.IO;

namespace ApiExtractor
{
    internal static class Program
    {
        public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            Options options;
            try { options = Options.Parse(args); }
            catch (ArgumentException e)
            {
                error.WriteLine(e.Message);
                error.WriteLine(Options.Usage);
                return 2;
            }

            try { return Extraction.Run(options, output, error); }
            catch (Exception e) when (e is IOException || e is BadImageFormatException || e is UnauthorizedAccessException)
            {
                error.WriteLine("error: " + e.Message);
                return 1;
            }
        }
    }
}
