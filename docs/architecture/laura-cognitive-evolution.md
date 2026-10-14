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

## Fase 4 — experiências e reflexão, primeira unidade

`run_program` produz `BrainExecutionObservation` somente após observar o processo terminar. Código de saída, nome do executável, horário, ID da observação e fingerprint da operação vêm do adaptador real, não de texto do modelo. `BrainExperiences` agrupa tentativas por objetivo em memória episódica, conserva erros/tentativas subsequentes e revisões, sem concluir que exit code zero verifica o objetivo inteiro.

Não são persistidos argumentos, stdout/stderr, tokens, imagens ou caminhos do processo nesses episódios. Objetivos sensíveis/longos e aprendizado desabilitado são excluídos. O resultado da ação continua independente de falha de aprendizado; recibos pendentes e erro de armazenamento são reportados separadamente. O histórico SQLite passa ao esquema 3 em transação; até 200 recibos selecionados (16.000 caracteres serializados cada) permanecem até a escrita Markdown. Não há descarte silencioso quando a capacidade acaba. Reinício consolida conhecimento e nunca reexecuta o comando. Limpar histórico também limpa recibos pendentes; experiências já consolidadas são conhecimento separado.

`reflect_experience` exige ID/hash atuais e evidência literal de um episódio. Produz hipótese draft/unverified em `Inbox/Reflections`, com relação `learned_from`. Há no máximo uma proposta por versão da fonte; uma hipótese não pode refletir a si própria. Ela não ativa skill, instala dependência ou altera autorização. Essa unidade não implementa reflexão contínua nem geração automática de conclusões causais.

Validação: **274 testes passaram**, `_Output/CognitivePhase4/phase4-experiences.trx`; build Release zero warnings/erros. Quatro novos casos verificam tentativa com erro e outra saída zero, idempotência/reinício, isolamento de vault, privacidade/configuração, corrupção isolada e reflexões. Integração real em `_Output/CognitiveNativeExperiences/result.txt` passou: `dotnet --version` aprovado com checkbox nativo, saída observada, repetição negada sem observação e reflexão mantida em revisão. O ensaio não fez instalação nem executou código recuperado do cérebro.

Limites: essa primeira unidade observa processos concluídos por `run_program`, não todas as ferramentas ou sucesso visual da tarefa. Existe uma fronteira não atômica entre efeito no SO e retenção do recibo; queda antes da retenção pode perder essa observação. Markdown é editável e seus rótulos de evidência não são um log criptográfico nem autoridade de execução. Reutilização de aplicativos aprendidos é a próxima unidade.

### Reutilização de aplicativos observados

`ProcessLaunchResult` distingue aceitação do shell e criação real de processo. O executor Windows retorna PID e hash de executáveis de até 64 MiB. `BrainApplications` aprende apenas criação observada, conserva uma referência sem argumentos, agrega aliases para o mesmo alvo e valida arquivo/hash antes de reutilizar. O caminho fica em metadados YAML `application`, fora do corpo/indexação automática do prompt. Código de descoberta e comandos locais existentes usam a mesma referência; qualquer nova execução continua passando pelo `PermissionManager`.

Executáveis aprendidos têm hash vinculado aos parâmetros de aprovação e recalculado no callback real, mantendo um handle de leitura que impede escrita/remoção durante a verificação/partida. Alteração, rede, redirecionamento, armazenamento protegido, referências ambíguas e adapters sem suporte a hash não recebem execução implícita. Uma negação explícita ou falha de pinning interrompe fallback; ausência real do programa principal preserva o fallback conhecido. Falha de escrita da referência é separada do resultado nativo e registrada sem conteúdo privado.

