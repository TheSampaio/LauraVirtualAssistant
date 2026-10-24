# Brain, concorrência e estabilidade do áudio — 2026-10-14

> **Laura 2.2 runtime notice (2026-10-20):** Live/audio behavior and measurements below describe historical implementations. Current operation uses local Whisper/Kokoro, supervised local inference and optional text providers. See [current architecture](laura-local-first.md) and [implementation evidence](../../LAURA-2.2-MIGRATION.md). Historical results are not new acceptance measurements.

## Auditoria inicial

Base: `4080246`, após clima e feedback de processamento. A suíte anterior contém 391 testes aprovados. Este relatório distingue código inspecionado, medições e hipóteses; o sintoma de timbre metálico ainda não foi reproduzido nem atribuído ao provedor.

| Fluxo / recurso | Implementação encontrada | Responsabilidade / risco observado |
|---|---|---|
| Texto | `MainPage` → canal de `AssistantEngine` → `GeminiSkillDispatcher` → `GeminiLiveClient.AskAsync` | Worker em background; histórico SQLite, contexto transitório e ferramentas existentes. |
| Entrada de voz | `WaveInEvent` → canal limitado de 30 pacotes → WebSocket | Captura 16 kHz mono; silêncio/VAD no Gemini; push-to-talk usa atividade explícita. Canal de entrada descarta pacotes antigos sob pressão: diferente do buffer de saída. |
| Contexto textual | `AskAsync` → `BrainService.RefreshAsync`, `BrainSkills.DiscoverAsync`, `RecallAsync` | Faz rescan integral antes de toda mensagem, inclusive cumprimentos. Voz não faz essa mesma preparação antes da geração; ferramentas usam o mesmo Brain. |
| Contexto da voz | transcrição → `SetWorkingContext`; conclusão → `ContinueVoiceTurnAsync` | Cada fragmento troca o ID do turno. O método assíncrono de conclusão pode executar SQLite sincronamente no receiver antes de um await incompleto. |
| Recuperação | passagens FTS5 + ONNX opcional + ativação de relações autoradas | Budget, hashes, origem, confiança e conflitos já existem. Não criar outro índice ou memória. |
| Aprendizado | `BrainLearning`, propostas com citações literais e fila limitada; recibos transacionais em SQLite | Categorias atuais não incluem localização de aplicativo. Declaração de instalação não entra no parser conservador; referências de aplicativo só aparecem após processo observado. |
| Persistência | `FileBrainStorage.Write`, gate do serviço, hash otimista, histórico e drafts | Flush de disco e rename; índices reconstruíveis. Cancelamento/falhas existentes devem preservar Markdown confirmado. |
| Escrita / índice | `BrainService.SaveAsync` → `Scan(false)` → `BrainIndex.Update` | Cada gravação enumera/lê todo o vault e reconstrói snapshot/topologia. O índice evita embeddings de bytes iguais, mas percorre todas as passagens. Trabalho está fora da UI; ainda ocupa CPU e gates compartilhados. |
| Grafo / UI | `BrainService.Changed` → `MemoryPage.RefreshAsync` → `BrainGraphView.SetGraphAsync` | Cada evento despacha refresh; há guards de geração, mas não coalescência. Stamp inclui hash do corpo, então editar texto provoca novamente 45 iterações de layout mesmo sem mudar topologia. Comunidades/filter/stamp são calculados na UI; layout é Task.Run. |
| Aplicativos | `BrainApplications` → catálogo Windows → `WindowsProcessLauncher` | Referência observada revalida SHA-256 e gate de processo. Processo criado e aceitação shell já são distintos; falta aproveitar a localização ensinada antes do primeiro lançamento. |
| Outras ferramentas | `MauiAssistantTools` → registro fechado de permissões → adaptadores | Observações estruturadas para processos/tarefas, limites e continuations existentes. Há retornos textuais negativos sem `Failed=true`, que confundem o estado da orquestração. |
| Áudio de saída | Gemini inlineData/base64 → PCM → `PcmPlaybackBuffer` → `WaveOutEvent` | 24 kHz/16 bit/mono, sem conversão de sample rate. Buffer limitado a 120 s. Não valida MIME do fragmento; não conta underruns. Após priming inicial, um gap não reativa priming. |
| Callback de playback | `MeteredProvider.Read` → buffer + medição RMS | Independente da UI; locks curtos. Dados recebidos e dados lidos não têm comparação opt-in. Não é possível atribuir microcorte/timbre com a observabilidade atual. |
| Concorrência | gates de Brain, índice, aprendizado, sessão/conexão, read-aloud, captura; timers e watcher | Domínio singleton; Task.Run para disco/SQLite/ONNX/layout. APIs legadas bloqueiam via GetAwaiter, normalmente chamadas por workers, mas callbacks precisam de isolamento. |
| Síncrono legítimo | Git safety no storage; compatibilidade `IConversationMemory` | Não remover verificações de Git/permissão nem colocar todo o projeto em Task.Run. Revisar apenas os callers críticos. |

