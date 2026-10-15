# PrtSc, diagnóstico compartilhável e extensão do chat — 2026-10-15

## Auditoria antes da integração

Base da auditoria original: `61bcf84`, quando o produto suportava somente Gemini Live. Após a entrega inicial, o usuário confirmou a inclusão de GPT opcional. O novo adaptador, a seleção explícita e as verificações adicionais estão documentados em [GPT opcional](laura-gpt-provider.md). Gemini continua padrão e provedor de voz.

PrtSc: `ScreenshotOptions` e os defaults de INI usam PrtSc/Ctrl+PrtSc/Shift+PrtSc. `WindowsScreenshotShortcuts` registra via RegisterHotKey e relata falha, mas não trata o conjunto antigo que disputa captura do Windows. Não há hook de teclado a remover. [Microsoft RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) documenta conflito de registro, interação especial com PrintScreen e reserva de F12. A correção troca apenas os três defaults legados por Ctrl+Shift+F8/F9/F10 e preserva customizações; não altera o registro/políticas do Windows.

Logs: `LauraDiagnostics` tem amostras numéricas opt-in em memória para harnesses; `ILogger` escreve em Debug e alguns callers incluem comandos nos parâmetros. Portanto, exportar simplesmente o texto de Debug vazaria conteúdo. Novo journal deve consumir esquema técnico fechado e o logger deve ignorar argumentos/mensagens de exceção. Escrita/rotação/snapshot ficam em worker com fila limitada; falha do log não pode encerrar a assistente. Brain/áudio existentes fornecem latências, scores e gaps sem conteúdo.

Chat: MainPage usa CollectionView com no máximo 200 mensagens; SQLite retém 2.000 e ReadRecent limita 200. Uma exportação visual perderia mensagens. SQLite trunca texto em 16.000 caracteres; mensagens novas precisam ser preservadas integralmente. BrainConversationMemory separa notas duráveis de transcripts. ClearHistory existe, mas não encerra sessão remota nem invalida callbacks; também apaga observações técnicas, que são uma responsabilidade diferente. Rolagem usa LastVisibleItemIndex e Dispatch antes da disposição nativa, sem botão de retorno. Label não habilita seleção e Entry não aceita entrada multilinha.

Agent loop existente: AssistantEngine tem um consumidor de canal; Gemini mantém sessão, serializa solicitações, limita ferramentas concorrentes a quatro, aguarda resultados e faz até quatro continuations de tarefa. `MauiAssistantTools` usa `CognitiveTaskRun`: máximo de 32 chamadas, seis minutos por objetivo e duas tentativas da mesma operação/argumentos; negação/cancelamento bloqueia efeitos posteriores. Timeouts/cancelamento e validação central PermissionManager já existem. Esses limites foram reutilizados e receberam eventos técnicos; não se criou outro agent loop ou orçamento paralelo. A extensão acrescenta fencing de publicação de comandos antigos após reset.

## Referências e decisões