Validação: **280 testes passaram**, `_Output/CognitivePhase4/phase4-final.trx`; build Release zero warnings/erros. Quatro testes verificam referências/reinício/deduplicação, bytes alterados, aceitação sem PID, aliases/escopos/rede/configuração; dois verificam fallback negado versus programa ausente. Integração real passou em `_Output/CognitiveNativeApplications/result.txt`: um host do SDK foi copiado somente para Workspace de diagnóstico, houve criação real de processo, `open_app` reutilizou o alvo sem atualizar inventário e exigiu nova aprovação. Bytes foram alterados mantendo tamanho/data durante uma aprovação pendente; o callback recusou o hash e não criou processo. Não foi alterado o runtime instalado.

Limites: referência observa criação, não janela pronta nem tarefa concluída. Shortcuts/protocolos, aceitação sem PID e executáveis maiores que 64 MiB não são promovidos a referência verificada; descoberta permanece disponível. Não há autoaprovação de programas conhecidos ou armazenamento de argumentos. O cérebro continua fornecendo uma pista revalidada, não uma autoridade de segurança.

## Fase 5 — aquisição local de fontes

`BrainSources` e `study_source` adquirem um arquivo local ou escopo estreito de repositório após aprovação do inventário real. A leitura ocorre dentro do callback autorizado, com recursos congelados e revalidação. MD/TXT/JSON, projetos C# e formatos comuns de código são armazenados como referências externas/unverified; frontmatter de origem não vira política, skill ativa ou metadado de autoridade. Reimportação usa chaves por origem e mantém IDs/revisões; o projeto de estudo se relaciona às referências efetivamente importadas.

O inventário exclui diretórios de credenciais, Git/cache/build, arquivos ocultos, nomes de credenciais e redirects. Limites: 64 arquivos, profundidade oito, 5.000 entradas examinadas, 64 KiB por arquivo, 256 KiB de texto por aquisição e 128 nomes de dependências. Leitura usa UTF-8 estrito e stream limitado, rejeita bytes nulos e linhas com valores de credenciais. Parsing JSON é limitado; XML proíbe DTD e resolver externo. `package.json` e `PackageReference` fornecem nomes de dependências declaradas, jamais confirmação de instalação/disponibilidade.

O retorno informa quantidade importada/excluída, linguagens de origem e dependências declaradas. Código, `postinstall`, programas e instalações não são executados para estudar fontes. A referência é recuperável pelo mesmo índice granular, sem um segundo cérebro paralelo.

Validação: **283 testes passaram**, `_Output/CognitivePhase5/phase5-all.trx`; build Release zero warnings/erros. Três novos testes verificam estudo TypeScript sem execução/instalação, fontes C#/XML/JSON, fontes sensíveis/malformadas, deduplicação, recuperação, negação/cancelamento e aprovação tardia. Integração real em `_Output/CognitiveNativeSources/result.txt` passou pelo adaptador `study_source` e checkbox nativo, com TypeScript recuperado e `postinstall` não executado. A validação usou fontes sintéticas, sem estudar nem transmitir repositórios pessoais.

Limites: esta unidade cobre arquivos locais; URLs/HTTP, PDF e documentos binários não são readers implementados. Repositórios grandes exigem seleção de subpastas e aquisições incrementais. O detector conservador de credenciais não é um DLP universal e pode excluir exemplos legítimos. Dependências são apenas declaradas, não instaladas/testadas. As próximas fases incluem proposta/evolução de skills, MCP, consolidação, observabilidade/UI e validação integrada final; não são consideradas concluídas por esta entrega incremental.

## Registro de commits e reprodução

### Fonte real indicada: REA