## Problemas confirmados e ordem de correção

1. Rescan e eventos redundantes: medir baseline em vault sintético e UI nativa, depois recuperação adaptativa e publicação incremental.
2. Caminho ensinado não é referência reutilizável: adicionar declaração operacional inerte, deduplicação/correção com revisão, resolução única e validação local antes do gate original.
3. Assimetria cognitiva e receiver: compartilhar preparação adaptativa, resolução nativa e aprendizado por modalidade; processamento de transcrições concluídas e ferramentas não deve bloquear chegada do PCM.
4. Layout depende de texto: separar identidade visual/topologia de conteúdo e agrupar eventos; manter inspeção/editor atualizados.
5. Falhas textuais sem flag: tornar resultados de execução honestos, sem transformar mera aceitação shell em processo confirmado.
6. Diagnóstico de áudio insuficiente: instrumentar formato, chegada, buffer, underruns e comparação A/B explicitamente habilitada. Correções somente após ensaios. Não alterar voz/sample rate para mascarar sintomas.

## Hipóteses ainda abertas

Qualidade do áudio produzido pelo modelo; latência de rede sustentada; driver/dispositivo e carga externa; representatividade do corpus pessoal; pausas de GC como causa dos engasgos. As observações acima mostram trabalho redundante, mas não comprovam sozinhas qual causou cada sintoma relatado.

## Invariantes

Markdown continua sendo a fonte, sem migração destrutiva ou dependências novas. Memória nunca autoriza execução. Declaração do usuário, bytes validados e processo observado são evidências distintas. Credenciais/conteúdo privado não entram em métricas. Gravação de áudio somente em diagnóstico explícito, com armazenamento isolado e sinal sintético/consentido. Interface e identidade visual preservadas.

## Evidências e validação

As medições antes/depois, testes A–L, commits e limitações serão registrados neste documento após cada etapa. Não se presume que áudio sintético com buffer sozinho valide hardware ou timbre perceptível.

### Etapa 1 — referência nativa

Build Release passou sem avisos/erros. `_Output/BrainStability/BaselineMeasured`: 80 fatos sintéticos iniciais, página de memória visível, 32 revisões do mesmo fato, sem rede/microfone. 15.469,33 ms totais; 45 scans/atualizações de índice; 20 layouts; 8.234,38 ms de CPU; 127.091.824 bytes alocados. Timer de UI (16 ms): 604 ticks, p95 31,29 ms, máximo 68,24 ms. `GC.GetGCMemoryInfo` informou 2,81 ms de pausas no último GC: não é soma de todas as pausas do ensaio. Os tempos de operações se sobrepõem; não devem ser somados como timeline exclusiva. Timer mede jitter do dispatcher, não tempo de cada frame renderizado nem duração exata de bloqueio.

Instrumentação: `LauraDiagnostics` usa Meter/Stopwatch, escopo de ID e captura opt-in limitada a 4.096 linhas sem texto/caminhos. Escrever/scan/search/index/recuperação/layout estão medidos. O runner nativo mantém o estado final persistido e verifica que a UI continuou recebendo ticks. Isso é baseline observado, sem alegar resolução de engasgos ou do timbre.

### Etapa 2 — recuperação adaptativa

