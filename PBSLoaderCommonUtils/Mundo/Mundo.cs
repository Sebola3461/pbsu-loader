using System;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PBSLoaderCommonUtils.Mundo
{
    public enum Chuva { Nenhuma, Leve, Media, Pesada }

    /// <summary>Hora do jogo (EnviroSky). Sem céu carregado (menus), <see cref="Existe"/> é false.</summary>
    public static partial class Tempo
    {
        public static bool Existe { get; internal set; }
        public static int Hora { get; internal set; }
        public static int Minuto { get; internal set; }
        public static int Segundo { get; internal set; }
        public static int Dia { get; internal set; }
        /// <summary>Hora com fração (13.5 = 13:30).</summary>
        public static float HoraDecimal { get; internal set; }
        public static bool Noite { get; internal set; }
        /// <summary>Quantos minutos reais dura o dia do jogo.</summary>
        public static float DuracaoDoDiaMin { get; internal set; }
        /// <summary>O relógio do jogo está parado (o jogador ou um mod travou a hora).</summary>
        public static bool Parado { get; internal set; }
        public static string Texto => Existe ? $"{Hora:00}:{Minuto:00}" : "--:--";

        static readonly Inscritos<Action<int>> _horaPassou = new("Tempo.HoraPassou");
        /// <summary>A hora cheia mudou (o argumento é a nova hora).</summary>
        public static event Action<int> HoraPassou { add => _horaPassou.Add(value); remove => _horaPassou.Remove(value); }
        internal static void AvisarHora(int h) => _horaPassou.Disparar(h);
    }

    /// <summary>Clima do jogo: chuva (MepMobileControls/MepChuva) e o preset do EnviroSky.</summary>
    public static partial class Clima
    {
        public static bool Chovendo { get; internal set; }
        public static Chuva Chuva { get; internal set; }
        /// <summary>Quanto o mundo está molhado, 0..1 (o EnviroSky sobe e desce aos poucos).</summary>
        public static float Molhado { get; internal set; }
        /// <summary>Nome do preset de clima do EnviroSky ("Clear Sky", "Rain"...).</summary>
        public static string Nome { get; internal set; } = "";

        static readonly Inscritos<Action> _mudou = new("Clima.Mudou");
        /// <summary>Começou ou parou de chover, ou o preset mudou.</summary>
        public static event Action Mudou { add => _mudou.Add(value); remove => _mudou.Remove(value); }
        internal static void AvisarMudou() => _mudou.Disparar();
    }

    /// <summary>Tráfego de IA (MEPAI_Traffic_System_Controller): quantos existem agora e o limite.</summary>
    public static partial class Trafego
    {
        public static int Carros => Ler(() => MEPAI_Traffic_System_Controller.currentCars);
        public static int Pedestres => Ler(() => MEPAI_Traffic_System_Controller.currentPeds);
        public static int Onibus => Ler(() => MEPAI_Traffic_System_Controller.currentBuses);
        public static int Trens => Ler(() => MEPAI_Traffic_System_Controller.quantosTrensTem);
        public static int MaxCarros => Ler(() => MEPAI_Traffic_System_Controller.maxCars);
        public static int MaxPedestres => Ler(() => MEPAI_Traffic_System_Controller.maxPeds);
        public static int MaxOnibus => Ler(() => MEPAI_Traffic_System_Controller.maxBuses);
        public static bool ColisoesAtivas => Ler(() => MEPAI_Traffic_System_Controller.collisionsActivated ? 1 : 0) != 0;

        static int Ler(Func<int> f) { try { return f(); } catch { return 0; } }
    }

    // Leitura do céu e da chuva, 4 vezes por segundo.
    internal static class LeitorDoMundo
    {
        static float _proxima, _proximaChuva;
        static MepChuva _chuva;
        static int _hora = -1;

        internal static void CenaMudou() { _chuva = null; _proximaChuva = 0f; }

        internal static void Atualizar()
        {
            if (Time.unscaledTime < _proxima) return;
            _proxima = Time.unscaledTime + 0.25f;

            EnviroSky sky = null;
            try { sky = EnviroSky.instance; } catch { }
            var t = sky != null ? sky.GameTime : null;
            if (t == null)
            {
                Tempo.Existe = false; _hora = -1;
            }
            else
            {
                Tempo.Existe = true;
                Tempo.Hora = t.Hours; Tempo.Minuto = t.Minutes; Tempo.Segundo = t.Seconds; Tempo.Dia = t.Days;
                Tempo.HoraDecimal = sky.internalHour;
                Tempo.Noite = sky.isNight;
                Tempo.DuracaoDoDiaMin = t.DayLengthInMinutes;
                Tempo.Parado = t.ProgressTime == EnviroTime.TimeProgressMode.None;
                if (_hora >= 0 && t.Hours != _hora) Tempo.AvisarHora(t.Hours);
                _hora = t.Hours;
            }

            bool chovendo = MepMobileControls.TahChovendo;
            float molhado = 0f; string nome = "";
            try
            {
                var w = sky?.Weather;
                if (w != null)
                {
                    molhado = Mathf.Clamp01(w.curWetness);
                    nome = w.currentActiveWeatherPreset?.Name ?? "";
                }
            }
            catch { }

            if (Time.unscaledTime >= _proximaChuva && (_chuva == null || _chuva.WasCollected))
            {
                _proximaChuva = Time.unscaledTime + 3f;
                _chuva = Object.FindObjectOfType<MepChuva>();
            }
            var intensidade = Chuva.Nenhuma;
            if (_chuva != null && !_chuva.WasCollected)
            {
                if (Ativo(_chuva.chuvaPesada)) intensidade = Chuva.Pesada;
                else if (Ativo(_chuva.chuvaMedia)) intensidade = Chuva.Media;
                else if (Ativo(_chuva.chuvaLeve)) intensidade = Chuva.Leve;
            }
            // Chove se o jogo diz que chove ou se o céu está num clima de chuva.
            if (!chovendo && (nome.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0 || nome.IndexOf("Storm", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                chovendo = true;
                if (intensidade == Chuva.Nenhuma)
                    intensidade = nome.IndexOf("Storm", StringComparison.OrdinalIgnoreCase) >= 0 ? Chuva.Pesada
                        : nome.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0 ? Chuva.Leve : Chuva.Media;
            }
            if (chovendo && intensidade == Chuva.Nenhuma) intensidade = Chuva.Media;
            if (!chovendo) intensidade = Chuva.Nenhuma;

            bool mudou = chovendo != Clima.Chovendo || intensidade != Clima.Chuva || nome != Clima.Nome;
            Clima.Chovendo = chovendo; Clima.Chuva = intensidade; Clima.Molhado = molhado; Clima.Nome = nome;
            if (mudou) Clima.AvisarMudou();
        }

        static bool Ativo(GameObject g) => g != null && g.activeInHierarchy;

        internal static string Resumo() =>
            $"hora {Tempo.Texto}:{Tempo.Segundo:00} dia {Tempo.Dia} (decimal {Tempo.HoraDecimal:0.00}, noite {Tempo.Noite}, dia dura {Tempo.DuracaoDoDiaMin:0} min, parado {Tempo.Parado})\n" +
            $"clima \"{Clima.Nome}\", chovendo {Clima.Chovendo} ({Clima.Chuva}), molhado {Clima.Molhado:0.00}\n" +
            $"tráfego: {Trafego.Carros}/{Trafego.MaxCarros} carros, {Trafego.Pedestres}/{Trafego.MaxPedestres} pedestres, {Trafego.Onibus}/{Trafego.MaxOnibus} ônibus, {Trafego.Trens} trens, colisões {Trafego.ColisoesAtivas}\n";
    }
}
