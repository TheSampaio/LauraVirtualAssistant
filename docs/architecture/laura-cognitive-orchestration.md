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