`BrainContextBuilder` centraliza contexto factual e descoberta de skills, com orçamento existente. Só uma lista fechada de cumprimentos/agradecimentos/data/hora dispensa recuperação; pedidos curtos desconhecidos continuam consultando. `RefreshIfDirtyAsync` reutiliza snapshot coerente e mantém refresh explícito para leituras com verificação de hash. Cache factual tem 32 entradas e invalida por snapshot/configuração; não armazena aprovação ou instruções de skills. `AskAsync` usa o serviço; o uso na fronteira de ferramentas de voz vem na etapa 5. Sete testes novos verificam classificação, cache/correção, memória desativada, snapshot limpo e cancelamento. Não foi alegada equivalência universal de interpretação do modelo.

### Etapa 3 — declarações operacionais

O parser conservador reconhece “O Neural Screen está instalado em <local absoluto>” / “The Neural Screen is installed in <local absoluto>”, incluindo prefixo “Correção:” / “Correction:”. A proposta fundamentada `learn_memory` também aceita categoria `application`, com nome/local literal da evidência real. Reutiliza nota `application`, YAML existente e chave estável: hash de executável vazio distingue localização declarada de execução observada. Diretórios/arquivos inexistentes podem ser conhecimento declarado, nunca execução comprovada. Correção exige ID atual e preserva revisão; local divergente sem correção fica em Inbox. Foi corrigida deduplicação por título que indevidamente esconderia mudança de local. Rede/relativos/evidência inventada são recusados. Três testes novos e dois de referência/proteção passaram; build Release limpo. Resolução/validação antes de abrir vem na etapa 4.

### Etapa 4 — conhecimento antes da execução

`BrainApplications.ResolveAsync` diferencia aplicativo desconhecido de conhecimento ambíguo/inválido/contraditório. Local declarado procura apenas até 32 `.exe` diretamente no diretório, exige nome compatível único e revalida SHA-256/localidade pelo guard existente. “Neural Screen” e “NeuralScreen.exe” são equivalentes apenas por normalização de espaços; empates não escolhem um arquivo arbitrário. Conhecimento conhecido inválido impede fallback especulativo e pede correção. `open_app` usa o mesmo launcher/gate, congelando hash e vault; apenas processo observado promove a nota, preservando ID. Resultados carregam `Launch` e protocolo informa `executionVerified` separado de `goalVerified=false`. Aceitação shell sem PID não vira execução verificada. Dez casos relacionados passaram; os binários desses testes são fixtures de contrato, não processos Windows reais. Nenhuma autorização foi concedida pelo aprendizado.

### Etapa 5 — capacidades compartilhadas entre modalidades

Transcrição completa passa a ter origem `UserVoice`, com os mesmos recibos/políticas/Brain do texto. Fragmentos atualizam o conteúdo mantendo um único ID; a próxima entrada cria novo ID. A preparação adaptativa compartilhada ocorre no texto antes da geração e na fronteira de ferramentas `open_app`/`run_program`/`run_local_command` antes da execução em ambas as modalidades. O resultado transmite somente contexto factual limitado, explicitamente não autorizador. Em voz Live, a geração pode começar antes da transcrição final: não se afirma recuperação anterior a todo raciocínio do provedor. Resolução nativa de aplicativos não depende dessa escolha do modelo. Escopos de interação correlacionam recuperação e ferramenta/outcome. Testes usam os adaptadores/recibos reais com transcript sintético em cada sentido; microfone físico ainda não foi utilizado.

### Etapa 6 — callbacks e coalescência

Conclusão de voz sai do receiver antes de SQLite/Brain e é serializada. Continuação/captura conserva o token original; histórico atrasado não substitui o contexto de uma solicitação mais nova. Ferramentas também cedem imediatamente o receiver e têm quatro slots reais, eliminando a corrida no limite contado pelo dicionário. `MemoryPage` agrupa bursts por 120 ms, no dispatcher, e atualiza novamente se houve mudança durante a renderização; não faz retry visual infinito de falha. Stamp do grafo usa identidades/topologia, então editar corpo atualiza dados/inspector sem repetir layout. Seis testes relacionados passaram, incluindo backpressure/shutdown de aprendizado e preservação de turno mais novo. A medição nativa comparável será repetida após a etapa 7, separadamente desses contratos.

### Etapa 7 — publicação e índice incrementais

