# Aquisição remota e orquestração cognitiva

## Auditoria de 2026-10-14

Base limpa no commit `7e77b1b`; 330 testes Core passaram em Release (`_Output/AutonomyBaseline/baseline.trx`). Os quatro relatórios anteriores foram consultados. Não havia processo Laura acessível por `Get-Process Laura*`; a consulta CIM foi indisponível no sandbox. Isso não comprova a versão de uma instância iniciada posteriormente.

Evidências da limitação original:

- `GeminiSkillDispatcher.DispatchAsync` envia `request.RawText` e oito mensagens anteriores a `IConversationEngine`. Não identifica REA nem interpreta URLs localmente. A escolha de operações depende do Gemini.
- `GeminiLiveClient.AskAsync` recupera Brain e descoberta progressiva de skills antes da requisição; transcrição de voz também atualiza o contexto transitório. `Session.ExecuteToolAsync` devolve o resultado com o ID original. Há limite de quatro ferramentas concorrentes, mas nenhum orçamento cumulativo para ferramentas comuns. O limite de continuações existente aplica-se somente a imagens.
- `MauiAssistantTools.Declarations` e seu switch expõem aquisição **local** (`study_source`), recuperação/passagens, propostas/revisões inertes, descoberta de skills e cliente MCP existente. `BrainSources.AcquireAsync` exige caminho absoluto local e não aceita URL. `BrainSourceSmokeRunner` depende de dez arquivos previamente preparados pelo diagnóstico, portanto não demonstra aquisição autônoma de URL.
- `BrainSkills.ProposeAsync` já exige ID/hash e evidência literal, preserva revisão e mantém draft. `Inspect` verifica ferramentas realmente registradas, fontes atuais e dependências através do runtime MCP. Dependências gerais não verificadas são intencionalmente bloqueadas. `SetEnabledAsync` e aprovações por hash continuam sob revisão nativa.
- `BrainService` oferece recuperação por passagens e grafo através do mesmo índice. Não é necessário outro cérebro, parser ou extrator de IA. `BrainLearning` aceita evidência do usuário; documentação externa não deve passar por esse mecanismo como declaração do proprietário.
- `MauiToolPermissions` é registro fechado. Os adaptadores especializados de aquisição e MCP precisam aplicar seus gates próprios. `PermissionManager.ExecuteAsync` autoriza o efeito concreto e revalida os parâmetros; Markdown e modelo não concedem permissões.
- O catálogo de skills existe, mas falta uma consulta unificada a declarações nativas, estados de memória, skills e conexões MCP. A lista ampla de declarações não equivale a disponibilidade operacional de uma skill externa.

## Aquisição GitHub

`BrainSources.AcquireGitHubAsync` usa `IRemoteBrainSource` / `GitHubBrainSource`, registrado na composição. Usa somente APIs HTTPS oficiais, sem Git, Node, Python ou CLI. URLs de repositório e `/tree/ref` são aceitas; branches com barras usam o parâmetro `reference`. A revisão é resolvida a commit antes do download.

Há dois gates distintos: inventário externo e leitura/persistência/divulgação dos arquivos exatos com commit, blobs, vault e destino do modelo congelados. Não geram autorização permanente. Repositórios privados falham com orientação explícita; autenticação privada não está implementada e nenhum token é procurado.

Transporte sem proxy, cookies ou redirecionamento automático. Cada redirect é validado antes de uma nova requisição. Só `api.github.com:443` é aceito; conexão usa endereço público resolvido e validado no callback, mantendo TLS para o host. Bytes são limitados durante streaming; até 40 requisições, 2 MiB recebidos, 20 segundos por requisição e três minutos por aquisição, incluindo aprovação. Não há retries automáticos em rate limiting.

Inventário: até 5.000 entradas/1 MiB, no máximo nove segmentos, sem traversal, symlinks/submodules, pastas ocultas/geradas/sensíveis ou arquivos acima de 64 KiB. Primeira aquisição seleciona até 12 arquivos, priorizando licença/README/manifests/documentação; expansão aceita até 24 caminhos explícitos da revisão. Até 256 KiB de texto por chamada. Respostas sempre dizem aquisição parcial, distinguindo inventário truncado, importados, reutilizados, excluídos e sugestões adicionais.

