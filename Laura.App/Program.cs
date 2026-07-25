using System.Threading;
using System.Windows.Forms;
using Laura.App.Composition;
using Laura.App.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Laura.App;

/// <summary>
/// Ponto de entrada da aplicação Laura.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Inicializa o Windows Forms, compõe as dependências e entrega o controle à
    /// bandeja do sistema.
    ///
    /// Uma única instância é garantida por um mútex nomeado: dois processos
    /// disputariam o microfone e a tecla de atalho global.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            "Laura.VirtualAssistant.SingleInstance",
            out bool isFirstInstance);

        if (!isFirstInstance)
        {
            return;
        }

        // Precisa vir antes de qualquer controle existir na thread; depois disso o
        // Windows Forms recusa a troca de modo.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();

        // Instala o contexto de sincronização do Windows Forms antes de compor as
        // dependências, para que o IUiDispatcher capture a thread da interface.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        ServiceProvider provider = new ServiceCollection()
            .AddLauraServices()
            .BuildServiceProvider();

        try
        {
            InstallExceptionHandlers(provider.GetRequiredService<ILogger<LauraApplicationContext>>());

            using var context = new LauraApplicationContext(provider);
            Application.Run(context);
        }
        finally
        {
            provider.Dispose();
        }
    }

    /// <summary>
    /// Registra Laura como responsável por exceções não tratadas.
    ///
    /// Sem isso, uma falha em um manipulador de evento derruba o laço de mensagens e
    /// a aplicação fica presa em segundo plano: a bandeja continua lá, mas nada mais
    /// responde. Registrar e seguir mantém a assistente utilizável.
    ///
    /// Args:
    ///     logger: Destino dos registros de falha.
    /// </summary>
    private static void InstallExceptionHandlers(ILogger logger)
    {
        Application.ThreadException += (_, args) =>
            logger.LogError(args.Exception, "Exceção não tratada na thread da interface.");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogError(args.ExceptionObject as Exception, "Exceção não tratada no domínio da aplicação.");
    }
}
