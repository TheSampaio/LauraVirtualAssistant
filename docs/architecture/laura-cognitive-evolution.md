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
