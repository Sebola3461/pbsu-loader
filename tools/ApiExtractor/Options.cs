using System;
using System.Collections.Generic;

namespace ApiExtractor
{
    internal sealed class Options
    {
        public const string Usage =
            "usage: ApiExtractor --dll <file.dll> --output <file.api.json> [--xml <file.xml>] [--version <x.y.z>]\n" +
            "                    [--references <dir>[;<dir>...]]... [--exclude-base <Full.Type.Name>]... [--strict]";

        public string DllPath { get; private set; }
        public string XmlPath { get; private set; }
        public string OutputPath { get; private set; }
        public string Version { get; private set; }
        public bool Strict { get; private set; }
        public List<string> ReferenceDirectories { get; } = new List<string>();

        // A classe principal de um mod MelonLoader é pública, mas não é API para outros mods.
        public List<string> ExcludedBaseTypes { get; } = new List<string> { "MelonLoader.MelonBase" };

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dll": options.DllPath = Value(args, ref i); break;
                    case "--xml": options.XmlPath = Value(args, ref i); break;
                    case "--output": options.OutputPath = Value(args, ref i); break;
                    case "--version": options.Version = Value(args, ref i); break;
                    case "--references": options.ReferenceDirectories.AddRange(Value(args, ref i).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                    case "--exclude-base": options.ExcludedBaseTypes.Add(Value(args, ref i)); break;
                    case "--strict": options.Strict = true; break;
                    default: throw new ArgumentException("unknown argument: " + args[i]);
                }
            }
            if (options.DllPath == null) throw new ArgumentException("missing --dll");
            if (options.OutputPath == null) throw new ArgumentException("missing --output");
            return options;
        }

        static string Value(string[] args, ref int index)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("missing value for " + args[index]);
            return args[++index];
        }
    }
}
