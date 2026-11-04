using System;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Notas com Retenção de INSS")]
[assembly: AssemblyProduct("Notas com Retenção de INSS")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace NotasRetencaoINSS
{
    static class Programa
    {
        [STAThread]
        static void Main()
        {
            Thread.CurrentThread.CurrentCulture = Formatos.PtBr;
            Thread.CurrentThread.CurrentUICulture = Formatos.PtBr;
            ClienteFiscal.ConfigurarRede();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception is ErroAviso ? e.Exception.Message : "Erro inesperado:\n" + e.Exception, "Notas com Retenção de INSS",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new TelaPrincipal());
        }
    }
}
