using System;
using UnityEngine;

namespace PBSLoaderCommonUtils
{
    /// <summary>
    /// Uma tecla com os modificadores que precisam estar apertados junto: "Ctrl+Alt+V", "PageDown", "LeftAlt".
    /// Os modificadores valem pelo lado (Ctrl = esquerdo ou direito). AltGr conta como Ctrl+Alt, como no Windows.
    /// </summary>
    public readonly struct Combinacao : IEquatable<Combinacao>
    {
        public readonly KeyCode Tecla;
        public readonly bool Ctrl, Alt, Shift;

        public Combinacao(KeyCode tecla, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Tecla = tecla; Ctrl = ctrl; Alt = alt; Shift = shift;
        }

        public static readonly Combinacao Nenhuma = new(KeyCode.None);
        public bool Vazia => Tecla == KeyCode.None;

        // A própria tecla é um modificador ("LeftAlt" sozinho, para segurar): os modificadores não são
        // comparados, senão ela nunca bateria (apertar Alt já liga o Alt).
        public bool SoModificador => EhModificador(Tecla);

        public static bool EhModificador(KeyCode k) =>
            k is KeyCode.LeftControl or KeyCode.RightControl or KeyCode.LeftAlt or KeyCode.RightAlt or KeyCode.AltGr
              or KeyCode.LeftShift or KeyCode.RightShift or KeyCode.LeftCommand or KeyCode.RightCommand;

        internal static bool CtrlApertado => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        internal static bool AltApertado => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);
        internal static bool ShiftApertado => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Modificadores exatos: "F9" não dispara com Ctrl+F9 apertado, e "Ctrl+Alt+V" não dispara com Shift junto.
        internal bool ModificadoresBatem() =>
            SoModificador || (Ctrl == CtrlApertado && Alt == AltApertado && Shift == ShiftApertado);

        internal bool Apertou() => !Vazia && Input.GetKeyDown(Tecla) && ModificadoresBatem();

        // Segurar tolera modificadores a mais (segurar PageDown com Shift ainda é segurar PageDown).
        internal bool Segurando() =>
            !Vazia && Input.GetKey(Tecla)
            && (SoModificador || ((!Ctrl || CtrlApertado) && (!Alt || AltApertado) && (!Shift || ShiftApertado)));

        /// <summary>"Ctrl+Alt+V". O texto é o mesmo que fica gravado nas preferências.</summary>
        public override string ToString()
        {
            if (Vazia) return "";
            var s = "";
            if (Ctrl) s += "Ctrl+";
            if (Alt) s += "Alt+";
            if (Shift) s += "Shift+";
            return s + Tecla;
        }

        /// <summary>Texto para mostrar ao jogador: "Ctrl + Alt + V", "` (crase)", "Page Down".</summary>
        public string Legivel()
        {
            if (Vazia) return "Nenhum";
            var s = "";
            if (Ctrl) s += "Ctrl + ";
            if (Alt) s += "Alt + ";
            if (Shift) s += "Shift + ";
            return s + NomeTecla(Tecla);
        }

        public static string NomeTecla(KeyCode k) => k switch
        {
            KeyCode.BackQuote => "` (crase)",
            KeyCode.Quote => "' (aspas)",
            KeyCode.PageUp => "Page Up",
            KeyCode.PageDown => "Page Down",
            KeyCode.LeftAlt => "Alt esquerdo",
            KeyCode.RightAlt => "Alt direito",
            KeyCode.LeftControl => "Ctrl esquerdo",
            KeyCode.RightControl => "Ctrl direito",
            KeyCode.LeftShift => "Shift esquerdo",
            KeyCode.RightShift => "Shift direito",
            KeyCode.Return => "Enter",
            KeyCode.KeypadEnter => "Enter (numérico)",
            KeyCode.KeypadPlus => "+ (numérico)",
            KeyCode.KeypadMinus => "- (numérico)",
            KeyCode.KeypadMultiply => "* (numérico)",
            KeyCode.KeypadDivide => "/ (numérico)",
            KeyCode.KeypadPeriod => ", (numérico)",
            >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => (k - KeyCode.Keypad0) + " (numérico)",
            >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)(k - KeyCode.Alpha0)).ToString(),
            KeyCode.UpArrow => "Seta para cima",
            KeyCode.DownArrow => "Seta para baixo",
            KeyCode.LeftArrow => "Seta para a esquerda",
            KeyCode.RightArrow => "Seta para a direita",
            KeyCode.Space => "Espaço",
            KeyCode.Escape => "Esc",
            _ => k.ToString(),
        };

        /// <summary>Lê "Ctrl+Alt+V" (sem diferenciar maiúsculas). Vazio ou "Nenhum" = sem atalho.</summary>
        public static bool TentarLer(string texto, out Combinacao c)
        {
            c = Nenhuma;
            if (string.IsNullOrWhiteSpace(texto)) return true;
            var t = texto.Trim();
            if (t.Equals("Nenhum", StringComparison.OrdinalIgnoreCase) || t.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
            bool ctrl = false, alt = false, shift = false;
            var partes = t.Split('+');
            for (int i = 0; i < partes.Length - 1; i++)
            {
                var p = partes[i].Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
                else return false;
            }
            var nome = partes[^1].Trim();
            if (nome.Length == 1 && char.IsDigit(nome[0])) nome = "Alpha" + nome;
            if (!Enum.TryParse(nome, true, out KeyCode k) || k == KeyCode.None) return false;
            c = new Combinacao(k, ctrl, alt, shift);
            return true;
        }

        public bool Equals(Combinacao o) => Tecla == o.Tecla && Ctrl == o.Ctrl && Alt == o.Alt && Shift == o.Shift;
        public override bool Equals(object o) => o is Combinacao c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(Tecla, Ctrl, Alt, Shift);
        public static bool operator ==(Combinacao a, Combinacao b) => a.Equals(b);
        public static bool operator !=(Combinacao a, Combinacao b) => !a.Equals(b);
    }
}