Blobs são verificados pelo SHA-1 do objeto Git e recebem SHA-256 dos bytes; Markdown retém origem imutável, licença declarada e atribuição. Conteúdo binário, UTF-8 inválido e evidência de credenciais são excluídos. Cada documento confirmado é persistido pelo `BrainService` e a coleção é atualizada incrementalmente com relações `derived_from` fundamentadas em aquisição. Cancelamento não remove documentos já persistidos. Não há criação de neurônio conceitual ou relação semântica só por similaridade; os documentos são referências para interpretação e recuperação por passagens.

Repetição na mesma revisão reutiliza os bytes retidos somente quando tamanho e SHA-256 ainda conferem. Mudanças de revisão usam identidades estáveis e histórico existente. `study_github_repository` expõe resultados estruturados ao Gemini e orienta consulta prévia, interpretação, recuperação associativa, descoberta de skills equivalentes e proposta por evidência. Não executa nem ativa habilidades.

## Validação e limites

Testes de rede usam respostas HTTP sintéticas no transporte/parser de produção e gates determinísticos reais com decisões do harness. Esses testes não são aquisição pública real, confirmação humana nem seleção real do modelo. Os resultados de aquisição pública/Gemini e reinício de processo serão registrados separadamente.

Referências do protocolo: [GitHub Git trees](https://docs.github.com/en/rest/git/trees), [Gemini Live tools](https://ai.google.dev/gemini-api/docs/live-api/tools), [mensagens Live](https://ai.google.dev/api/live). Resultados das chamadas do servidor usam `toolResponse` com o ID original. Se o servidor encerra o turno antes da avaliação, a aplicação envia `clientContent` explicitamente identificado como continuação nativa do mesmo objetivo, sem simular nova autorização ou substituir o contexto real do usuário.
`_Output/AutonomyGitHub/github-all.trx`: **364 testes passaram**, zero falhas/ignorados; build Release da solução: zero warnings/erros. Os 34 casos adicionais usam HTTP sintético; não são o teste público real.

## Catálogo, estado e continuação operacional

`list_capabilities` deriva nomes e descrições das mesmas declarações executadas pelo switch; informa memória/aprendizado desabilitados, visão indisponível no modelo, ausência de configuração/conexão MCP, catálogo de skills com aprovação/requisitos atuais e a distinção entre conhecimento e execução. Não foi criada lista de funcionalidades no prompt. O conjunto amplo de ferramentas preexistente foi mantido por compatibilidade; a redução/reconfiguração de declarações por sessão ainda não está implementada. Skills permanecem carregadas progressivamente.

`CognitiveTaskRun` mantém o objetivo real, plano opcional de 2–16 operações registradas, estados e resultados observados. Até 32 chamadas, seis minutos e duas tentativas idênticas; planejar novamente não reseta os limites. Até oito tarefas transitórias são retidas pelo adaptador; cada uma conserva no máximo 16.000 caracteres de resultados e referências atuais aos documentos. Evidência integral permanece no Brain. Uma negação/cancelamento bloqueia efeitos subsequentes no mesmo objetivo. Uma nova solicitação pode retomar conhecimento persistido, mas precisa de nova autorização concreta; nenhum token de aprovação é restaurado.

Depois de aquisição confirmada, o coordenador exige leitura de uma fonte efetivamente adquirida e descoberta de habilidades antes de registrar avaliação. `NextSourceOperation` seleciona somente operações de conhecimento realmente registradas, com IDs/hashes nativos: leitura de passagem, recuperação de conhecimento relacionado e descoberta de skills. Usa o dispatcher e os gates originais; não seleciona processo, instalação, MCP externo ou alteração de permissões. Observações distinguem `model` de `native_continuation`, portanto uma escolha do coordenador não é contabilizada como escolha do modelo.

`GeminiLiveClient.AskAsync` permite até quatro continuações de geração justificadas por estado nativo pendente, além da continuação normal do protocolo após ferramentas. A intenção original permanece no contexto transitório. O caminho de voz usa o mesmo mecanismo e recusa continuação supersedida por outra solicitação. Não foi realizado teste físico de microfone neste trabalho. O protocolo devolve sucesso/falha/bloqueio/cancelamento estruturados; concluir operações não equivale a validar o objetivo inteiro.

`propose_brain_skill` publica um esquema de objeto completo; os antigos chamadores com JSON em string continuam aceitos. Erros de formato/categoria/evidência retornam orientações verificáveis de correção. Uma dependência indisponível bloqueia ativação, não a criação de draft. `assess_source_learning` exige versão/evidência literal de fonte realmente lida e descoberta real de skills; permite concluir honestamente uma avaliação de conhecimento sem fabricar habilidade. Propostas continuam em `BrainSkills.ProposeAsync` e revisão por hash.

Arquivos remotos são referências, não declarações do proprietário. Wikilinks dentro de fontes importadas não criam relações com o grafo autorado do proprietário. As relações da coleção descrevem aquisição; as de propostas descrevem sua proveniência. Similaridade não cria relações factuais. Os bytes exatos são preservados em `Assets/Sources/<sha256>.source` pelo armazenamento de anexos existente, enquanto a referência Markdown é indexada por passagens; isso mantém CRLF/espaços finais verificáveis e evita downloads repetidos após reinício. Manifestos de lotes anteriores na mesma revisão continuam contribuindo às dependências declaradas.

Evidências de código: [aquisição e gates](../../Laura.Core/Brain/BrainSources.Remote.cs), [estado e seleção limitada](../../Laura.Core/Engine/CognitiveTaskRun.cs), [catálogo/despacho/esquema](../../Laura.App/Infrastructure/MauiAssistantTools.cs), [continuação textual e de voz](../../Laura.Platform.Windows/Conversation/GeminiLiveClient.cs), [propostas e requisitos](../../Laura.Core/Brain/BrainSkillProposals.cs).

Suíte integrada: **376 testes passaram**, zero falhas/ignorados (`_Output/AutonomyFinalRegression/final-regression.trx`). Builds Debug e Release passaram sem warnings/erros. Auditoria de privacidade confirmou que os vaults de diagnóstico são ignorados e não aparecem no índice/histórico Git. Resultados públicos e do modelo real abaixo são independentes dessa suíte.

## Demonstração final: solicitação original e processo reiniciado

Entrada real enviada ao Gemini Live: **“Laura, estude e aprenda essa habilidade: https://github.com/morluto/rea”**. Sem microfone, áudio em volume zero, armazenamento isolado e credencial protegida existente usada somente em processo. O harness invocou os controles nativos de aprovação para os escopos públicos concretos; isso verifica a UI de aprovação, não um clique físico feito pelo proprietário.

Evidências: [resultado](../../_Output/AutonomyLiveFinal/result.txt), [estado/observações](../../_Output/AutonomyLiveFinal/live-task.json), [métricas](../../_Output/AutonomyLiveFinal/live-metrics.json), [conversa](../../_Output/AutonomyLiveFinal/live-answer.txt), [recuperação em outro processo](../../_Output/AutonomyLiveFinal/github-restart-result.txt).

| Observação | Resultado confirmado |
| --- | --- |
| Revisão/atribuição | `morluto/rea` em `f75b1a416ba67b8421e7dbd248160afedb79581f`, licença declarada MIT, bytes Git e SHA-256 verificados |
| Cobertura | 3.774 arquivos ordinários inventariados; 11 referências adquiridas, cobertura parcial |
| Rede/latência | 15 requisições; 1.187.051 bytes recebidos; aquisição 15.201 ms; tarefa observada 75.420 ms |
| Escolhas reais do modelo | `study_github_repository`, `read_brain_passage`, duas tentativas de `propose_brain_skill` |
| Coordenação nativa | `recall_memory`, `list_brain_skills`; uma continuação de geração; seis chamadas totais de ferramentas |
| Leitura comprovada | Uma fonte expandida explicitamente: README, até 4.000 caracteres; a recuperação forneceu passagens relacionadas. Não se afirma que os 11 arquivos foram integralmente interpretados pelo modelo |
| Validação de proposta | Primeira tentativa recusada por declarar ferramentas/dependências como guidance; o modelo corrigiu o tipo para MCP dentro do mesmo objetivo |
| Draft real | `Skills/003088BDA83DD4C7/SKILL.md`, ID `c467efd1-60a7-44d8-ac6f-5b89f988b478`, duas fontes atuais com citações literais: README e package.json |
| Disponibilidade | `Approved=false`, `Ready=false`, estado blocked: dependências/pré-requisitos e runtime externo não verificados. Não equivale a comprovar ausência de Node.js no computador |
| Persistência | Processo Release 11932 encerrou; processo Debug 18068 reconstruiu índice e recuperou 13 IDs/caminhos/hashes exatos, incluindo draft. Não repetiu aquisição ou autorização |

Arquivos efetivamente **adquiridos e persistidos** nessa conversa:

- `LICENSE`, `README.md`, `package.json`, `THIRD_PARTY_NOTICES.md`
- `website/public/examples/notes-electron/package.json`
- `docs/adb-device-analysis.md`, `docs/android-analysis.md`, `docs/apktool-resource-analysis.md`
- `src/main.ts`, `src/adb/AdbConfiguration.test.ts`, `src/adb/AdbConfiguration.ts`

São referências recuperáveis sobre o framework, integração MCP, dependências declaradas e documentação Android/ADB; permanecem fontes externas não verificadas. A coleção contém 11 relações `derived_from` de aquisição; a proposta contém duas relações de proveniência para fontes. Nenhuma associação factual com conhecimento do proprietário foi inventada. O texto do draft é gerado pelo modelo e requer revisão de conteúdo/escopo, além da resolução dos requisitos. Nenhum código, script, instalação ou servidor REA foi executado, e não se declara habilidade operacional de engenharia reversa.

O GitHub limitou a última leitura desse lote. A falha parcial contém reset `2026-10-14T12:09:42Z` (**09:09:42 de 2026-10-14, America/Sao_Paulo**); os 11 documentos anteriores foram conservados. A repetição `_Output/AutonomyPublicFinal` foi bloqueada nesse mesmo limite, sem contornar autenticação ou procurar tokens. Esse horário registra o evento observado, não um bloqueio permanente.

Generalização pública anterior: [resultado de dois repositórios](../../_Output/AutonomyPublic/result.txt) confirmou REA e **dotnet/ClangSharp**, de estrutura C# diferente. ClangSharp em `0c1c4098a7aab7cb392a2367b75d447bbac05d58`, licença MIT: 12 documentos, 3.078 arquivos inventariados, 15 requisições, 1.159.882 bytes e 16.024 ms. O primeiro reinício público recuperou 27 versões exatas. A proposta produzida por esse harness é integração direta de adaptadores e está separada da proposta efetivamente criada pelo Gemini na demonstração final.

Regressão final: **377 testes passaram**, zero falhas/ignorados (`_Output/AutonomyAcceptance/acceptance-all.trx`), incluindo cancelamento imediatamente após o primeiro arquivo remoto realmente persistido e recuperação desse arquivo em um serviço com índice novo. Base anterior: 330 testes. Não se estima taxa universal de seleção/conclusão a partir desses poucos ensaios; houve recusas de schema/evidência e uma conversa sem resposta final nos builds intermediários, preservados nos diretórios de diagnóstico. O ensaio final confirmou a sequência acima.

## Limites restantes e reprodução

- Somente repositórios públicos github.com; autenticação privada e GitHub Enterprise não implementados.
- Inventário acima de 1 MiB/5.000 entradas é limitado ou recusado antes de importar. Travessia remota paginada de subárvores e análise integral de repositórios maiores não implementadas.
- Não há extração conceitual independente do Gemini nem criação automática de relações semânticas factuais. Recuperação pode ser lexical quando ONNX não está configurado.
- Dependências gerais permanecem **não verificadas**; somente conexões MCP configuradas têm verificador operacional existente. Instalação, ativação e execução são ações separadas.
- O estado de tarefa é transitório; restart conserva fontes, versões e drafts, mas não reexecuta nem restaura aprovação pendente. A evidência atual de voz é código/contrato compartilhado, não conversa física com microfone.
- Não foram medidos consumo de RAM/contexto em escala, precisão semântica em corpus pessoal nem taxa de conclusão generalizada. Contadores de rede, arquivos, leituras, ferramentas, continuações e latência estão nos artefatos.

Os processos normais LAURA estavam ausentes na inspeção CIM autorizada; as instâncias verificadas foram explicitamente identificadas por caminho/PID/argumentos e usaram armazenamento isolado. Debug e Release atuais foram compilados, e o reinício final foi feito com Debug. Não se presume que uma instância aberta no futuro seja automaticamente atualizada sem reiniciar.

```powershell
dotnet build Laura.sln -c Debug
dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release
# Usar uma raiz nova; todos os arquivos de diagnóstico permanecem locais/ignorados.
Laura.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage <root> --brain-smoke <root> --brain-github-smoke
# Opt-in: somente texto sintético/fontes públicas, usando a credencial protegida já configurada.
Laura.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage <root> --brain-smoke <root> --brain-github-smoke --brain-live-smoke
# Depois do processo anterior encerrar; não realiza nova aquisição.
Laura.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage <root> --brain-smoke <root> --brain-github-smoke --github-restart
```

Commits funcionais: `1134661` (aquisição remota) e `641e7ad` (catálogo, estado, continuação, recuperação e proposta integrada). O commit subsequente registra o harness público/Gemini e as evidências finais. Nenhum push/PR, instalação ou alteração no Brain pessoal foi realizado.
