# Evolução cognitiva da LAURA

## Fase 0 — auditoria de 2026-10-13

Este trabalho evolui o cérebro existente. Markdown permanece a fonte de verdade; SQLite/FTS5 e vetores são índices descartáveis. As políticas de execução ficam no `PermissionManager`, fora do vault e fora do modelo. A base de permissões e captura está descrita no [relatório de integração](laura-permissions-screenshot-delivery.md), incluindo verificações manuais pendentes.

### Fluxo atual e componentes reutilizados

| Caminho | Responsabilidade existente | Limitação encontrada |
|---|---|---|
| `AssistantEngine` / `GeminiSkillDispatcher` / `GeminiLiveClient` | fila de comandos, conversa, ferramentas, voz e contexto recuperado | provedor de conversa implementado é Gemini; não há extrator cognitivo independente |
| `BrainConversationMemory` / `SqliteConversationMemory` | histórico limitado, migração de fatos antigos, última declaração do usuário como contexto transitório | métodos de compatibilidade síncronos; contexto de voz publicado somente ao concluir o turno |
| `BrainLearning` | declarações conservadoras, chaves estáveis, correção explícita com revisão, contradição em Inbox | gramática restrita; `DropWrite` pode perder eventos; encerramento cancela antes de drenar; falhas não retornam ao chamador |
| `BrainService` / `FileBrainStorage` / `BrainMarkdown` | identidade, YAML preservado, hash otimista, revisões, watcher, relações, migração e anexos | gravações precisam continuar usando essas proteções; nenhuma memória pode ativar ferramenta |
| `BrainIndex` / `OnnxBrainEmbedding` | FTS5 e vetores locais com cache por hash/modelo | índice por nota inteira; ONNX lê até 126 tokens; excerpt procura frase inteira, perdendo passagens tardias |
| `BrainGraphTopology` / grafo em `MemoryPage` | topologia baseada em relações reais e links resolvidos | recuperação expande um único salto sem orçamento específico; proveniência da relação não integra `BrainEdge` |
| `BrainSkills` | importação inerte, aprovação externa por `id:hash`, catálogo e leitura progressiva | descoberta lexical; sem propostas, composição ou validação de dependências; catálogo limitado pode omitir habilidade relevante |
| `WindowsApplicationCatalog` / `WindowsProcessLauncher` | inventário real de atalhos/registro, alvos congelados, execução autorizada | inventário só em memória; não há experiência operacional estruturada/reutilização persistente verificada |
| `MauiAssistantTools` / `MauiToolPermissions` | ferramentas estruturadas e registro fechado de efeitos | resultados de texto não equivalem a evidência de execução; não há protocolo/client MCP existente |

MAUI/WinUI, serviços singleton e padrões visuais existentes são mantidos. Markdig, SQLite e ONNX já são dependências do Core. Não é necessária uma segunda infraestrutura de armazenamento nem um novo framework para as fases iniciais.

### Invariantes e regressões a evitar

- Não aprender respostas geradas como fatos do usuário. Uma proposta do modelo precisa ser atribuída à declaração real e permanecer conhecimento, sem efeitos executáveis.
- Repetição não aumenta confiança. Correções precisam preservar identidade, revisão anterior e fontes; declarações incompatíveis não devem escolher silenciosamente uma versão.
- `RememberConversations` e `Brain.AutoLearn` continuam controlando aprendizado e contexto. Dados sensíveis não entram na captura automática.
- Skills externas continuam inertes; alterar seus bytes invalida aprovação. Conhecimento, dependências sugeridas e Markdown não alteram permissões.
- Reutilizar um caminho aprendido exige revalidar existência/identidade e passar novamente pela camada de execução. Não executar código de fontes adquiridas para estudá-las.
- Pastas novas usam segmentos com inicial maiúscula. IDs, links, aliases, metadados desconhecidos e comportamento em filesystem sensível a caixa devem ser preservados.
- Testes usam vaults e mensagens sintéticos, sem alterar o cérebro pessoal nem transmitir capturas/documentos privados.

