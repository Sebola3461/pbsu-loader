using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace PBSLoaderCommonUtils.Interface
{
    public enum Gravidade { Info, Aviso, Falha }

    /// <summary>Um aviso do veículo. Os textos podem mudar enquanto ele está ativo (<see cref="Versao"/> sobe).</summary>
    public sealed class Alerta
    {
        public string Origem { get; internal set; }
        public string Id { get; internal set; }
        public string Chave => Origem + "/" + Id;
        public Gravidade Gravidade { get; internal set; }
        public string Titulo { get; internal set; }
        public string Detalhe { get; internal set; }
        /// <summary>Quando apareceu (Time.unscaledTime).</summary>
        public float Desde { get; internal set; }
        /// <summary>Condição (fica até a origem dizer que acabou) ou notificação (expira sozinha).</summary>
        public bool Condicao { get; internal set; }
        /// <summary>Muda a cada troca de texto ou gravidade (para a interface redesenhar só quando precisa).</summary>
        public int Versao { get; internal set; }
        internal float Expira;
    }

    /// <summary>
    /// Avisos e falhas do veículo, de qualquer mod. Quem mostra (a PBSLoaderCommonInterface, por exemplo) lê <see cref="Ativos"/>.
    ///
    ///   Condição      Avisos.Definir("MeuMod", "oleo", pressaoBaixa, Gravidade.Falha, "Pressão do óleo baixa", "0,8 bar");
    ///                 chame quando o estado mudar ou a cada frame; com "ativo" false ela sai.
    ///   Notificação   Avisos.Notificar("MeuMod", "salvo", Gravidade.Info, "Ajustes salvos");  some depois de alguns segundos.
    ///
    /// Os avisos são do veículo atual: trocar de veículo limpa tudo.
    /// </summary>
    public static class Avisos
    {
        static readonly List<Alerta> _ativos = new();
        static int _geracao = -1;

        /// <summary>Avisos ativos, do mais antigo ao mais novo.</summary>
        public static IReadOnlyList<Alerta> Ativos => _ativos;

        /// <summary>Algum aviso entrou, saiu ou mudou de texto.</summary>
        public static event Action Mudou;

        /// <summary>Liga, atualiza ou desliga uma condição.</summary>
        public static void Definir(string origem, string id, bool ativo, Gravidade gravidade, string titulo, string detalhe = null)
        {
            int i = Indice(origem, id);
            if (!ativo)
            {
                if (i >= 0 && _ativos[i].Condicao) { _ativos.RemoveAt(i); Avisar(); }
                return;
            }
            if (i < 0)
            {
                _ativos.Add(new Alerta { Origem = origem, Id = id, Gravidade = gravidade, Titulo = titulo, Detalhe = detalhe ?? "",
                                         Desde = Time.unscaledTime, Condicao = true });
                Avisar();
                return;
            }
            var a = _ativos[i];
            if (a.Gravidade != gravidade || a.Titulo != titulo || a.Detalhe != (detalhe ?? ""))
            {
                // Piorou: conta como novo (a interface volta a destacar).
                if (gravidade > a.Gravidade) a.Desde = Time.unscaledTime;
                a.Gravidade = gravidade; a.Titulo = titulo; a.Detalhe = detalhe ?? "";
                a.Condicao = true; a.Versao++;
                Avisar();
            }
        }

        /// <summary>Aviso pontual que some depois de <paramref name="segundos"/>. Repetir o mesmo id renova.</summary>
        public static void Notificar(string origem, string id, Gravidade gravidade, string titulo, string detalhe = null, float segundos = 5f)
        {
            int i = Indice(origem, id);
            if (i >= 0) _ativos.RemoveAt(i);
            _ativos.Add(new Alerta { Origem = origem, Id = id, Gravidade = gravidade, Titulo = titulo, Detalhe = detalhe ?? "",
                                     Desde = Time.unscaledTime, Expira = Time.unscaledTime + segundos });
            Avisar();
        }

        /// <summary>Tira todos os avisos de uma origem (ex.: o mod foi desligado).</summary>
        public static void Limpar(string origem)
        {
            if (_ativos.RemoveAll(a => a.Origem == origem) > 0) Avisar();
        }

        static int Indice(string origem, string id) => _ativos.FindIndex(a => a.Origem == origem && a.Id == id);

        static void Avisar()
        {
            try { Mudou?.Invoke(); } catch (Exception e) { MelonLogger.Warning("[Avisos] " + e.Message); }
        }

        internal static void Atualizar()
        {
            if (_geracao != Veiculo.Geracao || !Veiculo.Existe)
            {
                _geracao = Veiculo.Geracao;
                if (_ativos.Count > 0) { _ativos.Clear(); Avisar(); }
                if (!Veiculo.Existe) return;
            }
            if (_ativos.RemoveAll(a => !a.Condicao && Time.unscaledTime > a.Expira) > 0) Avisar();
            MonitorDoVeiculo.Quadro();
        }

        internal static string Resumo()
        {
            if (_ativos.Count == 0) return "avisos: nenhum\n";
            var sb = new System.Text.StringBuilder("avisos:\n");
            foreach (var a in _ativos) sb.Append($"  [{a.Gravidade}] {a.Chave}: {a.Titulo}{(a.Detalhe.Length > 0 ? " — " + a.Detalhe : "")}\n");
            return sb.ToString();
        }
    }

    // Avisos que o próprio núcleo sabe dar: ar comprimido e portas.
    internal static class MonitorDoVeiculo
    {
        const string O = "PBSLoaderCommonUtils";
        static bool _andando;
        static float _proximo;

        internal static void Quadro()
        {
            if (Time.unscaledTime < _proximo) return; // 5 vezes por segundo bastam para avisos
            _proximo = Time.unscaledTime + 0.2f;

            // Ar: com a elétrica ligada, o painel acusa pressão baixa (a mesma hora do alarme sonoro).
            if (Ar.Simulando && Veiculo.EletricaLigada && Ar.Pressao < Ar.PressaoAlarme.Value)
            {
                bool vazio = Ar.Pressao < 0.5f;
                string det = $"{Ar.Pressao:0.0} bar";
                if (Ar.FreioMolas > 0.5f && !Veiculo.Pedais.FreioDeMaoPuxado) det += " · freio de molas aplicado";
                else if (Ar.Pressao < 5f) det += " · freio fraco";
                if (!Veiculo.Motor.Ligado) det += " · ligue o motor para encher";
                else if (Ar.CompressorCarregando) det += " · enchendo";
                Avisos.Definir(O, "ar", true, Gravidade.Falha, vazio ? "Reservatório de ar vazio" : "Pressão do ar baixa", det);
            }
            else Avisos.Definir(O, "ar", false, Gravidade.Falha, null);

            // Portas: abertas parado é normal (ponto), só informa; andando é falha.
            var abertas = new List<string>();
            foreach (var p in Veiculo.Portas.Todas) if (p.Aberta) abertas.Add((p.Lado == Lado.Direito ? "R" : "L") + p.Numero);
            if (abertas.Count == 0 && Veiculo.Portas.AlgumaAberta) abertas.Add("?");
            float v = Veiculo.VelocidadeKmh;
            _andando = _andando ? v > 2f : v > 5f;
            if (abertas.Count > 0)
            {
                string quais = abertas.Count == 1 && abertas[0] != "?" ? "Porta " + abertas[0] : abertas.Count + " portas: " + string.Join(", ", abertas);
                if (abertas.Count == 1 && abertas[0] == "?") quais = "Porta aberta";
                Avisos.Definir(O, "portas", true, _andando ? Gravidade.Falha : Gravidade.Info,
                    _andando ? "Porta aberta em movimento" : "Porta aberta", quais);
            }
            else Avisos.Definir(O, "portas", false, Gravidade.Info, null);
        }
    }
}
