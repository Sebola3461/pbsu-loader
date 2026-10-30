using System;
using System.Collections.Generic;
using System.Linq;

namespace ApiExtractor
{
    /// <summary>Nomes de tipo como aparecem em código C# (<c>float</c>, <c>List&lt;string&gt;</c>, <c>int?</c>).</summary>
    internal static class TypeNames
    {
        static readonly Dictionary<string, string> Keywords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Void"] = "void", ["System.Boolean"] = "bool", ["System.Byte"] = "byte", ["System.SByte"] = "sbyte",
            ["System.Int16"] = "short", ["System.UInt16"] = "ushort", ["System.Int32"] = "int", ["System.UInt32"] = "uint",
            ["System.Int64"] = "long", ["System.UInt64"] = "ulong", ["System.Single"] = "float", ["System.Double"] = "double",
            ["System.Decimal"] = "decimal", ["System.Char"] = "char", ["System.String"] = "string", ["System.Object"] = "object",
        };

        public static string Display(Type type)
        {
            if (type.IsByRef) return Display(type.GetElementType());
            if (type.IsPointer) return Display(type.GetElementType()) + "*";
            if (type.IsArray) return Display(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (type.IsGenericParameter) return type.Name;
            if (type.FullName != null && Keywords.TryGetValue(type.FullName, out string keyword)) return keyword;
            if (type.IsGenericType) return GenericDisplay(type);
            return Prefix(type) + type.Name;
        }

        /// <summary>Nome na declaração do próprio tipo, sem o tipo que o contém: <c>Gaveta&lt;T&gt;</c>.</summary>
        public static string Declaration(Type type)
        {
            string name = StripArity(type.Name);
            int arity = OwnArity(type.Name);
            if (arity == 0) return name;
            var arguments = type.GetGenericArguments();
            return name + "<" + string.Join(", ", arguments.Skip(arguments.Length - arity).Select(Display)) + ">";
        }

        public static string StripArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        static string GenericDisplay(Type type)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition.FullName == "System.Nullable`1") return Display(arguments[0]) + "?";
            if (definition.FullName != null && definition.FullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
                return "(" + string.Join(", ", arguments.Select(Display)) + ")";

            // Num tipo aninhado em genérico, os argumentos do tipo de fora vêm primeiro.
            int arity = OwnArity(type.Name);
            string name = StripArity(type.Name);
            if (arity == 0) return Prefix(type) + name;
            return Prefix(type) + name + "<" + string.Join(", ", arguments.Skip(arguments.Length - arity).Select(Display)) + ">";
        }

        static string Prefix(Type type) => type.IsNested ? Display(type.DeclaringType) + "." : "";

        static int OwnArity(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? 0 : int.Parse(name.Substring(tick + 1), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