### Referência e plano incremental

A referência anterior contém 243 testes Core aprovados e build Release sem warnings/erros. Evidências nativas da base: `_Output/PermissionsAcceptance`, `_Output/PermissionsLiveFinal`, `_Output/LauraUiAcceptance` e `_Output/ScreenshotStableFinal`. Uma nova execução de referência é salva em `_Output/CognitiveBaseline`.

Ordem: (1) aprendizado contextual e confiabilidade; (2) recuperação granular com testes comparáveis; (3) expansão de grafo limitada; (4) experiências e reflexão verificável; (5) aquisição; (6) propostas e evolução de skills; (7) MCP; (8) consolidação; (9) observabilidade/interface; (10) validação integrada. Cada unidade coerente recebe testes e commit próprio. Fases futuras não são consideradas implementadas por este plano.

O ambiente não comprova gestos reais de captura, múltiplos monitores/DPI físico, leitura por Narrator ou áudio audível. Não há servidor MCP configurado a partir do qual validar integração, nem repositório externo específico fornecido para aquisição. Essas limitações não impedem os testes locais de memória e recuperação.

## Fase 1 — aprendizado contextual e fila

`BrainLearning` mantém o caminho conservador de declarações simples. `learn_memory` permite ao modelo de conversa já configurado interpretar declarações naturais sem uma segunda requisição de extração. O Core valida categoria fechada, tamanho, privacidade e citações literais presentes no contexto transitório real. O adaptador congela esse contexto antes de aguardar outras ferramentas; texto tipado e transcrição de voz atualizam o mesmo `IConversationMemory`. Não recebe caminhos arbitrários, regras de segurança, scripts ou aprovação de skills.

Preferências conhecidas reutilizam `owner.editor`, `owner.language` e `owner.style`; outros assuntos recebem chave derivada da categoria e tópico efetivamente citado. Propostas classificam novidade, repetição, complemento, correção, contradição, contexto temporário, obsolescência e evidência não verificada. Correção/complemento exigem o ID real do alvo no mesmo escopo. Repetição não altera revisão nem confiança. Complementos preservam texto anterior; correções usam as revisões existentes. Declarações incompatíveis e sugestões de obsolescência ficam em Inbox para revisão, sem excluir conhecimento.

O novo campo YAML opcional `verification` distingue `user_reported` e `unverified`; arquivos antigos usam `unspecified`. Recuperação apresenta essa informação junto de origem, categoria e confiança. Uma interpretação de fala não é verificação independente de um fato externo.

A fila possui 64 posições, retorna aceitação/exclusão/saturação/encerramento e oferece `ObserveAsync` para aguardar capacidade com cancelamento. `FlushAsync` permite aguardar a conclusão dos eventos anteriores. Contadores não registram conteúdo. Falhas recuperáveis têm até três tentativas; falhas permanentes não encerram o worker. Shutdown fecha a entrada e concede cinco segundos para drenar antes de cancelar. Gravações de conhecimento são serializadas e continuam usando hashes/revisões do vault.

### Validação e limites