Gravação relê apenas o documento e a revisão realmente escritos, publica snapshot coerente e preserva as validações/hash/flush/rename. Watcher guarda paths alterados e reconcilia conteúdo/exclusão; echoes de hashes iguais não republisham. Eventos de diretório/anexos e rescan periódico mantêm reconciliação completa. Falha marca nova reconciliação; arquivo Markdown confirmado não é apagado. Topologia de recuperação reutiliza vizinhanças quando identidades elegíveis e arestas não mudam; índice não repete SQL/vetores de notas iguais no mesmo modelo. Mudança de vault/modelo, schema, arquivo de índice ou falha invalida o cache. Vetor ausente após erro continua elegível para retry. Dois novos testes provam ausência de leituras alheias e reconciliação real por watcher; 186 testes do Brain passaram antes dos últimos guards, com regressão final prevista na etapa 9.

| Mesmo fixture nativo | Antes | Depois |
|---|---:|---:|
| Total (32 revisões) | 15.469,33 ms | 13.396,55 ms |
| CPU | 8.234,38 ms | 6.812,50 ms |
| Escrita média / máxima (32 operações) | 459,53 / 572,41 ms | 395,01 / 605,56 ms |
| Bytes alocados | 127.091.824 | 68.415.576 |
| Scans completos | 45 | 13 |
| Layouts visuais | 20 | 0 |
| Layouts, tempo total | 14,03 ms | 0 ms |
| Refresh do grafo, total / máximo | 252,37 / 31,96 ms | 2,23 / 0,27 ms |
| Atualizações do índice | 45 | 45 |
| Total medido de indexação | 2.659,38 ms | 2.881,85 ms |
| Timer UI p95 / máximo | 31,29 / 68,24 ms | 31,95 / 63,15 ms |
| Working set ao fim | 271.978.496 B | 275.771.392 B |

Artefatos: `_Output/BrainStability/After/brain-performance.json`. São dois ensaios nesta máquina, não garantia estatística. Alocação caiu 46,2% e CPU 17,3%; frequência de indexação e p95 não melhoraram. Escrita média caiu, mas a pior escrita aumentou. A média é o total medido dividido pelas 32 operações; tempos sobrepostos não são somáveis como duração exclusiva. Refresh teve 20 chamadas antes e 23 depois, com topologia reutilizada. Pausas do último GC passaram de 2,81 para 12,13 ms, sem atribuição causal; não foi somado o histórico completo de GC. Não foi executado benchmark comparável de latência de recuperação: existem traces de busca/recall e contratos de cache/invalidação, sem afirmar ganho numérico nessa latência. A instrumentação permite investigar carga maior sem esconder essas limitações.

### Etapa 8 — formato, alinhamento e áudio A/B

