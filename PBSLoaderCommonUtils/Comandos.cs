using System;
using MelonLoader;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Comandos de carroceria do veículo do jogador, pelos mesmos métodos que os botões do jogo usam (então o jogo
    /// cuida de som, animação do botão do painel e luzes). Cada comando alterna o estado; leia o estado em
    /// <see cref="Veiculo"/>. Sem veículo, nada acontece.
    ///
    /// Portas passam pela trava de ar do <see cref="Ar"/> (sem pressão elas não se mexem).
    /// </summary>
    public static class Comandos
    {
        static Il2CppMep.MEPControlesBus C => Veiculo.Existe ? Veiculo.Controles : null;

        static void Fazer(string nome, Action<Il2CppMep.MEPControlesBus> a)
        {
            var c = C;
            if (c == null) return;
            try { a(c); }
            catch (Exception e) { MelonLogger.Warning($"[Comandos] {nome}: {e.Message}"); }
        }

        // ---------- luzes e sinais ----------
        public static void FarolBaixo() => Fazer("farol baixo", c => c.ClicouFarolBaixo());
        public static void FarolAlto() => Fazer("farol alto", c => c.ClicouFarolAlto());
        public static void PiscaAlerta() => Fazer("pisca-alerta", c => c.ClicouPiscaAlerta());
        public static void SetaEsquerda() => Fazer("seta esquerda", c => c.ClicouSetaEsquerda());
        public static void SetaDireita() => Fazer("seta direita", c => c.ClicouSetaDireita());
        public static void FreioDeMao() => Fazer("freio de mão", c => c.ClicouFreioDeMao());

        /// <summary>O veículo tem a luz interna (salão) 1, 2 ou 3.</summary>
        public static bool TemLuzInterna(int n)
        {
            var c = C;
            if (c == null) return false;
            try
            {
                return n switch
                {
                    1 => c.objluzInterna1 != null || c.texObjLuzInterna1 != null,
                    2 => c.objluzInterna2 != null || c.texObjLuzInterna2 != null,
                    3 => c.objluzInterna3 != null || c.texObjLuzInterna3 != null,
                    _ => false,
                };
            }
            catch { return false; }
        }

        public static bool LuzInternaAcesa(int n) => n >= 1 && n <= 3 && (Veiculo.LuzesInternas & (1 << (n - 1))) != 0;

        public static void LuzInterna(int n) => Fazer("luz interna " + n, c =>
        {
            if (n == 1) c.ClicouLuzInterna1();
            else if (n == 2) c.ClicouLuzInterna2();
            else if (n == 3) c.ClicouLuzInterna3();
        });

        // ---------- portas ----------
        static Il2Cpp.MEPCtrlPortasPeds _portas;

        static Il2Cpp.MEPCtrlPortasPeds Portas()
        {
            if (_portas == null || _portas.WasCollected) _portas = UnityEngine.Object.FindObjectOfType<Il2Cpp.MEPCtrlPortasPeds>();
            return _portas;
        }

        /// <summary>Abre ou fecha uma porta (lado e número como em <see cref="Porta"/>).</summary>
        public static void Porta(Lado lado, int numero)
        {
            if (C == null || numero < 1 || numero > 6) return;
            var p = Portas();
            if (p == null) return;
            try
            {
                string m = "AbreFechaPorta" + (lado == Lado.Direito ? "Dir" : "Esq") + numero;
                typeof(Il2Cpp.MEPCtrlPortasPeds).GetMethod(m)?.Invoke(p, null);
            }
            catch (Exception e) { MelonLogger.Warning($"[Comandos] porta {lado} {numero}: {e.InnerException?.Message ?? e.Message}"); }
        }

        public static void Porta(Porta porta) { if (porta != null) Porta(porta.Lado, porta.Numero); }

        public static void TodasAsPortas()
        {
            if (C == null) return;
            try { Portas()?.AbreFechaTodasAsPortas(); }
            catch (Exception e) { MelonLogger.Warning("[Comandos] todas as portas: " + e.Message); }
        }

        // ---------- ar-condicionado ----------
        public static void ArCondicionado() => Fazer("ar-condicionado", c => c.ClicouLigaDesligaArCondicionado());
        /// <summary>Passa para o próximo nível do ventilador.</summary>
        public static void ArCondicionadoVentilador() => Fazer("ventilador", c => c.ClicouArCondicionadoVentilador());
        public static void ArCondicionadoTemperatura(int passos) => Fazer("temperatura", c =>
        {
            for (int i = 0; i < Math.Abs(passos); i++)
                if (passos > 0) c.ClicouArCondicionadoTemperaturaUp(); else c.ClicouArCondicionadoTemperaturaDown();
        });

        // ---------- retarder ----------
        public static bool RetarderAutomatico => C != null && C.retarderAuto;
        public static void RetarderMais() => Fazer("retarder +", c => c.ClicouRetarderUp());
        public static void RetarderMenos() => Fazer("retarder -", c => c.ClicouRetarderDown());
        public static void RetarderAuto() => Fazer("retarder auto", c => c.ClicouRetarderAuto());

        // ---------- suspensão a ar (mod SuspensaoAr, pelos atalhos que ele registra) ----------
        public static bool TemComandoSuspensao => Atalhos.Achar("SuspensaoAr", "Ajoelhar")?.TemAcao == true;
        public static void Ajoelhar() => Atalhos.Achar("SuspensaoAr", "Ajoelhar")?.Executar();
        public static void Elevar() => Atalhos.Achar("SuspensaoAr", "Elevar")?.Executar();
    }
}
