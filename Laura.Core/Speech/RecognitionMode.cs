namespace Laura.Core.Speech;

/// <summary>
/// Modo de escuta do reconhecedor de fala.
///
/// A separação existe por precisão: uma gramática restrita ao gatilho praticamente
/// elimina ativações acidentais, enquanto o ditado livre — necessário para comandos
/// arbitrários — é ruidoso demais para ficar ativo o tempo todo.
/// </summary>
public enum RecognitionMode
{
    /// <summary>Escuta suspensa; nenhum áudio é processado.</summary>
    Idle,

    /// <summary>Escuta apenas as frases de ativação ("Ok, Laura").</summary>
    WakeWord,

    /// <summary>Escuta um comando completo após a ativação.</summary>
    Command,
}
