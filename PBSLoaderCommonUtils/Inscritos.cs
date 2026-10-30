using System;

namespace PBSLoaderCommonUtils
{
    // Lista de inscritos de um evento, copiada ao mudar: disparar não aloca (os ganchos de física rodam a cada
    // passo) e um inscrito que sai durante o disparo não atrapalha. Cada inscrito roda protegido.
    internal sealed class Inscritos<T> where T : Delegate
    {
        readonly string _nome;
        T[] _lista = Array.Empty<T>();

        internal Inscritos(string nome) { _nome = nome; }

        internal T[] Lista => _lista;
        internal bool Vazio => _lista.Length == 0;

        internal void Add(T d)
        {
            if (d == null) return;
            var n = new T[_lista.Length + 1];
            _lista.CopyTo(n, 0); n[^1] = d;
            _lista = n;
        }

        internal void Remove(T d)
        {
            int i = Array.IndexOf(_lista, d);
            if (i < 0) return;
            var n = new T[_lista.Length - 1];
            Array.Copy(_lista, 0, n, 0, i);
            Array.Copy(_lista, i + 1, n, i, _lista.Length - i - 1);
            _lista = n;
        }

        internal void Erro(T d, Exception e) =>
            Sdk.Registrar(e, _nome + " → " + (d.Method.DeclaringType?.FullName ?? "?") + "." + d.Method.Name);
    }

    internal static class InscritosExt
    {
        internal static void Disparar(this Inscritos<Action> ins)
        {
            foreach (var d in ins.Lista)
                try { d(); } catch (Exception e) { ins.Erro(d, e); }
        }

        internal static void Disparar<A>(this Inscritos<Action<A>> ins, A a)
        {
            foreach (var d in ins.Lista)
                try { d(a); } catch (Exception e) { ins.Erro(d, e); }
        }
    }
}
