using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace PbsuLoader
{
    /// <summary>
    /// Uma janela só: a primeira instância segura o mutex e escuta um pipe nomeado; as seguintes mandam o id do
    /// site (ou vazio, para só trazer a janela à frente) por ali e saem — duas instâncias brigariam pelo limite
    /// de consultas do GitHub e pelos arquivos do jogo.
    /// </summary>
    static class InstanciaUnica
    {
        // Sufixo injetável para os testes usarem nomes próprios, sem encostar no mutex da janela real.
        internal static string Sufixo = "";

        static Mutex _mutex; // referência viva enquanto o processo existir: soltar o mutex abriria uma segunda janela

        public static string NomeMutex => @"Local\pbsu-loader-instalador" + Sufixo;
        public static string NomePipe => "pbsu-loader-instalador" + Sufixo;

        /// <summary>
        /// true: esta é a única instância (e passa a escutar o pipe). false: já existe outra — o id (ou vazio)
        /// é encaminhado para ela e quem chama deve encerrar.
        /// </summary>
        public static bool Entrar(string idParaEncaminhar, Action<string> aoReceber)
        {
            _mutex = new Mutex(false, NomeMutex);
            bool nossa;
            try { nossa = _mutex.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { nossa = true; } // a anterior morreu sem soltar: o mutex é nosso
            if (!nossa) { Encaminhar(idParaEncaminhar); return false; }
            Escutar(aoReceber);
            return true;
        }

        static void Escutar(Action<string> aoReceber)
        {
            var servidor = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(NomePipe, PipeDirection.In, 1))
                        {
                            pipe.WaitForConnection();
                            using (var leitor = new StreamReader(pipe))
                                aoReceber(leitor.ReadLine() ?? "");
                        }
                    }
                    catch (ObjectDisposedException) { return; } // app fechando
                    catch (ThreadInterruptedException) { return; }
                    catch { Thread.Sleep(200); } // cliente sumiu ou o pipe falhou: volta a escutar
                }
            }) { IsBackground = true };
            servidor.Start();
        }

        /// <summary>Encaminha o id (ou vazio) para a instância que está rodando. Uso dos testes também.</summary>
        internal static void Encaminhar(string id)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", NomePipe, PipeDirection.Out))
                {
                    pipe.Connect(2000);
                    using (var escritor = new StreamWriter(pipe)) { escritor.AutoFlush = true; escritor.WriteLine(id ?? ""); }
                }
            }
            catch { } // sem quem receber, não há o que fazer: o usuário abriu o instalador normal
        }
    }
}
