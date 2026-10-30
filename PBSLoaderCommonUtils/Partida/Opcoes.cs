using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using UnityEngine;

namespace PBSLoaderCommonUtils.Partida
{
    /// <summary>Quando uma mudança de opção do jogo passa a valer.</summary>
    public enum QuandoVale { NaHora, AoRecomecarViagem }

    /// <summary>
    /// Uma opção do jogo (settings.txt, pelo MepPrefs do jogo). <c>Valor</c> lê e grava; gravar escreve o arquivo
    /// do jogo na hora. Veja <see cref="Vale"/>: a maioria só vale ao recomeçar a viagem, como no menu de opções do jogo.
    /// </summary>
    public abstract class Opcao
    {
        /// <summary>Chave no settings.txt ("maxCars").</summary>
        public string Chave { get; }
        public string Nome { get; }
        public string Grupo { get; }
        public QuandoVale Vale { get; }
        public abstract Type Tipo { get; }
        /// <summary>Valor como texto (diagnóstico).</summary>
        public abstract string Texto { get; }

        protected Opcao(string chave, string nome, string grupo, QuandoVale vale) { Chave = chave; Nome = nome; Grupo = grupo; Vale = vale; }
        public override string ToString() => $"{Grupo} / {Nome} ({Chave}) = {Texto}{(Vale == QuandoVale.AoRecomecarViagem ? " [ao recomeçar]" : "")}";
    }

    public sealed class Opcao<T> : Opcao
    {
        readonly T _padrao;
        readonly Action<T> _aplicar;
        public T Min { get; }
        public T Max { get; }
        public override Type Tipo => typeof(T);

        internal Opcao(string chave, string nome, string grupo, QuandoVale vale, T padrao, T min = default, T max = default, Action<T> aplicar = null)
            : base(chave, nome, grupo, vale) { _padrao = padrao; Min = min; Max = max; _aplicar = aplicar; }

        /// <summary>Valor gravado no jogo (o padrão do jogo se ainda não existir).</summary>
        public T Valor
        {
            get => Opcoes.Ler(Chave, _padrao);
            set
            {
                if (!Opcoes.Gravar(Chave, value)) return;
                if (_aplicar != null) try { _aplicar(value); } catch (Exception e) { Sdk.Registrar(e, "Opcoes " + Chave); }
            }
        }

