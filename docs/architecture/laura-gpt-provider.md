# GPT opcional — 15/10/2026

O usuário autorizou suporte opcional ao GPT após a entrega `cca245e`. Gemini Live continua padrão. Configurações → Conexão permite selecionar Gemini Live ou GPT · OpenAI e manter duas chaves e dois modelos independentes. O GPT usa `gpt-5.4-mini` por padrão; o campo aceita outro identificador GPT. Chaves novas são verificadas antes de substituir o segredo protegido. O campo mascarado vazio nunca é salvo como uma chave falsa. O arquivo OpenAI usa DPAPI do usuário atual, separado de `google-key.dat`; nenhum segredo entra no INI.

Commits de implementação: `e24ed6e` (adaptador, reset, privacidade e testes) e `e68a7e7` (configuração, chat, exportação e integração nativa). Este relatório e as atualizações de README/auditoria ficam em commit documental separado.

## Conversa, contexto e ações

`OpenAiConversationEngine` implementa Responses API por HTTP/SSE, sem SDK/dependência adicional e com destino fixo `https://api.openai.com/v1/responses`. Redirecionamentos HTTP estão desabilitados. `store:false`, sem Conversations API ou `previous_response_id`. Durante uma tarefa, o adaptador reenvia os itens completos de output, inclusive conteúdo de raciocínio opaco e `phase`, para concluir chamadas de ferramentas. Somente texto público aparece no streaming e histórico; protocolo bruto, raciocínio, argumentos, imagens, chaves e conteúdo das respostas não entram no diagnóstico técnico.

Entre turnos, usa até oito mensagens do histórico permitido ou um contexto volátil da sessão (máximo oito mensagens e 128 mil caracteres, descartando mensagens antigas inteiras). O contexto volátil permite conversar sem persistência em disco. Limpar/trocar o provedor descarta esse contexto. Contexto factual vem do mesmo `BrainContextBuilder`, respeitando as configurações existentes; notas Markdown, catálogo e permissões não são duplicados.

As declarações Gemini das ferramentas são convertidas para JSON Schema com tipos minúsculos, preservando campos opcionais (`strict:false`). Ferramentas do Google não são anunciadas ao GPT. Ferramentas locais registradas passam pelo mesmo `MauiAssistantTools`, `CognitiveTaskRun`, MCP e `PermissionManager`. Mantêm-se plano, orçamento nativo de 32 chamadas/seis minutos/duas operações idênticas e resultados observados. O transporte adicional também limita 32 chamadas, 38 respostas e quatro continuations justificadas, desabilita chamadas paralelas e rejeita identidades repetidas. Uma recusa/cancelamento interrompe ferramentas posteriores do mesmo lote; a resposta final recebe `tool_choice:none`. Erros não provocam troca automática ou repetição de efeitos.

SSE tem limite de 2 MiB por evento e 8 MiB por resposta; encerramento sem `response.completed` é falha. Não se executam ferramentas a partir de argumentos parciais. Timeout/cancelamento atravessam HTTP, contexto, ferramentas e permissões. O reset aguarda até uma ferramenta que ignore token retornar e descarta seu resultado tardio. Operações externas já iniciadas não são revertidas por esse cancelamento.

Imagens estão habilitadas para `gpt-5.4-mini` e seu snapshot documentado `gpt-5.4-mini-2026-03-17`; outros modelos permanecem texto até revisão. Os pixels são congelados, limitados a 32 MiB por imagem/três por tarefa e autorizados pelo destino `api.openai.com`, hash, MIME e modelo antes de cada envio. Fechar uma captura editável cancela divulgações futuras. A autorização concedida ao Google não substitui a autorização para OpenAI.

`ConversationProviderRouter` serializa pedidos e drena ambos os provedores antes de aplicar a troca ou a barreira de limpar histórico. Pedidos novos são recusados durante reset. A troca mantém diálogo retido e Brain; limpar mantém Brain e remove diálogo/contexto. Não há fallback entre fornecedores. Exportação de conversa mascara também a chave OpenAI exata, além dos padrões existentes. O journal registra provedor, categoria de modelo, HTTP status, latência e contadores de tokens, sem payload.

## Voz e interface

GPT é chat por texto nesta entrega. O HUD identifica o modelo e desabilita escuta/microfone; Ctrl+Espaço também verifica o modo antes de qualquer captura. O Gemini continua disponível ao selecioná-lo novamente, com sua chave, voz e modelo preservados. Não se enviam respostas do GPT ao Google para síntese implicitamente. A animação Pensando acompanha o estado do motor; o feedback falado e os anúncios de abertura continuam recursos do modo Gemini.

## Verificação

- Suíte Core Release: **465 testes aprovados, zero falhas/ignorados**, em 28 segundos; 27 casos novos dedicados ao GPT/Brain. Solução Release compilada com zero avisos/erros. Auditoria de privacidade e `git diff --check` aprovados.
- Testes HTTP determinísticos: streaming visível e output canônico; raciocínio não exibido; repetição completa de itens; conversão de schemas; associação `call_id`; resultados observados; recusa/cancelamento no lote; chamadas inválidas/repetidas; limites; HTTP 401/429/500; SSE truncado/inválido/excessivo; timeout; reset durante streaming e ferramenta não cooperativa; troca entre provedores; imagem recusada/aprovada e autorização a cada envio; falta de chave; INI; contexto volátil e reset.
- Integração com Brain real: recuperação Markdown compartilhada, respeito à memória desabilitada e preservação do hash da nota após limpar sessão.
- Harness MAUI/WinUI com chaves sintéticas em armazenamento isolado: seleção nos dois sentidos, DPAPI independente, máscara, remoção, ausência de fallback sem chave, páginas EN/PT em 1440×900 e 720×600. Capturas inspecionadas visualmente; a seção inferior continua alcançável por rolagem. Nenhum acesso à nuvem, clipboard ou microfone nesse ensaio.
- Artefatos locais ignorados pelo Git: `_Output/Gpt/Native/result.txt`, PNGs e `_Output/Gpt/Tests/release-all.trx`. Comando nativo: `Laura.exe --storage <pasta-isolada> --ui-smoke <saida> --gpt-settings-smoke`.

A API real GPT não foi exercitada: não existe chave OpenAI configurada no armazenamento padrão. Fixtures validam o contrato implementado, sem demonstrar disponibilidade, cota, qualidade ou sucesso de tarefas no modelo real. Configure a chave em Conexão para uso normal; uma chave inválida ou modelo sem acesso mantém a credencial anterior. Não foi feito push nem substituição do aplicativo instalado.

## Documentação consultada

[Responses e function calling](https://developers.openai.com/api/docs/guides/function-calling), [streaming](https://developers.openai.com/api/docs/guides/streaming-responses), [estado de conversa](https://developers.openai.com/api/docs/guides/conversation-state) e [GPT-5.4 mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini), consultados em 15/10/2026. A implementação usa o contrato oficial de funções/resultados e mantém estado local limitado para permitir reset verificável.