- Referência: 243 testes. Fase 1: **254 passaram, zero falhas/ignorados**, relatório `_Output/CognitivePhase1/phase1-all.trx`.
- Três testes exercitam fila cheia real, espera/cancelamento, falha transitória, falha permanente e shutdown. O primeiro ensaio de testes sem escalonamento abortou na conexão VSTest/testhost; a execução com ambiente Windows disponível passou.
- Oito casos adicionais exercitam interpretação contextual, citações inventadas, dados sensíveis/políticas, correção, complementos, contradição, obsolescência, contexto temporário, configurações e reinício com índice independente.
- Build Release: zero warnings/erros. Diagnóstico `--brain-smoke <raiz> --storage <mesma-raiz>` verifica o adaptador real sem nuvem. `--brain-live-smoke` habilita somente mensagens sintéticas ao modelo configurado e referência à credencial protegida dentro do processo, sem copiá-la ou imprimir erros remotos.
- O diagnóstico real passou em `_Output/CognitiveLivePhase1Verified/result.txt`: adaptador recusou evidência de outro contexto; Gemini interpretou preferência natural em português, corrigiu o ID real com revisão preservada e recuperou `Reviewed Editor` em um terceiro turno. Apenas dados sintéticos foram enviados; nenhum microfone foi iniciado. Isso comprova os cenários executados, não generalização para toda linguagem natural.
- Chamadas diretas a `Observe` usam fila volátil. Mensagens da aplicação usam os recibos duráveis descritos abaixo; propostas explícitas retornam somente depois da gravação.
- A interpretação depende da disponibilidade/comportamento do modelo; o Core recusa propostas sem evidência, mas não comprova por conta própria o significado de toda frase. Fontes externas e falas da assistente não são usadas como declaração do usuário. Experiências operacionais estruturadas, extração de documentos e evolução de skills ainda são fases posteriores.

### Recuperação durável de eventos selecionados

O esquema SQLite do histórico evolui de 1 para 2 em transação, preservando fatos e mensagens. `learning_events` contém somente ID da mensagem e hash da identidade do vault. Texto não é duplicado. `BrainConversationMemory` cria o recibo na mesma transação da mensagem quando aprendizado/configuração/privacidade permitem. Histórico anterior à migração não recebe recibos retroativos, e texto da assistente não é uma fonte aceita.

O alimentador coloca recibos pendentes na fila existente, com backpressure e deduplicação por recibo. Falhas permanecem pendentes para reinício, sem ciclo infinito de retry. Só o vault correspondente recupera seus eventos. A consolidação é idempotente se o processo interromper entre Markdown e acknowledgment; gravações revalidam o vault sob o gate real. Limpar histórico remove recibos, e a retenção de 2.000 mensagens remove eventos expirados. Não há retenção secreta ilimitada nem replay de todo o transcript.

Validação: **260 testes passaram**, `_Output/CognitivePhase1/phase1-recovery.trx`; build Release zero warnings/erros. Os seis novos casos verificam recuperação, isolamento de vault/histórico, interrupção antes do acknowledgment, falha preservada, remoção/privacidade, migração v1 e retenção. A interrupção é simulada na fronteira entre escrita e acknowledgment; não foi injetada falha elétrica física. Retenção de histórico continua sendo limite explícito de recuperação. A fila direta sem recibo não oferece recuperação após crash.

## Fase 2 — recuperação por passagens

`BrainPassages` usa o Markdig já instalado para identificar seções reais (cabeçalhos dentro de código não viram seções). Produz até 512 spans por documento, com sobreposição pequena, fronteiras UTF-16 válidas, contexto de cabeçalhos e cobertura do final. Spans são derivados; não criam nós nem relações e não alteram Markdown. Se um documento excede o orçamento, a passagem final cobre o restante e a seleção de excerpt continua limitada a 800 caracteres.

O mesmo `BrainIndex` passa ao esquema derivado 2: FTS5 e vetores por passagem, hash/modelo para cache e invalidação de dados removidos. Índices anteriores são reconstruídos a partir do vault. A consulta limita 4.096 passagens lexicais, deduplica até 80 candidatos de notas e combina até 60 candidatos semânticos locais. A segunda etapa preserva os pesos configurados de conteúdo/grafo/tempo/confiança e seleciona excerpt no span correspondente. A busca usa o gate do serviço para ler snapshot e índice coerentes. Nenhum vetor sozinho cria relação factual.

`BrainHit` expõe localização; `recall_memory` apresenta seção, offset e hash. `read_brain_passage` expande até 4.000 caracteres de uma versão atual e recusa hash antigo, skill, nota arquivada, ID conflitante ou bounds inválidos. Consultas têm limite de 4.000 caracteres; contexto mantém seu orçamento configurado. Recuperação vazia, fallback lexical e memórias antigas permanecem suportados.