O cenário público usa [morluto/rea](https://github.com/morluto/rea), revisão fixa `c7105d9723c835b5e0122f5fc6cfbea86265c349`. `Build/Smoke/Get-ReaSourceFixture.ps1` baixa dez arquivos textuais específicos (README, manifesto npm, quatro documentos e quatro fontes TypeScript), confere hashes SHA-256 fixados e preserva a licença no fixture ignorado pelo Git. Não clona submódulos, executa scripts nem instala Node/npm/REA. Isso valida um recorte de **156.391 bytes**, não o repositório inteiro.

`BrainSourceSmokeRunner` amplia o diagnóstico nativo com `--brain-rea-smoke`. O adaptador real `study_source` importou **10/10**, excluiu zero, identificou TypeScript e dependências apenas declaradas, exigiu confirmação crítica nas duas aquisições e preservou IDs/hashes na repetição. As instruções de instalação permaneceram referências `unverified`; não apareceram skills ou concessões de permissões.

Quatro consultas substantivas recuperaram os documentos de política de startup MCP, inspeção managed/ILSpy, orçamento de transporte MCP e identidade do servidor nas posições **1, 1, 3 e 1**. A informação esperada apareceu diretamente no excerpt em **1/4**; a expansão contextual do mesmo documento, com ID/hash e até **4.000 caracteres**, recuperou as quatro evidências. Os dois primeiros ensaios falharam ao procurar a evidência apenas a partir do início do trecho escolhido: o contexto relevante podia precedê-lo. O diagnóstico final explicita a leitura de até 600 caracteres anteriores e mantém os resultados iniciais como evidência dessa limitação; não se afirma precisão perfeita dos excerpts nem compreensão pelo modelo.

Um novo `BrainService`, com índice lexical vazio, reabriu o Markdown, preservou as dez identidades e recuperou a política de startup. Não foi um reinício completo do Windows/processo. O inventário/bytes das fontes e as concessões de permissões ficaram inalterados. Evidências finais: `_Output/CognitiveNativeReaVerified/result.txt` e `rea-checks.txt` (PASS); **283 testes** em `_Output/CognitiveRea/rea-source-regression.trx`; build Release sem avisos/erros. Nenhum servidor MCP real REA foi iniciado e o teste não avalia qualidade neural ou resposta semântica de um modelo.

```powershell
# Usar uma raiz de diagnóstico nova. Somente esta preparação acessa HTTP.
./Build/Smoke/Get-ReaSourceFixture.ps1 -Destination "$PWD/_Output/ReaCheck/Workspace/ReaSourceFixture"
Laura.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage "$PWD/_Output/ReaCheck" --brain-smoke "$PWD/_Output/ReaCheck" --brain-rea-smoke
```

A preparação HTTP é uma ferramenta explícita de validação, não um leitor de URL implementado no produto. A aquisição da LAURA permanece local e autorizada; os dados públicos ficam somente no Brain isolado do diagnóstico. O conhecimento da documentação do REA não é apresentado como capacidade de executá-lo.

| Commit | Unidade testada |
|---|---|
| `efa7f32` | Base de permissões e captura integrada |
| `474e94b` | Auditoria cognitiva e referência |
| `92528d1` | Aprendizado contextual e fila observável |
| `c1958ea` | Recibos duráveis e retomada |
| `cb9fa0d` | Passagens e expansão de versão exata |
| `bf39162` | Grafo limitado e proveniência |
| `813a9f2` | Experiências observadas e reflexões draft |
| `6d603d3` | Aplicativos aprendidos e hash no momento da execução |
| `c5bab8f` | Aquisição local autorizada, sem executar código |

O diagnóstico público REA é registrado no commit subsequente desta documentação. Artefatos `_Output` são ignorados pelo Git e não contêm a credencial protegida.

```powershell
dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-restore
dotnet build Laura.sln -c Release --no-restore
# Raiz nova e isolada; o diagnóstico cria somente dados/fontes sintéticos.
Laura.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage D:/Temp/LauraBrainCheck --brain-smoke D:/Temp/LauraBrainCheck
```

`--brain-live-smoke` é opt-in para verificar o modelo real com mensagens sintéticas; usa a credencial existente somente em processo. O diagnóstico altera apenas seus fixtures e copia um host do SDK para testar alteração de bytes. Mouse/voz completa, múltiplos monitores e servidor MCP conectado continuam verificações separadas. Ausência de servidor não é apresentada como integração MCP verificada.
