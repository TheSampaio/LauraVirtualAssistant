# Brain, concorrência e estabilidade do áudio — 2026-10-14

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
