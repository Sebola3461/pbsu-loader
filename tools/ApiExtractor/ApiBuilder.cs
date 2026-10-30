using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ApiExtractor
{
    /// <summary>Monta o <see cref="ApiDocument"/> com os tipos e membros que um mod enxerga (public e protected).</summary>
    internal sealed class ApiBuilder
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        readonly XmlDocumentation documentation;
        readonly HashSet<string> excludedBaseTypes;

        public ApiBuilder(XmlDocumentation documentation, IEnumerable<string> excludedBaseTypes)
        {
            this.documentation = documentation;
            this.excludedBaseTypes = new HashSet<string>(excludedBaseTypes, StringComparer.Ordinal);
        }

        public List<string> Warnings { get; } = new List<string>();

        public ApiDocument Build(Assembly assembly, string version)
        {
            var document = new ApiDocument { Assembly = assembly.GetName().Name, Version = version };
            foreach (var type in LoadTypes(assembly).Where(IsDocumentable).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var apiType = Attempt(type.FullName, () => BuildType(type));
                if (apiType != null) document.Types.Add(apiType);
            }
            return document;
        }

        IEnumerable<Type> LoadTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                Warnings.Add("some types could not be loaded: " + e.LoaderExceptions.FirstOrDefault()?.Message);
                return e.Types.Where(t => t != null);
            }
        }

        bool IsDocumentable(Type type)
            => type.IsVisible
                && !Reflection.HasAttribute(type, "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                && !IsHidden(type)
                && !InheritsExcludedBase(type);

        static bool IsHidden(MemberInfo member)
        {
            try
            {
                return member.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ComponentModel.EditorBrowsableAttribute"
                    && a.ConstructorArguments.Count == 1 && Convert.ToInt32(a.ConstructorArguments[0].Value) == 1);
            }
            catch (Exception e) when (Reflection.IsResolutionFailure(e)) { return false; }
        }

        bool InheritsExcludedBase(Type type)
        {
            for (var current = Reflection.Safe(() => type.BaseType); current != null; current = Reflection.Safe(() => current.BaseType))
                if (excludedBaseTypes.Contains(current.FullName)) return true;
            return false;
        }

        ApiType BuildType(Type type)
        {
            string id = DocumentationIds.ForType(type);
            var apiType = new ApiType
            {
                Id = id,
                Name = TypeNames.Display(type),
                FullName = type.FullName.Replace('+', '.'),
                Namespace = type.Namespace,
                Kind = KindOf(type),
                DeclaringType = type.DeclaringType?.FullName?.Replace('+', '.'),
                BaseType = BaseTypeName(type),
                Signature = Signatures.ForType(type),
                Obsolete = Reflection.ObsoleteMessage(type),
                Docs = documentation.For(id),
            };
            apiType.Members.AddRange(BuildMembers(type).Where(m => m != null));
            return apiType;
        }

        static string KindOf(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (type.IsValueType) return "struct";
            return Reflection.IsDelegate(type) ? "delegate" : "class";
        }

        static string BaseTypeName(Type type)
        {
            if (type.IsValueType || type.IsInterface || Reflection.IsDelegate(type)) return null;
            var baseType = Reflection.Safe(() => type.BaseType);
            return baseType == null || baseType.FullName == "System.Object" ? null : TypeNames.Display(baseType);
        }

        IEnumerable<ApiMember> BuildMembers(Type type)
        {
            if (Reflection.IsDelegate(type)) return Enumerable.Empty<ApiMember>();
            if (type.IsEnum)
                return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(f => Attempt(Name(type, f), () => BuildEnumValue(f)));

            bool isStatic = type.IsAbstract && type.IsSealed;
            var constructors = isStatic ? Enumerable.Empty<ConstructorInfo>() : type.GetConstructors(Declared).Where(c => !c.IsStatic && Reflection.IsVisible(c));
            return constructors.Select(c => Attempt(Name(type, c), () => BuildConstructor(c)))
                .Concat(type.GetProperties(Declared).Where(p => Reflection.MainAccessor(p) != null).Select(p => Attempt(Name(type, p), () => BuildProperty(p))))
                .Concat(type.GetMethods(Declared).Where(m => !m.IsSpecialName && Reflection.IsVisible(m) && !IsCompilerGenerated(m)).Select(m => Attempt(Name(type, m), () => BuildMethod(m))))
                .Concat(type.GetEvents(Declared).Where(e => Reflection.IsVisible(e.AddMethod)).Select(e => Attempt(Name(type, e), () => BuildEvent(e))))
                .Concat(type.GetFields(Declared).Where(f => Reflection.IsVisible(f) && !f.IsSpecialName && !IsCompilerGenerated(f)).Select(f => Attempt(Name(type, f), () => BuildField(f))))
                .ToList();
        }

        static bool IsCompilerGenerated(MemberInfo member)
            => Reflection.HasAttribute(member, "System.Runtime.CompilerServices.CompilerGeneratedAttribute") || IsHidden(member);

        ApiMember BuildConstructor(ConstructorInfo constructor)
        {
            string id = DocumentationIds.ForMethod(constructor);
            var docs = documentation.For(id);
            return new ApiMember
            {
                Id = id, Name = TypeNames.StripArity(constructor.DeclaringType.Name), Kind = "constructor",
                Signature = Signatures.ForConstructor(constructor), Docs = docs,
                Obsolete = Reflection.ObsoleteMessage(constructor), Parameters = BuildParameters(constructor.GetParameters(), docs),
            };
        }

        ApiMember BuildProperty(PropertyInfo property)
        {
            string id = DocumentationIds.ForProperty(property);
            var docs = documentation.For(id);
            return new ApiMember
            {
                Id = id, Name = property.Name, Kind = "property", Signature = Signatures.ForProperty(property),
                IsStatic = Reflection.MainAccessor(property).IsStatic, Docs = docs, Obsolete = Reflection.ObsoleteMessage(property),
                Parameters = BuildParameters(property.GetIndexParameters(), docs),
            };
        }

        ApiMember BuildMethod(MethodInfo method)
        {
            string id = DocumentationIds.ForMethod(method);
            var docs = documentation.For(id);
            bool returnsValue = method.ReturnType.FullName != "System.Void";
            return new ApiMember
            {
                Id = id, Name = method.Name, Kind = "method", Signature = Signatures.ForMethod(method), IsStatic = method.IsStatic,
                Docs = docs, Obsolete = Reflection.ObsoleteMessage(method), Parameters = BuildParameters(method.GetParameters(), docs),
                Returns = returnsValue ? new ApiReturn { Type = TypeNames.Display(method.ReturnType), Description = docs?.Returns } : null,
            };
        }

        ApiMember BuildEvent(EventInfo evento)
        {
            string id = DocumentationIds.ForEvent(evento);
            return new ApiMember
            {
                Id = id, Name = evento.Name, Kind = "event", Signature = Signatures.ForEvent(evento),
                IsStatic = evento.AddMethod.IsStatic, Docs = documentation.For(id), Obsolete = Reflection.ObsoleteMessage(evento),
            };
        }

        ApiMember BuildField(FieldInfo field)
        {
            string id = DocumentationIds.ForField(field);
            return new ApiMember
            {
                Id = id, Name = field.Name, Kind = field.IsLiteral ? "constant" : "field", Signature = Signatures.ForField(field),
                IsStatic = field.IsStatic, Docs = documentation.For(id), Obsolete = Reflection.ObsoleteMessage(field),
            };
        }

        ApiMember BuildEnumValue(FieldInfo field)
        {
            string id = DocumentationIds.ForField(field);
            string value = Convert.ToString(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);
            return new ApiMember
            {
                Id = id, Name = field.Name, Kind = "value", Signature = field.Name + " = " + value, Value = value, IsStatic = true,
                Docs = documentation.For(id), Obsolete = Reflection.ObsoleteMessage(field),
            };
        }

        static List<ApiParameter> BuildParameters(ParameterInfo[] parameters, ApiDocs docs)
        {
            if (parameters.Length == 0) return null;
            return parameters.Select(p => new ApiParameter
            {
                Name = p.Name,
                Type = TypeNames.Display(p.ParameterType),
                Description = docs?.Parameters != null && docs.Parameters.TryGetValue(p.Name, out string text) ? text : null,
            }).ToList();
        }

        T Attempt<T>(string what, Func<T> build) where T : class
        {
            try { return build(); }
            catch (Exception e) when (Reflection.IsResolutionFailure(e))
            {
                Warnings.Add(what + " skipped: " + e.Message);
                return null;
            }
        }

        static string Name(Type type, MemberInfo member) => type.FullName + "." + member.Name;
    }
}