### Evidência e decisões de ranking

- **265 testes passaram**, `_Output/CognitivePhase2/phase2-all.trx`; build Release zero warnings/erros.
- Benchmark reproduzível: cinco documentos Markdown sintéticos, cinco perguntas EN/PT, cinquenta consultas lexicais aquecidas. A seleção antiga de excerpt por frase inteira recuperou 0/5 respostas tardias; a seleção por passagem recuperou 5/5, com documento esperado em primeiro lugar em 5/5. p50/p95: **1,22/1,45 ms** nesta máquina, `_Output/CognitivePhase2/passage-benchmark.trx`.
- Essa comparação isola seleção de excerpt: não afirma melhora universal do ranking nem precisão semântica em corpus de produção. Um double de embedding com janela curta verifica contrato/caching/cobertura tardia, não qualidade linguística de ONNX. Nesta execução não foi fornecido modelo ONNX real.
- Integração MAUI real passou em `_Output/CognitiveNativePhase2/result.txt`: aprendizado com recibo durável e expansão de trecho tardio através dos adaptadores de produção, sem nuvem.
- RRF foi considerado, mas a melhora demonstrada aqui depende da granularidade. Sem corpus semântico representativo não existe evidência para substituir os pesos já configuráveis; esse benchmark não é apresentado como comparação entre RRF e fusão ponderada.

APIs consultadas: [SQLite FTS5](https://www.sqlite.org/fts5.html) para MATCH/BM25/colunas não indexadas e [Markdig](https://github.com/xoofx/markdig) para parsing Markdown. Não foi introduzido pacote. Limites: até 512 spans por nota; passagem final grande tem janela semântica parcial conforme o modelo; SQLite/ONNX em andamento não são interrompidos no meio de uma chamada nativa. A UI aguarda de forma assíncrona. Testes de corpus real, escalas grandes e modelo ONNX instalado continuam pendentes.

## Fase 3 — ativação associativa limitada

`BrainGraphActivation` prepara vizinhanças com até 32 relações elegíveis por nó durante a publicação do snapshot. A consulta seleciona até oito sementes reais, usa prioridade por força, atenua saltos posteriores e preserva cada aresta, sua direção e proveniência. Busca reversa é identificada e recebe peso menor. Ciclos e relações duplicadas não elevam confiança nem criam conhecimento novo.

O orçamento padrão é dois saltos, 160 candidatos e 30 ms de travessia; o limite adicional é 4.096 arestas visitadas. Os valores `RetrievalGraphDepth`, `RetrievalGraphCandidates` e `RetrievalGraphMilliseconds` são persistidos no INI externo ao vault e sanitizados (profundidade 0–4, candidatos 8–500, tempo 5–250 ms). Zero saltos desativa expansão. Essas configurações não modificam permissões. O orçamento cobre travessia, não construção do índice nem chamadas nativas de ONNX/SQLite.

Relações desconhecidas, `contradicts`, nós draft/arquivados/skills, identidades conflitantes e pesos inválidos são excluídos. `BrainEdge.Provenance` mantém a origem de `BrainRelation.Source` e identifica wikilinks autorados. O resultado de recuperação expõe o caminho associado, explicando que ele não estabelece um novo fato. Esta unidade não gera automaticamente relações semânticas gerais a partir de similaridade; interpretações adicionais devem ter evidência e revisão adequadas.

Validação: **270 testes passaram**, `_Output/CognitivePhase3/phase3-all.trx`; build Release zero warnings/erros. Os cinco novos testes verificam dois saltos na recuperação real, preservação de hashes/arestas, direção reversa, ciclos, candidatos, cancelamento e exclusões. O teste de INI existente também verifica persistência dos três limites. Não é um benchmark de grafo de milhões de nós nem uma garantia de deadline rígido de sistema operacional.
