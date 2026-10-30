using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ApiExtractor.Tests.Fixtures
{
    /// <summary>Uma classe estática com <c>código</c> e <see cref="T:ApiExtractor.Tests.Fixtures.Derived"/>.</summary>
    /// <remarks><para>Primeiro.</para><para>Segundo.</para></remarks>
    public static class StaticSample
    {
        /// <summary>Versão.</summary>
        public const string Version = "1.0";

        public static float SpeedKmh { get; private set; }

        /// <summary>Soma.</summary>
        /// <param name="a">Primeiro.</param>
        /// <param name="b"> </param>
        /// <returns>A soma.</returns>
        public static int Add(int a, int b = 2) => a + b;

        /// <summary>Mudou.</summary>
        public static event Action<SampleKind> Changed;

        /// <summary>Lê.</summary>
        public static bool TryRead(string key, out float value, ref int count, in int limit, params string[] extra)
        {
            value = 0;
            return false;
        }

        /// <summary>Genérico.</summary>
        /// <typeparam name="T">O tipo.</typeparam>
        public static T Read<T>(string key, T fallback = default) => fallback;

        /// <summary>Talvez.</summary>
        public static int? Maybe(List<string> names, Dictionary<string, int[]> map, (int, string) pair) => null;

        /// <summary>Grade.</summary>
        public static void Grid(int[,] grid) { }

        /// <summary>Ponteiro.</summary>
        public static unsafe void Raw(int* pointer) { }

        internal static void Hidden() { }
    }

    public static class Extensions
    {
        public static int Twice(this int value) => value * 2;
    }

    /// <summary>Tipos.</summary>
    public enum SampleKind : byte
    {
        None = 0,
        /// <summary>O primeiro.</summary>
        First = 1,
        Second = 4,
    }

    public enum PlainKind { A, B }

    /// <summary>Base abstrata.</summary>
    public abstract class ScriptBase
    {
        public virtual void Activate() { }
        public abstract void Frame();
        protected void Helper() { }
        protected internal void Helper2() { }
        private protected void Hidden() { }

        /// <summary>Indexador.</summary>
        public string this[int index] => "";

        /// <summary>Aninhado.</summary>
        public class Nested
        {
            public int Value;
            public readonly float Fixed = 1f;
            public static readonly string Shared = "";
            protected int Guarded;
        }
    }

    public sealed class Derived : ScriptBase, IDisposable
    {
        public Derived() { }

        /// <summary>Com número.</summary>
        public Derived(int x) { }

        public override void Frame() { }
        public sealed override void Activate() { }
        public void Dispose() { }
        private void HiddenBecausePrivate() { }
    }

    public class Generic<T> where T : class
    {
        public T Item { get; set; }

        /// <summary>Define.</summary>
        public void Set(T value, List<T> list) { }

        /// <summary>Mapeia.</summary>
        public U Map<U>(Func<T, U> map) => default;
    }

    public delegate bool SampleHandler(string name, int count);

    public struct Point
    {
        public float X;
        public static Point operator +(Point a, Point b) => a;
    }

    public interface ISample
    {
        string Name { get; }
    }

    public class Record
    {
        public int Id { get; init; }
        public int Count { get; protected set; }
        protected int Secret { get; set; }
        public int WriteOnly { set { } }
    }

    public class ExcludedBase { }

    public class ExcludedChild : ExcludedBase { }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public class HiddenType { }

    public class WithHiddenMember
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Hidden() { }

        public void Visible() { }
    }

    [Obsolete("Use Derived.")]
    public class OldType
    {
        [Obsolete]
        public void Old() { }
    }

    internal class InternalType { }

    public class Defaults
    {
        public void Many(string s = "a\"b\\", bool flag = true, bool off = false, char c = 'x', double d = 1.5, SampleKind kind = SampleKind.First,
            object o = null, Point p = default, float f = 0.25f, SampleKind other = (SampleKind)9, long big = 10) { }
    }
}
