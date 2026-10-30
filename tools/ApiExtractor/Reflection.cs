using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ApiExtractor
{
    /// <summary>
    /// Consultas de reflexão que funcionam no MetadataLoadContext. Lá os atributos e tipos de outras DLLs só existem como
    /// metadados e são comparados pelo nome; se a DLL referenciada não estiver nas referências, a consulta lança.
    /// </summary>
    internal static class Reflection
    {
        public static T Safe<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception e) when (IsResolutionFailure(e)) { return null; }
        }

        public static bool IsResolutionFailure(Exception e)
            => e is FileNotFoundException || e is TypeLoadException || e is BadImageFormatException;

        public static bool HasAttribute(MemberInfo member, string fullName)
        {
            try { return member.CustomAttributes.Any(a => a.AttributeType.FullName == fullName); }
            catch (Exception e) when (IsResolutionFailure(e)) { return false; }
        }

        public static bool HasAttribute(ParameterInfo parameter, string fullName)
        {
            try { return parameter.CustomAttributes.Any(a => a.AttributeType.FullName == fullName); }
            catch (Exception e) when (IsResolutionFailure(e)) { return false; }
        }

        public static string ObsoleteMessage(MemberInfo member)
        {
            try
            {
                var attribute = member.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.ObsoleteAttribute");
                if (attribute == null) return null;
                var message = attribute.ConstructorArguments.Select(a => a.Value).OfType<string>().FirstOrDefault();
                return string.IsNullOrWhiteSpace(message) ? "" : message;
            }
            catch (Exception e) when (IsResolutionFailure(e)) { return null; }
        }

        public static bool IsDelegate(Type type)
            => Safe(() => type.BaseType)?.FullName == "System.MulticastDelegate";

        public static bool IsVisible(MethodBase method)
            => method != null && (method.IsPublic || ((method.IsFamily || method.IsFamilyOrAssembly) && !method.DeclaringType.IsSealed));

        public static bool IsVisible(FieldInfo field)
            => field.IsPublic || ((field.IsFamily || field.IsFamilyOrAssembly) && !field.DeclaringType.IsSealed);

        /// <summary>O acessor que define a visibilidade da propriedade: o mais aberto dos dois.</summary>
        public static MethodInfo MainAccessor(PropertyInfo property)
        {
            var get = property.GetMethod;
            var set = property.SetMethod;
            if (!IsVisible(get)) return set;
            if (!IsVisible(set)) return get;
            return set.IsPublic && !get.IsPublic ? set : get;
        }

        // O MetadataLoadContext não tem GetBaseDefinition. No IL, virtual sem NewSlot reaproveita o slot da base: é override.
        public static bool IsOverride(MethodInfo method)
            => method.IsVirtual && (method.Attributes & MethodAttributes.VtableLayoutMask) != MethodAttributes.NewSlot;

        public static bool IsInitOnly(MethodInfo setter)
        {
            if (setter == null) return false;
            var modifiers = Safe(() => setter.ReturnParameter.GetRequiredCustomModifiers()) ?? Type.EmptyTypes;
            return modifiers.Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }
    }
}