Contrato do provedor: [Gemini Live capabilities](https://ai.google.dev/gemini-api/docs/live-api/capabilities) documenta PCM little-endian 16 bit e saída 24 kHz. `PcmAudioFormat` valida PCM/rate/canais recebidos; não foi introduzida conversão ou mudança de sample rate/voz. `PcmPlaybackBuffer` retém fragmentos incompletos de amostra, só entrega pares de bytes e sinaliza término truncado. Depois de um gap ativo, reprime sem descartar palavras; priming/silêncio de turno concluído não são underruns.

Defeito local reproduzido **antes** da correção: dispositivo nativo recebeu duas leituras com bytes ímpares no fixture de 144.000 bytes com metade de amostra atrasada. A concatenação de bytes reais era igual, mas a inserção de padding/silêncio entre metades podia deformar a interpretação PCM. **Depois**: 144.000 bytes íntegros, zero leituras desalinhadas. Houve um gap contado em cada ensaio curto; não foi escondido. Artefatos `_Output/BrainStability/AudioBefore` / `AudioAfter`.

`PcmAudioDiagnosticCapture` é explicitamente instanciado pelo harness, limita A/B a 8 MiB cada, captura em memória sem I/O no callback e exporta WAV 24 kHz depois de drenar. A é PCM recebido; B contém bytes reais enviados ao queue do dispositivo, excluindo silêncio introduzido em gaps/priming. Igualdade de hashes comprova integridade/ordem dos bytes, não ausência de gaps ou timbre subjetivo, mixer/driver/DAC. Nenhum áudio é gravado no uso normal.

Resultados executados:

- 29 testes de áudio/contratos relacionados passaram; inclui 90 segundos de PCM fragmentado em teste determinístico, fila limitada, cancelamento/truncamento e rebuffer.
- Native integrado: **90 s**, 4.320.000 bytes recebidos/lidos, zero underruns e zero leituras desalinhadas, enquanto 32 revisões do Brain e UI estavam ativos. 3.385 ticks UI; p95 31,64 ms, máximo 68,96 ms. Arquivos `_Output/BrainStability/Integrated`. Trace detalhado é limitado a 4.096 linhas; grupos retidos não representam a contagem total de todo o ensaio.
- Gemini real, texto sintético e voz Charon, volume zero, sem microfone: **36,960 s**, 1.774.082 bytes A/B com SHA-256 idêntico, zero desalinhamentos. **Dois underruns** observados; maiores intervalos de chegada 1.406 / 1.328 / 953 ms. Isso mostra irregularidade de entrega, mas ainda não distingue atraso do provedor, rede ou agendamento do receiver em cada episódio. WAVs e `long-audio.json` em `_Output/BrainStability/LiveAudio` permitem escuta e correlação reproduzível.

A degradação subjetiva do relato original não foi reproduzida com o documento/conversa exatos. Não se atribui timbre ao modelo por exclusão. O defeito de alinhamento demonstrado foi corrigido; gaps de transporte e diagnóstico de voz metálica continuam limites explícitos. Nenhuma equalização/estiramento/resampling foi usada para mascarar som.

### Etapa 9 — validação integrada e relatório final

Build Release da solução, harness e GUI fixture: zero avisos/erros. **424 testes passaram**, zero falhas/ignorados, `_Output/BrainStability/Validated/validated-all.trx` (28 s). A base desta solicitação era 391; foram acrescentados 33 casos. Os testes finais incluem busca/escrita/áudio/ferramentas simuladas simultâneos, cancelamento sem escrita, inicialização lenta sem bloquear o lock do contexto transitório, prompt longo com consulta limitada e identidade conflitante sem fallback. A inicialização legada tem gate próprio; IDs do áudio são consultados sem criar snapshots de texto. Trace de recuperação inclui IDs/scores/modo lexical ou híbrido; underruns agora têm timestamp/ID para correlacionar com chegada dos fragmentos. Continuação de voz supersedida não executa trabalho pendente de outro turno.

Validação nativa final:

- `_Output/BrainStability/NativeApps/result.txt`: declaração literal registrada, referência resolvida por nome, hash revalidado e processo **real** iniciado no fixture GUI descartável. Negação nativa impediu execução mesmo com conhecimento; nova aprovação exata foi necessária. A nota foi promovida somente com PID observado. O harness invocou os controles reais de aprovação, não um clique físico do proprietário. Não houve uso de aplicativo pessoal ou confirmação de janela pronta.
- `_Output/BrainStability/FinalUi/result.txt`: inicialização oculta/toggle, navegação, grafo/reader/editor, árvore de arquivos, EN/PT, fullscreen, layout mínimo, acessibilidade das ações, animação Pensando e reinícios silenciosos do output passaram. Screenshots nativos foram produzidos; nada capturou desktop/conteúdo de outras aplicações.
- `_Output/BrainStability/LiveVoices`: Charon, Puck, Kore, Fenrir e Aoede produziram transcript/PCM; sessão persistente e reconexão passaram. Segundo ensaio prolongado, **Aoede**, 28,750 s / 1.380.002 bytes: A/B idênticos, **zero underruns e desalinhamentos**. O primeiro ensaio Charon com dois underruns é conservado acima, sem selecionar apenas o resultado melhor.
- `_Output/BrainStability/FinalVoice/long-audio.json`: repetição final com **Aoede**, depois dos guards de encerramento, 38,780 s / 1.861.442 bytes, A/B idênticos, zero desalinhamentos e **seis underruns**. Buffer máximo de 333.120 bytes; os 132 registros de chegada cobrem 44,119 s entre primeiro e último fragmento, mais que a duração do áudio. Nos seis esvaziamentos, o próximo fragmento chegou respectivamente 4.513 / 3.368 / 1.214 / 117 / 31 / 2.932 ms depois. O maior intervalo entre chegadas foi 5.297 ms. Isso confirma ausência de dados durante as pausas; não isola provedor/rede de agendamento/processamento local do receiver. O resultado sem underruns da execução anterior não se generaliza.
- `_Output/BrainStability/VoiceCancellation/result.txt`: oito reconfigurações e quatro descartes do cliente drenaram/cancelaram callbacks de voz concluída, sem notificação tardia, disponibilidade retida após descarte ou captura de microfone. O fechamento marca o cliente antes do cancelamento e bloqueia callbacks que tentariam se vincular a uma nova fonte de operações.
- Auditoria `Build/Verify-BrainPrivacy.ps1` passou nos vaults isolados: nenhum conteúdo/cache/credencial entrou no índice ou histórico Git. `git diff --check` passou. Os artefatos PCM e dados sintéticos estão somente em `_Output`, ignorado.

#### Matriz A–L

| Requisito | Evidência executada | Limite |
|---|---|---|
| A — local ensinado → abrir | `DeclaredInstallationResolves...` + native GUI por nome | Processo observado; prontidão de janela não presumida |
| B — texto ↔ voz | `BothUserModalitiesShareDurableLearning...`, transcrições parciais/recibos de produção | Transcripts sintéticos; microfone físico não usado |
| C — correção | `InstallationDeclarationDeduplicatesCorrects...` | Parser explícito/proposta literal, sem interpretação universal |
| D — deduplicação | mesmo teste e consolidação preexistente | Repetição não eleva confiança |
| E — persistência | restart com cache novo e testes de recibos/revisões | Crash físico/queda de energia não injetados |
| F — UI durante escrita | dois benchmarks nativos comparáveis | Jitter de timer, não deadline de frame; p95 não melhorou |
| G — grafo incremental | 20 layouts → 0 em revisões de corpo; testes de relações existentes | Mudanças de topologia continuam exigindo layout |
| H — áudio prolongado | 90 s nativo + 36,96 s Charon + 28,75 e 38,78 s Aoede + 90 s determinístico | Timbre subjetivo/driver requerem escuta; ensaios reais tiveram 2, 0 e 6 underruns |
| I — concorrência | native 90 s/UI/escrita + `AudioReadsWritesRetrievalAndSimulatedTools...` | Ferramentas concorrentes são simuladas nesse caso; lançamento real separado |
| J — sucesso técnico incompleto | `TechnicalAcceptanceDoesNotInventVerifiedExecution` | Não pretende verificar objetivos arbitrários do usuário |
| K — cancelamento | fila/worker/propostas, escrita previamente cancelada, áudio truncado/interrompido e 12 casos nativos de reconfiguração/descarte | I/O nativo em andamento não é desfeito |
| L — recuperação | recibos/replay idempotente, índices corrompidos/removidos e guard de reconciliação | Markdown confirmado permanece fonte; sem falha elétrica física |

#### Código e commits

Componentes criados: `BrainContextBuilder`, parser parcial `BrainLearning.Applications`, `LauraDiagnostics`, `PcmAudioFormat`/`PcmAudioDiagnosticCapture`, `BrainPerformanceSmokeRunner` e os testes de contexto/operacional/modalidade/incremental/concorrência/verificação/áudio. Nenhum componente do produto foi removido. **Nenhuma dependência adicionada.** Nenhuma migração destrutiva de memórias, configuração pessoal, permissões ou identidade visual.

Arquivos do produto modificados/criados:

- `Laura.Core/Brain/BrainService.cs`, `BrainIndex.cs`, `BrainContextBuilder.cs`, `BrainSkillDiscovery.cs`, `BrainApplications.cs`, `BrainConversationMemory.cs`, `BrainLearning.cs`, `BrainLearning.Applications.cs`, `BrainLearning.Proposals.cs`, `BrainMarkdown.cs`.
- `Laura.Core/Abstractions/IConversationMemory.cs`, `IAssistantTools.cs`; `Laura.Core/Diagnostics/LauraDiagnostics.cs`; `Laura.Core/Conversation/ConversationMessageSource.cs`, `GeminiLiveProtocol.cs`, `PcmPlaybackBuffer.cs`, `PcmAudioDiagnostics.cs`.
- `Laura.Platform.Windows/Conversation/GeminiLiveClient.cs`, `LiveAudioPlayer.cs`.
- `Laura.App/Composition/ServiceConfiguration.cs`, `Controls/BrainGraphView.cs`, `Views/MemoryPage.cs`, `Infrastructure/MauiAssistantTools.cs`, `ApplicationLaunchSmokeRunner.cs`, `BrainPerformanceSmokeRunner.cs`, `MauiSmokeRunner.cs`.
- `Build/Smoke/Program.cs`; `README.md`; este relatório e `laura-cognitive-evolution.md`.
- `Laura.Core.Tests/Brain/BrainAdaptiveContextTests.cs`, `BrainOperationalLearningTests.cs`, `BrainModalityTests.cs`, `BrainIncrementalTests.cs`, `BrainConcurrencyAcceptanceTests.cs`; `Conversation/ToolVerificationTests.cs`, `AudioStabilityTests.cs`.

Commits atômicos desta evolução: `f21ec76` auditoria/medição; `4f900e1` recuperação; `806b347` aprendizado; `2f68b3a` execução; `fcc06ea` modalidades; `16b112f` concorrência/UI; `853fba7` Brain incremental; `3697e5f` áudio; `4e399fb` identidades conflitantes/lançamento aprendido nativo; `c1b4064` encerramento de voz, correlação e concorrência final. O commit que contém este relatório registra o harness de replay e os resultados de aceitação. `4080246` é o commit anterior solicitado, de clima e Pensando. O log completo é reproduzível com `git log --oneline 4080246^..HEAD`.

#### Pendências e reprodução

O episódio original de timbre metálico com a exportação exata não foi reproduzido. Igualdade A/B localiza transformações/perdas, mas não comprova timbre estável no áudio do provedor, gaps inaudíveis ou comportamento do mixer/dispositivo. Permanecem os dois underruns do primeiro teste real e seis da repetição final. Distinguir espera de recepção de processamento local e repetir a sessão original são os próximos passos de diagnóstico; aumentar arbitrariamente a latência do buffer não foi tratado como correção causal. Corpus pessoal amplo, recuperação ONNX real, sessão de muitas horas e teste físico de fala continuam sem medição nesta entrega. Não foi prometido latência universal nem precisão semântica a partir de fixtures.

Para investigar o episódio, use um prompt local conscientemente selecionado (até 64 KiB) e a mesma voz. `--prompt-file` envia esse conteúdo ao Gemini; sem a flag, usa apenas o texto sintético do jardim. Gravação A/B só ocorre quando `--long-audio-smoke` é explicitamente passado. Os WAVs contêm saída do provedor, sem gravar o microfone. Comparar A/B por escuta e correlacionar timestamps `audio.chunk.arrival` / `audio.underrun` diferencia conteúdo do provedor de gaps/client. A repetição curta abaixo não restaura por si só todo o histórico/estado da sessão original.

```powershell
dotnet build Laura.sln -c Release --no-restore
dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-build --no-restore
# Referência visual/escrita: escolher uma raiz nova para cada execução.
Laura.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Laura.exe --show --storage _Output/BrainPerf/Storage --ui-smoke _Output/BrainPerf --brain-performance-smoke
# 90 s de áudio nativo junto à UI/Brain: acrescentar --brain-audio-concurrency-smoke ao comando anterior.
# Callbacks no encerramento: usar --voice-cancellation-smoke com --ui-smoke e armazenamento isolado.
# A/B com texto sintético e a voz indicada; sem --voice percorre as cinco vozes.
dotnet run --project Build/Smoke/Laura.Smoke.csproj -c Release --no-build --no-restore -- _Output/AudioReplay --gemini --key-file ../Mark-LV/config/api_keys.json --voice Aoede --long-audio-smoke
# Replay deliberado: acrescentar --prompt-file <arquivo-consentido.txt>.
# Usar provider-a.wav, playback-b.wav e long-audio.json somente depois de o processo terminar.
```

Para B físico: em armazenamento de diagnóstico, ensinar o local por texto, pedir por voz; inverter as modalidades, reiniciar e repetir. Confirmar a nota/correção, hash atual, pedido de autorização e PID antes de declarar sucesso. Para timbre: ouvir primeiro A e depois B em condições iguais; igualdade de bytes não detecta silêncio inserido pelo tempo de chegada, que fica nos contadores/timestamps. Não ativar aprendizado/execução a partir do conteúdo do prompt de diagnóstico.
