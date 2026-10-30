using System.Collections.Generic;
using PBSLoaderCommonUtils.Veiculos;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Valores que um mod publica para os outros lerem, sem um depender da DLL do outro
    /// (ex.: o SuspensaoAr publica o nível da suspensão; a PBSLoaderCommonInterface mostra).
    /// Por padrão o valor vale só para o veículo atual: some quando o veículo troca.
    /// Chaves são texto livre; use o nome do mod como <c>mod</c>.
    /// </summary>
    public static class Dados
    {
        struct Valor { public object V; public int Geracao; }

        static readonly Dictionary<(string, string), Valor> _valores = new();

        /// <summary>Publica (ou substitui) um valor. Com <paramref name="porVeiculo"/> false, vale até o jogo fechar.</summary>
        public static void Publicar(string mod, string chave, object valor, bool porVeiculo = true)
            => _valores[(mod, chave)] = new Valor { V = valor, Geracao = porVeiculo ? Veiculo.Geracao : -1 };

        /// <summary>Apaga um valor.</summary>
        public static void Apagar(string mod, string chave) => _valores.Remove((mod, chave));

        /// <summary>Existe um valor válido agora.</summary>
        public static bool Tem(string mod, string chave) => Achar(mod, chave, out _);

        /// <summary>Lê o valor, ou <paramref name="padrao"/> se não existir, for de outro veículo ou de outro tipo.</summary>
        public static T Ler<T>(string mod, string chave, T padrao = default)
            => Achar(mod, chave, out var v) && v is T t ? t : padrao;

        static bool Achar(string mod, string chave, out object v)
        {
            v = null;
            if (!_valores.TryGetValue((mod, chave), out var x)) return false;
            if (x.Geracao >= 0 && (x.Geracao != Veiculo.Geracao || !Veiculo.Existe)) return false;
            v = x.V;
            return true;
        }
    }
}