        public override string Texto => Convert.ToString(Valor, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Opções do próprio jogo (as do menu de opções dele), lidas e gravadas pelo MepPrefs do jogo, no settings.txt da pasta
    /// de dados. As principais têm nome em <see cref="Todas"/> e campo próprio (ex.: <see cref="CarrosNoTransito"/>); as
    /// outras, por chave com <see cref="Ler{T}"/> e <see cref="Gravar{T}"/>. Antes de o jogo abrir o arquivo (logo no
    /// começo do menu), ler devolve o padrão e gravar não faz nada.
    /// </summary>
    public static class Opcoes
    {
        const string Transito = "Trânsito", Graficos = "Gráficos", Espelhos = "Espelhos", Viagem = "Viagem", Controles = "Controles", Som = "Som";

        // Trânsito: a quantidade também muda na viagem em andamento (o controlador de tráfego lê estes campos).
        public static readonly Opcao<int> CarrosNoTransito = new("maxCars", "Carros no trânsito", Transito, QuandoVale.NaHora, 20, 0, 100,
            v => MEPAI_Traffic_System_Controller.maxCars = v);
        public static readonly Opcao<int> PedestresNaRua = new("maxPeds", "Pedestres", Transito, QuandoVale.NaHora, 60, 0, 200,
            v => MEPAI_Traffic_System_Controller.maxPeds = v);
        public static readonly Opcao<int> OnibusNoTransito = new("maxBuses", "Ônibus no trânsito", Transito, QuandoVale.NaHora, 10, 0, 50,
            v => MEPAI_Traffic_System_Controller.maxBuses = v);
        public static readonly Opcao<int> PassageirosPorcento = new("porcentagemPassageiros", "Passageiros (%)", Transito, QuandoVale.AoRecomecarViagem, 30, 0, 100);
        public static readonly Opcao<int> CarrosEstacionadosPorcento = new("porcentagemCarrosEstacionados", "Carros estacionados (%)", Transito, QuandoVale.AoRecomecarViagem, 55, 0, 100);
        public static readonly Opcao<bool> OnibusEstacionados = new("onibusEstacionados", "Ônibus estacionados", Transito, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> TrensDoMapa = new("usarTrensDoMapa", "Trens do mapa", Transito, QuandoVale.AoRecomecarViagem, false);

        public static readonly Opcao<float> DistanciaVisivel = new("distanciaVisivel", "Distância visível (m)", Graficos, QuandoVale.AoRecomecarViagem, 500f, 100f, 3000f);
        public static readonly Opcao<int> MaxFps = new("maxFPS", "Limite de FPS", Graficos, QuandoVale.AoRecomecarViagem, 60, 30, 240);
        public static readonly Opcao<bool> Sombras = new("usarSombras", "Sombras", Graficos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> SombrasDeNoite = new("usarSombrasDeNoite", "Sombras à noite", Graficos, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> Reflexos = new("usarReflexos", "Reflexos", Graficos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> Neblina = new("usarFog", "Neblina", Graficos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> Antialiasing = new("usarAntialiasing", "Antialiasing", Graficos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> Arvores3D = new("arvores3D", "Árvores 3D", Graficos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> RaiosETrovoes = new("usarRaiosETrovoes", "Raios e trovões", Graficos, QuandoVale.AoRecomecarViagem, true);

        public static readonly Opcao<bool> RetrovisorEsquerdo = new("retrovisorEsq", "Retrovisor esquerdo", Espelhos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> RetrovisorDireito = new("retrovisorDir", "Retrovisor direito", Espelhos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> RetrovisorInterno = new("retrovisorMeio", "Retrovisor interno", Espelhos, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> EspelhosMaisQualidade = new("espelhosMaisQualidade", "Espelhos com mais qualidade", Espelhos, QuandoVale.AoRecomecarViagem, false);

        public static readonly Opcao<bool> JogarANoite = new("jogarANoite", "Começar à noite", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> HoraLocal = new("jogarTempoLocal", "Hora do computador", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> ComecarChovendo = new("tahChovendo", "Começar chovendo", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> CambioManual = new("usarCambioManual", "Câmbio manual", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> Embreagem = new("usarEmbreagem", "Embreagem", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> Cobrador = new("usarCobrador", "Cobrador", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> Placas = new("usarPlacas", "Placas nos veículos", Viagem, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> ComecarLigado = new("iniciarLigado", "Começar com o ônibus ligado", Viagem, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> FreioDeEstacionamento = new("freioEstacionamento", "Freio de estacionamento automático", Viagem, QuandoVale.AoRecomecarViagem, true);

        public static readonly Opcao<bool> ForceFeedback = new("forceFeedback", "Force feedback do volante", Controles, QuandoVale.AoRecomecarViagem, true);
        public static readonly Opcao<bool> InverterVolante = new("inverterInputVolante", "Inverter o volante", Controles, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> InverterPedais = new("inverterInputPedais", "Inverter os pedais", Controles, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> PedaisCombinados = new("pedaisCombinados", "Pedais combinados", Controles, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<bool> AjudaDeDirecao = new("usarSteeringHelp", "Ajuda de direção", Controles, QuandoVale.AoRecomecarViagem, false);
        public static readonly Opcao<float> SensibilidadeTeclado = new("sensibilidadeTeclado", "Sensibilidade do teclado", Controles, QuandoVale.AoRecomecarViagem, 0.25f, 0.05f, 1f);

        // O volume geral também muda na hora (o jogo o aplica no AudioListener).
        public static readonly Opcao<float> Volume = new("volume", "Volume geral", Som, QuandoVale.NaHora, 1f, 0f, 1f, v => AudioListener.volume = v);
        public static readonly Opcao<bool> VozesComMotorista = new("somPassageirosComMotorista", "Passageiros falam com o motorista", Som, QuandoVale.AoRecomecarViagem, true);

        static Opcao[] _todas;

        /// <summary>As opções com nome, na ordem dos grupos.</summary>
        public static IReadOnlyList<Opcao> Todas => _todas ??= new Opcao[]
        {
            CarrosNoTransito, PedestresNaRua, OnibusNoTransito, PassageirosPorcento, CarrosEstacionadosPorcento, OnibusEstacionados, TrensDoMapa,
            DistanciaVisivel, MaxFps, Sombras, SombrasDeNoite, Reflexos, Neblina, Antialiasing, Arvores3D, RaiosETrovoes,
            RetrovisorEsquerdo, RetrovisorDireito, RetrovisorInterno, EspelhosMaisQualidade,
            JogarANoite, HoraLocal, ComecarChovendo, CambioManual, Embreagem, Cobrador, Placas, ComecarLigado, FreioDeEstacionamento,
            ForceFeedback, InverterVolante, InverterPedais, PedaisCombinados, AjudaDeDirecao, SensibilidadeTeclado,
            Volume, VozesComMotorista,
        };

        /// <summary>O jogo já abriu o arquivo de opções.</summary>
        public static bool Disponivel
        {
            get { try { return MepPrefs.hasInitialized; } catch { return false; } }
        }

        /// <summary>Lê qualquer opção do jogo pela chave (int, float, bool ou string).</summary>
        public static T Ler<T>(string chave, T padrao = default)
        {
            if (!Disponivel) return padrao;
            try
            {
                if (!MepPrefs.HasKey(chave)) return padrao;
                object v = typeof(T) == typeof(bool) ? MepPrefs.GetBool(chave, (bool)(object)padrao)
                    : typeof(T) == typeof(int) ? MepPrefs.GetInt(chave, (int)(object)padrao)
                    : typeof(T) == typeof(float) ? MepPrefs.GetFloat(chave, (float)(object)padrao)
                    : typeof(T) == typeof(string) ? MepPrefs.GetString(chave, (string)(object)padrao ?? "")
                    : throw new NotSupportedException("tipo de opção não suportado: " + typeof(T).Name);
                return (T)v;
            }
            catch (NotSupportedException) { throw; }
            catch { return padrao; }
        }

        /// <summary>Grava qualquer opção do jogo pela chave e escreve o arquivo. Retorna false se o jogo ainda não abriu o arquivo.</summary>
        public static bool Gravar<T>(string chave, T valor)
        {
            if (!Disponivel) return false;
            try
            {
                switch (valor)
                {
                    case bool b: MepPrefs.SetBool(chave, b); break;
                    case int i: MepPrefs.SetInt(chave, i); break;
                    case float f: MepPrefs.SetFloat(chave, f); break;
                    case string s: MepPrefs.SetString(chave, s); break;
                    default: throw new NotSupportedException("tipo de opção não suportado: " + typeof(T).Name);
                }
                MepPrefs.SaveNow();
                return true;
            }
            catch (NotSupportedException) { throw; }
            catch (Exception e) { Sdk.Registrar(e, "Opcoes.Gravar " + chave); return false; }
        }

        internal static string Resumo()
        {
            if (!Disponivel) return "opções do jogo ainda não carregadas\n";
            var sb = new System.Text.StringBuilder();
            foreach (var o in Todas) sb.Append(o).Append('\n');
            return sb.ToString();
        }
    }
}
