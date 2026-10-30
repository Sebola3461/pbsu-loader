using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ApiExtractor
{
    /// <summary>
    /// IDs no formato do compilador (<c>M:Ns.Tipo.Metodo(System.Single)</c>), os mesmos do arquivo XML. Se divergirem, o
    /// membro sai sem documentação.
    /// </summary>
    internal static class DocumentationIds
    {
        static readonly Regex Arity = new Regex(@"`\d+", RegexOptions.Compiled);

        public static string ForType(Type type) => "T:" + TypeName(type);

        public static string ForMethod(MethodBase method)
        {
            string name = method.IsConstructor ? (method.IsStatic ? "#cctor" : "#ctor") : method.Name;
            if (method.IsGenericMethodDefinition) name += "``" + method.GetGenericArguments().Length;
            string id = "M:" + TypeName(method.DeclaringType) + "." + name + Parameters(method.GetParameters());
            if (method is MethodInfo info && (method.Name == "op_Implicit" || method.Name == "op_Explicit"))
                id += "~" + ParameterTypeName(info.ReturnType);
            return id;
        }

        public static string ForProperty(PropertyInfo property)
            => "P:" + TypeName(property.DeclaringType) + "." + property.Name + Parameters(property.GetIndexParameters());

        public static string ForField(FieldInfo field) => "F:" + TypeName(field.DeclaringType) + "." + field.Name;

        public static string ForEvent(EventInfo evento) => "E:" + TypeName(evento.DeclaringType) + "." + evento.Name;

        static string TypeName(Type type) => (type.FullName ?? type.Namespace + "." + type.Name).Replace('+', '.');

        static string Parameters(ParameterInfo[] parameters)
            => parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(p => ParameterTypeName(p.ParameterType))) + ")";

        static string ParameterTypeName(Type type)
        {
            if (type.IsByRef) return ParameterTypeName(type.GetElementType()) + "@";
            if (type.IsPointer) return ParameterTypeName(type.GetElementType()) + "*";
            if (type.IsArray) return ParameterTypeName(type.GetElementType()) + ArraySuffix(type.GetArrayRank());
            if (type.IsGenericParameter) return (type.DeclaringMethod != null ? "``" : "`") + type.GenericParameterPosition;
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                string definition = Arity.Replace(TypeName(type.GetGenericTypeDefinition()), "");
                return definition + "{" + string.Join(",", type.GetGenericArguments().Select(ParameterTypeName)) + "}";
            }
            return TypeName(type);
        }

        static string ArraySuffix(int rank)
            => rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";
    }
}
