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

Referências do protocolo: [GitHub Git trees](https://docs.github.com/en/rest/git/trees), [Gemini Live tools](https://ai.google.dev/gemini-api/docs/live-api/tools), [mensagens Live](https://ai.google.dev/api/live). Resultados de ferramentas usam `toolResponse`; a continuação de geração pelo servidor não exige fabricar uma nova mensagem do usuário.
`_Output/AutonomyGitHub/github-all.trx`: **364 testes passaram**, zero falhas/ignorados; build Release da solução: zero warnings/erros. Os 34 casos adicionais usam HTTP sintético; não são o teste público real.
