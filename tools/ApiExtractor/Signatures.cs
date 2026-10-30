using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace ApiExtractor
{
    /// <summary>Declarações em C# para mostrar no topo de cada tipo e membro.</summary>
    internal static class Signatures
    {
        public static string ForType(Type type)
        {
            if (Reflection.IsDelegate(type)) return ForDelegate(type);
            var parts = new List<string> { type.IsNestedFamily || type.IsNestedFamORAssem ? "protected" : "public" };
            string modifier = TypeModifier(type);
            if (modifier != null) parts.Add(modifier);
            parts.Add(TypeKeyword(type));
            parts.Add(TypeNames.Declaration(type));
            string signature = string.Join(" ", parts);
            var bases = BaseList(type);
            return bases.Count == 0 ? signature : signature + " : " + string.Join(", ", bases);
        }

        public static string ForMethod(MethodInfo method)
        {
            var parts = new List<string> { Accessibility(method) };
            string modifier = MethodModifier(method);
            if (modifier != null) parts.Add(modifier);
            parts.Add(TypeNames.Display(method.ReturnType));
            string name = method.Name;
            if (method.IsGenericMethodDefinition)
                name += "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">";
            return string.Join(" ", parts) + " " + name + "(" + ParameterList(method) + ")";
        }

        public static string ForConstructor(ConstructorInfo constructor)
            => Accessibility(constructor) + " " + TypeNames.StripArity(constructor.DeclaringType.Name) + "(" + ParameterList(constructor) + ")";

        public static string ForProperty(PropertyInfo property)
        {
            var main = Reflection.MainAccessor(property);
            var parts = new List<string> { Accessibility(main) };
            string modifier = MethodModifier(main);
            if (modifier != null) parts.Add(modifier);
            parts.Add(TypeNames.Display(property.PropertyType));
            var index = property.GetIndexParameters();
            parts.Add(index.Length == 0 ? property.Name : "this[" + string.Join(", ", index.Select(p => Parameter(p, false))) + "]");
            return string.Join(" ", parts) + " { " + Accessors(property, main) + " }";
        }

        public static string ForEvent(EventInfo evento)
        {
            var add = evento.AddMethod;
            string modifier = MethodModifier(add);
            return Accessibility(add) + (modifier != null ? " " + modifier : "") + " event "
                + TypeNames.Display(evento.EventHandlerType) + " " + evento.Name;
        }

        public static string ForField(FieldInfo field)
        {
            var parts = new List<string> { field.IsPublic ? "public" : "protected" };
            if (field.IsLiteral) parts.Add("const");
            else
            {
                if (field.IsStatic) parts.Add("static");
                if (field.IsInitOnly) parts.Add("readonly");
            }
            parts.Add(TypeNames.Display(field.FieldType));
            parts.Add(field.Name);
            string signature = string.Join(" ", parts);
            return field.IsLiteral ? signature + " = " + Literal(field.GetRawConstantValue(), field.FieldType) : signature;
        }

        public static string Literal(object value, Type type)
        {
            if (value == null) return type.IsGenericParameter || (type.IsValueType && !IsNullable(type)) ? "default" : "null";
            if (type.IsEnum) return EnumLiteral(value, type);
            switch (value)
            {
                case string text: return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case bool flag: return flag ? "true" : "false";
                case char letter: return "'" + letter + "'";
                case float single: return single.ToString("R", CultureInfo.InvariantCulture) + "f";
                case double number: return number.ToString("R", CultureInfo.InvariantCulture);
                case decimal money: return money.ToString(CultureInfo.InvariantCulture) + "m";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        static string ForDelegate(Type type)
        {
            var invoke = type.GetMethod("Invoke");
            return "public delegate " + TypeNames.Display(invoke.ReturnType) + " " + TypeNames.Declaration(type) + "(" + ParameterList(invoke) + ")";
        }

        static string TypeModifier(Type type)
        {
            if (type.IsInterface || type.IsValueType) return null;
            if (type.IsAbstract && type.IsSealed) return "static";
            if (type.IsAbstract) return "abstract";
            return type.IsSealed ? "sealed" : null;
        }

        static string TypeKeyword(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            return type.IsValueType ? "struct" : "class";
        }

        static List<string> BaseList(Type type)
        {
            var bases = new List<string>();
            if (type.IsEnum)
            {
                var underlying = Reflection.Safe(() => type.GetEnumUnderlyingType());
                if (underlying != null && underlying.FullName != "System.Int32") bases.Add(TypeNames.Display(underlying));
                return bases;
            }
            var baseType = Reflection.Safe(() => type.BaseType);
            if (baseType != null && !type.IsValueType && baseType.FullName != "System.Object") bases.Add(TypeNames.Display(baseType));
            var inherited = baseType != null ? Reflection.Safe(() => baseType.GetInterfaces()) ?? Type.EmptyTypes : Type.EmptyTypes;
            var own = Reflection.Safe(() => type.GetInterfaces()) ?? Type.EmptyTypes;
            bases.AddRange(own.Where(i => !inherited.Contains(i) && i.IsVisible).Select(TypeNames.Display));
            return bases;
        }

        static string Accessibility(MethodBase method) => method.IsPublic ? "public" : "protected";

        static string MethodModifier(MethodInfo method)
        {
            if (method.IsStatic) return "static";
            if (method.DeclaringType.IsInterface) return null;
            bool isOverride = method.IsVirtual && Reflection.IsOverride(method);
            if (method.IsAbstract) return isOverride ? "abstract override" : "abstract";
            if (isOverride) return method.IsFinal ? "sealed override" : "override";
            return method.IsVirtual && !method.IsFinal ? "virtual" : null;
        }

        static string Accessors(PropertyInfo property, MethodInfo main)
        {
            var accessors = new List<string>();
            AddAccessor(accessors, property.GetMethod, "get", main);
            AddAccessor(accessors, property.SetMethod, Reflection.IsInitOnly(property.SetMethod) ? "init" : "set", main);
            return string.Join(" ", accessors);
        }

        static void AddAccessor(List<string> accessors, MethodInfo accessor, string keyword, MethodInfo main)
        {
            if (!Reflection.IsVisible(accessor)) return;
            bool narrower = !accessor.IsPublic && main.IsPublic;
            accessors.Add((narrower ? "protected " : "") + keyword + ";");
        }

        static string ParameterList(MethodBase method)
        {
            bool extension = method.IsStatic && Reflection.HasAttribute(method, "System.Runtime.CompilerServices.ExtensionAttribute");
            return string.Join(", ", method.GetParameters().Select((p, i) => Parameter(p, extension && i == 0)));
        }

        static string Parameter(ParameterInfo parameter, bool isThis)
        {
            var parts = new List<string>();
            if (isThis) parts.Add("this");
            if (Reflection.HasAttribute(parameter, "System.ParamArrayAttribute")) parts.Add("params");
            if (parameter.ParameterType.IsByRef) parts.Add(ByRefKeyword(parameter));
            parts.Add(TypeNames.Display(parameter.ParameterType));
            parts.Add(parameter.Name);
            string text = string.Join(" ", parts);
            return parameter.HasDefaultValue ? text + " = " + Literal(parameter.RawDefaultValue, parameter.ParameterType) : text;
        }

        static string ByRefKeyword(ParameterInfo parameter)
        {
            if (parameter.IsOut) return "out";
            return Reflection.HasAttribute(parameter, "System.Runtime.CompilerServices.IsReadOnlyAttribute") ? "in" : "ref";
        }

        static string EnumLiteral(object value, Type type)
        {
            var match = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(f => f.IsLiteral && Equals(f.GetRawConstantValue(), value));
            string name = TypeNames.Display(type);
            return match != null ? name + "." + match.Name : "(" + name + ")" + Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static bool IsNullable(Type type) => type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1";
    }
}
