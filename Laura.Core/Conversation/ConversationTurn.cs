using System.Globalization;

namespace Laura.Core.Conversation;

/// <summary>
/// Pergunta encaminhada ao modelo generativo, com o contexto mínimo para respondê-la.
/// </summary>
/// <param name="Prompt">Texto do usuário, na forma original.</param>
/// <param name="Culture">Idioma esperado na resposta.</param>
/// <param name="UserDisplayName">Nome pelo qual o modelo pode se dirigir ao usuário.</param>
public sealed record ConversationTurn(string Prompt, CultureInfo Culture, string UserDisplayName);