OpenClaw: [agent loop](https://github.com/openclaw/openclaw/blob/main/docs/concepts/agent-loop.md), [sessões](https://github.com/openclaw/openclaw/blob/main/docs/concepts/session.md), [fila](https://github.com/openclaw/openclaw/blob/main/docs/concepts/queue.md), [memória](https://github.com/openclaw/openclaw/blob/main/docs/concepts/memory.md) e [implementação da fila](https://github.com/openclaw/openclaw/blob/main/src/process/command-queue.ts). Inspeção em 15/10/2026, branch mutável main. As ideias pertinentes são serialização por sessão, cancelamento com fencing de escritores, separação transcript/memória e eventos técnicos. Não se importam código ou dependências. O reset da Laura deve drenar a sessão antiga e trocar a geração do consumidor antes de permitir novos envios; limpar elementos visuais é insuficiente.

OpenJarvis permanece referência local em `D:/DevelopmentLibrary/Apps/OpenJarvis`, especialmente traces/collector, agent loop e engine registry. Laura já possui descoberta ONNX/skills e evidências verificadas; não criar segundo Brain ou novo sistema de permissões. Aprendizado de experiência depende do resultado observado, não da mensagem do modelo. A execução externa que já começou não é desfeita pela limpeza do diálogo.

Referências complementares: [skills OpenClaw](https://github.com/openclaw/openclaw/blob/main/docs/tools/skills.md), [sandbox/políticas](https://github.com/openclaw/openclaw/blob/main/docs/gateway/sandboxing.md), [failover](https://github.com/openclaw/openclaw/blob/main/docs/concepts/model-failover.md). Skills são instruções com descoberta/gating; na Laura já existem revisão de hash, dependências e ferramentas registradas, sem dar autorização pelo texto. Política de ferramenta continua anterior à execução e o isolamento não transforma uma operação negada em permitida. Fallback de modelo é distinto de trocar o provedor e não deve repetir efeitos externos sem evidência de que não começaram. Nesta entrega, o fallback Gemini existente permanece; não se adicionam Docker, Gateway ou runtime Python/Node ao produto.

| Tema auditado | Adaptação na Laura |
|---|---|
| Loop e múltiplas etapas | `CognitiveTaskRun` + continuations existentes, resultados observados e limites preservados |
| Sessões/histórico/recuperação | `ConversationActions` drena provedor e usa barreira do consumidor; reconexão comum permanece distinta de reset confirmado |
| Markdown e recuperação entre conversas | BrainService/BrainContextBuilder permanecem independentes dos transcripts; limpar não invalida fatos |
| Skills/extensibilidade | Catálogo fechado no adaptador; nomes desconhecidos não viram labels técnicos com texto remoto |
| Fila/concorrência | Um consumidor de comandos, quatro slots de transporte e gates existentes; escrita de logs em worker limitado |
| Falhas | Códigos, tipos de exceção e frames sem paths/mensagens; perda/falha de log explícita |
| Permissões/isolamento | PermissionManager preservado; eventos técnicos registram apenas operação/risco/outcome |
| Streaming | Apenas transcrição visível do output; nenhuma gravação de raciocínio interno ou protocolo bruto |
| Provedores | Reset comum com cancelamento/drenagem; Gemini padrão e GPT opcional com credencial independente. Validação real Gemini e fixtures HTTP/nativas GPT, com limites descritos no relatório adicional |

## Etapas de implementação

1. Atalhos recomendados, migração restrita, validação de duplicatas e evidência nativa de registro/conflito.
2. Journal técnico local com rotação/retensão, contadores de perda/falha, contexto de correlação e exportação ZIP contendo report.md, summary.json e events.jsonl. Nenhum upload automático.
3. Reset real com cancelamento, barreira de geração, preservação do Brain/permissões e snapshot integral do histórico retido em Markdown/TXT/JSON.
4. Seleção/cópia nativas, compositor multilinha, rolagem vinculada ao layout e retorno discreto à última mensagem.
5. Testes de contratos, testes nativos em armazenamento sintético isolado, build completo e commits por tema. Limitações e resultados medidos serão registrados abaixo.

## Resultado funcional

**Atalhos:** Ctrl+Shift+F8 captura região, Ctrl+Shift+F9 janela e Ctrl+Shift+F10 tela inteira. O conjunto exato original de PrtSc é migrado junto, preservando AllDisplays e ShortcutsEnabled. Personalizações diferentes desse conjunto são mantidas; podem usar PrtSc conscientemente, com advertência sobre o Windows. Preferências oferece restaurar recomendados; Salvar valida duplicatas. F12 é recusada por ser reservada. RegisterHotKey continua sem substituir registro ocupado, agora com código Win32 no aviso e evento técnico. Nenhuma chave de registro ou preferência do Windows foi alterada.

**Diagnóstico:** journal automático de metadados em `%LOCALAPPDATA%/Laura/Diagnostics`, separado de Documents/Brain e da exportação do diálogo. Fila não bloqueante de 2.048 entradas; um worker, até oito segmentos de aproximadamente 1 MiB e retenção máxima de sete dias. Saturação descarta eventos, nunca áudio/comandos; o contador aparece no pacote. Falha de armazenamento mantém a assistente e é registrada em contador. Shutdown normal drena a fila; encerramento abrupto pode perder itens pendentes. Retenção por tamanho pode cobrir menos de sete dias.

O menu **⋯ → Exportar diagnóstico técnico** solicita destino local e gera um ZIP com `report.md`, `summary.json`, `events.jsonl`. Contém timestamps UTC, IDs de execução/interação, eventos e durações de Brain/ferramentas/áudio, scores/IDs de notas pseudonimizados, estados da UI/provedor, risco/decisão de permissão, saída real de processo quando disponível, configuração técnica permitida e recursos amostrados a cada 30 s. Gaps do dispatcher acima de 100 ms são sintomas medidos; não provam bloqueio ou causa. Espera de ReceiveAsync inclui rede/provedor e retomada pelo scheduler; processamento de mensagem é cronometrado separadamente.

O logger não chama formatter nem exporta argumentos, mensagens de exceção ou paths. Somente categorias/códigos e identidades de métodos; templates conhecidos têm códigos legíveis e os demais têm hash. Não captura comandos, respostas, persona, conteúdo do Brain, screenshots, PCM, nomes de dispositivos/usuário ou chaves. Unknown tool names recebem label genérico. IDs autorados de notas viram hashes; são pseudônimos, não anonimato estatístico garantido. Arquivos são revalidados ao exportar e linhas inválidas são contadas. Nenhum envio ao ChatGPT acontece automaticamente.

**Sessão e exportação:** limpar exige a confirmação especificada e avisa que operações externas iniciadas podem continuar. A entrada nova é suspensa no provedor enquanto a sessão antiga é encerrada e o consumidor cancela/drena a geração anterior, ignora comandos antigos enfileirados, apaga messages/learning receipts e limpa contexto transitório. Markdown, configurações, grants e evidências técnicas/versionadas de habilidades/experiências são mantidos. Falha na limpeza não é reportada como sucesso. Um efeito externo iniciado não é revertido nem morto pela exclusão do diálogo.

Exportar conversa consulta todo o histórico **retido** (até 2.000 mensagens), incluindo itens não renderizados (UI mantém 200). Mensagens novas não sofrem mais truncamento em 16.000 caracteres; truncamentos antigos não são recuperáveis. Com persistência desativada, a janela da sessão fica em memória. Alternar a opção não perde o histórico já carregado nesta execução. Merge preserva mensagens legitimamente repetidas com timestamp igual. Snapshot serializa com o reset e preserva corpo, voz, Unicode, código, autor e horários em Markdown/TXT/JSON UTF-8. Apenas mensagens concluídas entram; rascunho de streaming não persistido fica fora. Chave configurada e padrões reconhecíveis de credenciais são mascarados, mas isso não anonimiza todos os dados pessoais. Revise o diálogo antes de compartilhar.

**UX:** menu discreto no cabeçalho, copiar no hover/foco ou contexto e seleção nativa com Ctrl+C/Ctrl+A. Compositor Editor/TextBox aceita Ctrl+V/menu e múltiplas linhas, Enter envia e Shift+Enter insere linha; colar não envia. Windows pode normalizar o tipo de quebra de linha. Transcrição visível do Gemini aparece provisoriamente por streaming com coalescência no dispatcher, sem timers de scroll. O ScrollViewer segue mudanças reais de layout/viewport; gesto de subida suspende acompanhamento, mostra retorno à última mensagem e contador. Retornar ao final reativa acompanhamento. Selecionar texto não dispara cópia nem rolagem forçada. Conteúdo provisório é removido ao interromper e invalidado por versão ao limpar.

Exportações preparam o conteúdo fora da UI e fazem troca por arquivo temporário no destino escolhido, com tratamento de falhas. O seletor nativo é uma ação direta do usuário; não é uma nova ferramenta exposta ao modelo.

## Validação executada

- Build Release da solução e do harness: **zero avisos e erros**. Suíte completa: **438 testes aprovados**, zero falhas/ignorados, 29 s, `_Output/ChatDiagnostics/Tests/release-all.trx` (base anterior: 424; 14 casos acrescentados). Cobrem rotação/restart, fila saturada, falha de armazenamento, arquivo adulterado, redaction, três formatos, histórico não renderizado e longo, fencing do consumidor, contexto transitório e Markdown preservado. As expectativas antigas de apagar evidências técnicas ao limpar diálogo foram atualizadas deliberadamente para a nova separação de responsabilidades.
- `_Output/ChatDiagnostics/AcceptedNative/result.txt`: **PASS**. Registro concorrente real de Ctrl+Shift+F8 causou um único conflito; o dono anterior permaneceu e, após liberá-lo, os três recomendados registraram. Nenhuma captura de desktop foi disparada nem registro de PrtSc tentado. Native TextBlock selecionou tudo/trecho e menus reais copiaram o texto exato com Unicode/código. Native TextBox colou **61.500 caracteres** sem enviar em **169,01 ms** neste ensaio (inclui espera de binding/poll do harness; não é SLA). ScrollViewer seguiu layout/streaming, conservou uma posição manual emulada, mostrou badge `1` e retornou ao final. Reset real removeu histórico/contexto e stream pendente, preservou Markdown e não colapsou mensagens legitimamente repetidas. `_Output/ChatDiagnostics/Native3` e `NativeFinal` são execuções anteriores também aprovadas.
- `_Output/ChatDiagnostics/FullUi/result.txt`: **PASS**. Regressão nativa completa EN/PT, configurações, Brain, leitor/editor/grafo, navegação, fullscreen, mínimo 720×600, animação e segurança do microfone. Screenshots foram inspecionados. Cópia permanece discreta no hover/foco; estilo escuro/vermelho foi mantido.
- `_Output/ChatDiagnostics/LiveReset/session-reset.txt`: **PASS com Gemini real**, diálogo sintético, volume zero e sem microfone. Estabeleceu marcador temporário, recebeu deltas visíveis, cancelou turno em andamento durante reset, recusou pedido enquanto limpava, drenou eventos tardios, limpou SQLite e a sessão nova respondeu `EMPTY` ao perguntar pelo marcador anterior. Não usa conversas ou Brain pessoais.
- ZIPs técnico nativo e Gemini: **73 e 106 eventos**, respectivamente; nos snapshots, **zero descartes por pressão, falhas de escrita ou linhas inválidas**. Ambos abriram com os três arquivos esperados. Teste de privacidade garante que formatter/argumentos do logger e valores sintéticos de credenciais não entram nos arquivos. Contadores de perda/falha são desta execução; após restart, não devem ser confundidos com totais históricos.
- `_Output/ChatDiagnostics/AudioConcurrent`: **90 s de áudio nativo**, UI/32 escritas do Brain e journal ativos. Recebidos/lidos **4.320.000 bytes**, fila final zero, pico **839.998 bytes**, **zero leituras desalinhadas e um underrun**. Próximo pacote chegou 3,13 ms depois desse esvaziamento; intervalo medido entre chegadas foi 16 ms, sem inferir duração audível da pausa. Tempo total 91.132,13 ms; CPU do processo 10.781,25 ms; alocação 142.060.264 B; 3.239 ticks UI, p95 31,60 ms e máximo 107,33 ms. A suíte Core rodou em paralelo nesta execução. Não é comparação controlada de overhead nem prova de áudio sem pausas; o gap permanece registrado. Journal reteve 4.497 eventos de chegada e um de underrun; o trace opt-in separado continua limitado a 4.096 linhas.
- `git diff --check` e `Build/Verify-BrainPrivacy.ps1` passaram para os vaults isolados. Arquivos de áudio, diálogos sintéticos, ZIPs, credenciais protegidas de teste e métricas ficam em `_Output`, ignorado; `runtime-*.jsonl` também foi acrescentado ao ignore. Nenhuma dependência adicionada.

Durante a validação, o primeiro ensaio precisou reconhecer a normalização nativa de quebras de linha. O segundo encontrou `CLIPBRD_E_CANT_OPEN` ao restaurar o clipboard e falhou; o clipboard pode ter ficado com texto sintético dessa execução. O código passou a tratar contenção com tentativas breves/limitadas, sem exceção não tratada em ações de copiar; os ensaios seguintes preservaram/restauraram todos os formatos disponíveis em memória. Nenhum conteúdo anterior do clipboard foi colocado nos artefatos ou logs. O problema e a falha inicial não foram descartados da evidência (`Native1`/`Native2`).

### Critérios do anexo

| Caso | Evidência e limite |
|---|---|
| 1 — limpar sem apagar Brain | Teste BrainConversationReset + nota nativa com hash preservado |
| 2 — remover contexto anterior | WorkingContext vazio e Gemini real em sessão nova sem marcador |
| 3 — limpar em streaming | Fencing da fila, stream pendente nativo e cancelamento real Gemini sem deltas tardios |
| 4/5 — acompanhar mensagens/streaming | Extent/viewport reais no ensaio WinUI; estados Core testados |
| 6/7 — leitura manual/retorno | Posição manual emulada na mesma state machine + viewport nativo e badge; gesto físico fica manual |
| 8/9 — copiar inteiro/trecho | Seleção e menu por automation peer em TextBlock + clipboard real |
| 10 — colar múltiplas linhas | PasteFromClipboard nativo, Unicode/código e 61.500 caracteres sem submit |
| 11/12 — histórico completo/formatos | 231 linhas retidas contra 200 da UI, corpo de 50.000 caracteres e MD/TXT/JSON |
| 13 — confidencialidade | Logger sem formatter/valores, hashes, padrões/segredo exato removidos e arquivos adulterados filtrados |
| 14 — histórico extenso | Retenção de 2.000, virtualização de 200, exportação em worker e colagem nativa medida; não houve ensaio de milhões de mensagens |
| 15 — Gemini/GPT | Gemini real aprovado. GPT opcional implementado posteriormente: transporte/ferramentas/reset em fixtures HTTP, integração nativa e troca em ambas as direções. API real GPT pendente de chave OpenAI; ver relatório adicional |
| 16 — concorrência/duplicação | Cancelamento de dispatcher que ignora token, comandos velhos não executados, sessão remota drenada e merge preservando repetição legítima |

### Limites restantes

Teclas físicas Ctrl+C/Ctrl+A/Enter/Shift+Enter, menu de colar por clique físico e escolha de destino no seletor do SO permanecem verificações manuais; usam controles nativos e handlers inspecionados, sem alegar automação dessas teclas. A movimentação manual do scroll foi emulada no ensaio e aplicada ao ScrollViewer real. Não foi reproduzido o timbre metálico original nem corrigido todo underrun. Não houve benchmark A/B isolando custo do journal, sessão de muitas horas, queda de energia ou recuperação física do driver. Não se afirma que metadados sozinhos expliquem erros semânticos do modelo: para esses casos, forneça diálogo selecionado/revisado separadamente.

Redaction de conversa é defesa em profundidade, não detector universal de segredos. Logs não contêm payloads por construção dos callers/esquema, mas snapshots de metadados ainda devem ser revisados antes do envio. Falha do log não bloqueia ações; um pacote pode conter lacunas por retenção, pressão ou encerramento abrupto. Fontes OpenClaw em main são mutáveis, não uma auditoria de segurança de todo o projeto.

## Uso e reprodução

1. Use a Laura normalmente e reproduza o comportamento.
2. Abra **Atividade / Comandos → ⋯ → Exportar diagnóstico técnico** e escolha onde salvar o ZIP.
3. Envie o ZIP manualmente ao ChatGPT, junto com o horário aproximado, o que pediu, o esperado e o observado. O `report.md` inclui orientação para análise sem inventar causas.
4. Se precisar analisar o conteúdo da conversa, use **Exportar conversa** separadamente e revise o conteúdo pessoal. Limpar conversa não apaga os logs técnicos nem o Brain.

```powershell
dotnet build Laura.sln -c Release --no-restore
dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-build --no-restore
Laura.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Laura.exe --show --storage _Output/ChatVerify/Storage --ui-smoke _Output/ChatVerify --chat-diagnostics-smoke
dotnet run --project Build/Smoke/Laura.Smoke.csproj -c Release -- _Output/SessionVerify --gemini --key-file ../Mark-LV/config/api_keys.json --session-reset-smoke
```

Escolha uma pasta isolada nova por execução. O harness de chat usa o clipboard durante testes e tenta restaurar os formatos anteriores, sem escrevê-los em arquivos. O harness Gemini envia somente prompts sintéticos e requer a chave existente explicitamente indicada; a chave não é impressa. Logs normais não gravam áudio; A/B PCM continua um diagnóstico separado explicitamente habilitado.

## Código e commits

Criados: RuntimeJournal, RuntimeLogProvider, DiagnosticExport, ConversationExport, ConversationScrollState, ConversationStreamUpdate, ConversationActions, ConversationClipboard, RuntimeDiagnosticContext, WindowsConversationScroll, WindowsDocumentExport, ChatDiagnosticsSmokeRunner, partials do chat/harness e testes de diagnóstico/conversa/reset. Nenhum componente existente do produto removido; nenhum pacote adicionado.

Modificados: ScreenshotOptions, IniSettingsStore, ScreenshotSettingsView, WindowsScreenshotShortcuts, WindowsShortcutHost, ScreenshotCoordinator; IAssistantEngine/IConversationEngine/IConversationMemory/IAssistantTools, BrainConversationMemory, SqliteConversationMemory, AssistantEngine, CognitiveTaskRun, PermissionManager, GeminiLiveClient, NullConversationEngine; MainPage, ConversationBubble, Theme, ServiceConfiguration, LauraApplication, MauiAssistantTools/MauiSmokeRunner; Build/Smoke/Program.cs, locales EN/PT, testes relacionados, `.gitignore` e README. As notas/arquivos pessoais existentes não foram migrados destrutivamente.

Commits: `ce73071` atalhos; `4818164` journal/exportação técnica; `4a6ecff` contratos de sessão/histórico/streaming e ensaio Gemini; `223355d` integração nativa do chat e diagnóstico. O commit contendo este relatório e README encerra a documentação. Log reproduzível: `git log --oneline 61bcf84..HEAD`. Não foi feito push ou alteração no aplicativo instalado.
