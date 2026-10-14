# LAURA Permissions e Screenshot — relatório técnico

Data: 13/10/2026. Implementação no repositório atual, sem novos pacotes, frameworks ou aplicativos externos.

## Estado da entrega

Permissions foi implementado e passou pelo gate documentado antes de começar Screenshot: compilação limpa, 202 testes na ocasião, integração nativa aberta/oculta/minimizada, anúncios Gemini e regressão MAUI. A suíte final ampliada passou com **243 testes, zero falhas e zero ignorados**. As integrações nativas de Screenshot, visão real e recuperação do cérebro passaram nos cenários disponíveis.

**A aceitação integral ainda depende das verificações manuais listadas abaixo.** Não foi testado o gesto real de seleção por mouse, o disparo físico dos atalhos ou uma configuração física com vários monitores/DPI. Não apresento esses critérios como validados. Nos fluxos efetivamente testados, não permanece uma falha crítica conhecida.

## 1. Funcionalidades implementadas

Permissions reúne políticas determinísticas, solicitações assíncronas, decisões únicas, permissões temporárias/persistentes limitadas, revogação, histórico com retenção, notificações da bandeja, anúncios de voz e um Centro de Aprovações independente. Alertas, confirmações e perguntas comuns usam os diálogos da LAURA. As configurações oferecem Solicitar Aprovação, Aprovação Automática e as três opções de anúncio por voz.

Screenshot oferece região ajustável, seleção de janela, desktop/monitor, atalhos configuráveis, editor independente, formas/setas/linhas/texto/desenho/marca-texto, seleção/movimento/redimensionamento/remoção e desfazer/refazer. Copia imagens, salva PNG/JPEG, encaminha perguntas visuais à conversa e armazena referências recuperáveis no cérebro. Capturas locais não são automaticamente transmitidas ou memorizadas.

## 2. Arquitetura encontrada e decisões

- **Core:** domínio, configurações INI, fila do assistente, skills, conversa e Brain Markdown/YAML. **Platform.Windows:** áudio, Gemini Live e adaptadores do sistema. **App:** MAUI com janelas/controles WinUI nativos.
- A confirmação antiga dependia do `XamlRoot` principal. A LAURA inicia/permanece na bandeja e fechar a janela normalmente a oculta. Janelas WinUI independentes resolvem esse ciclo de vida; solicitações em segundo plano usam exibição sem ativação, sem sobreposição permanente.
- O provedor de conversa/voz existente é Gemini Live, com renderizador/fila de áudio, dispositivo e volume. Não havia um segundo TTS local ou autenticação confiável do locutor; esses recursos não foram inventados.
- As configurações continuam no armazenamento existente; credenciais continuam protegidas pelo mecanismo já usado pela aplicação. Políticas/histórico ficam em `Security/permissions.json`, separados do cérebro e dos prompts.
- O Brain mantém IDs estáveis, YAML, wikilinks, evidências/relacionamentos, índice/cache, revisões, conflitos e proteção de caminhos. Esses mecanismos foram estendidos para imagens, sem substituir o cérebro.
- Região/desktop reutilizam a captura GDI e codecs existentes. Janela usa Windows Graphics Capture/D3D11 de uma única imagem. UI/editor/save usam o mesmo tema e pipeline de ícones. A escolha evita bibliotecas adicionais e monitoramento contínuo.

O seletor COM de salvar foi descartado após um teste real expor cancelamento modal instável. O painel WinUI assíncrono reutiliza o host de diálogos validado, inclui pasta/nome/formato e oferece navegação de pasta pelo Windows. Seu cancelamento e seus botões PNG/JPEG foram efetivamente testados.

## 3. Componentes e arquivos

| Área | Criados | Modificados/integrados |
|---|---|---|
| Políticas | `Laura.Core/Permissions/PermissionAction.cs`, `PermissionOptions.cs`, `PermissionPolicy.cs`, `PermissionStore.cs`, `PermissionManager.cs` | `LauraSettings.cs`, `IniSettingsStore.cs` |
| Apresentação | `WindowsApprovalCenter.cs`, `WindowsDialogs.cs`, `LauraNotifications.cs`, `PermissionSettingsView.cs` | `WindowsConfirmation.cs`, `WindowsTrayHost.cs`, `Theme.cs`, `MainPage.cs`, `SetupPage.cs`, `MemoryPage.cs` |
| Execução | `MauiToolPermissions.cs` | `MauiAssistantTools.cs`, `MauiDesktopTools.cs`, `IProcessLauncher.cs`, `WindowsProcessLauncher.cs`, `WindowsComputerController.cs`, `LaunchApplicationSkill.cs`, `WebSearchSkill.cs` |
| Screenshot Core | `Laura.Core/Screenshots/ScreenshotDocument.cs`, `ScreenshotOptions.cs` | configurações e persistência INI |
| Captura/editor | `ScreenshotCoordinator.cs`, `ScreenshotSelectionWindow.cs`, `ScreenshotEditorWindow.cs`, `ScreenshotRendering.cs`, `WindowsWindowCapture.cs`, `WindowsScreenshotSaveDialog.cs`, `WindowsScreenshotShortcuts.cs`, `ScreenshotSettingsView.cs` | `WindowsScreenCapture.cs`, HUD, composição e ciclo de vida |
| Conversa/imagens | — | `IAssistantEngine.cs`, `IAssistantTools.cs`, `AssistantEngine.cs`, `GeminiSkillDispatcher.cs`, `SkillRequest.cs`, `ConversationTurn.cs`, `GeminiLiveProtocol.cs`, `GeminiLiveOptions.cs`, `GeminiLiveClient.cs` |
| Brain visual | — | `BrainService.cs`, leitor/seleção de anexos em `MemoryPage.cs` |
| Testes | `PermissionSmokeRunner.cs`, `ScreenshotSmokeRunner.cs`, `PermissionManagerTests.cs`, `ScreenshotDocumentTests.cs`, `BrainVisualTests.cs` | `MauiSmokeRunner.cs`, `GeminiLiveProtocolTests.cs`, `AssistantEngineTests.cs` |
| Recursos/documentação | sete SVGs de editor; [Permissions](laura-permissions.md), [Screenshot](laura-screenshot.md), este relatório | locales EN/PT, `README.md`, `ServiceConfiguration.cs`, `LauraApplication.cs` |

Os ícones existentes preservam os avisos/licença Lucide do repositório; os sete SVGs adicionais foram desenhados localmente com o mesmo estilo. Nenhum pacote de ícones novo foi incorporado.

## 4. Mecanismo de autorização

Os adaptadores congelam argumentos relevantes e passam pelo manager antes do efeito real. A identidade inclui operação, origem do adaptador, recursos, parâmetros/conteúdo, destino externo e marcador de gesto nativo quando aplicável. O modelo não fornece decisões, risco confiável ou políticas.

Operações desconhecidas são bloqueadas. Escritas de dados locais podem ser moderadas; scripts, executáveis, recursos sensíveis, mudanças de sistema e processos exigem revisão. Divulgação de arquivo/clipboard/tela/câmera/imagem e entrada arbitrária em aplicativos são críticas. A leitura de uma imagem do cérebro prepara bytes locais; o remetente multimodal exige autorização para o hash exato no envio real ao Gemini.

Permitir uma vez consome somente aquela solicitação. Sessão/persistência são oferecidas apenas a operações locais limitadas elegíveis. Uma política de escrita em pasta não permite leitura, execução, exclusão, envio externo ou alteração de segurança. Grupos de diretório respeitam separadores, não prefixos semelhantes. Revogação/reset valem antes de cada novo despacho.

Caminhos, links/reparse points, streams alternativos e metadados são revalidados. Ferramentas genéricas não escrevem no armazenamento da aplicação/segurança ou Brain; o Workspace guardado continua disponível. UNC/unidades de rede não são aceitos pelos adaptadores locais genéricos. Anexos Brain fixam o vault autorizado e o revalidam nas duas escritas; uma troca de vault não redireciona a operação.

O histórico não contém argumentos de comandos, conteúdo de arquivos/imagens, clipboard, credenciais ou prompts. Expiração, cancelamento, negação, encerramento e decisão tardia falham sem executar. A fila é limitada, duplicatas são rejeitadas e as tarefas não bloqueiam a UI. Esse mecanismo não é um sandbox de kernel para um programa arbitrário explicitamente aprovado, nem neutraliza outro processo hostil com a mesma conta Windows. UAC e restrições de captura permanecem em vigor.

## 5. Modos, notificações e voz

**Solicitar Aprovação** é o padrão e respeita operações exatas previamente concedidas. **Aprovação Automática** também utiliza políticas explícitas de diretório para efeitos locais elegíveis. Alto/crítico continuam exigindo revisão; não existe modo irrestrito.

O Centro de Aprovações contém pendências, histórico, permissões e gerenciamento. A janela, o contador no HUD e o menu da bandeja mantêm o acesso quando a principal está oculta/minimizada. Windows pode suprimir os balões; isso não decide a solicitação.

Anúncios usam Sempre / Somente oculta ou minimizada / Nunca. A fila existente foi preservada; um gate de leitura em voz separada permite anunciar enquanto uma ferramenta ocupa a conversa. O aviso é genérico e não envia detalhes privados da ação ao TTS em nuvem. Sem provedor/áudio disponível, a solicitação visual permanece. Aprovação exclusivamente por voz está indisponível por falta de autenticação segura do locutor.

## 6. Funcionamento do Screenshot

O coordenador limita a uma sessão de edição e ações concorrentes. A região usa pixels físicos do desktop virtual, aceita origem negativa, mostra dimensões, move/ajusta a seleção e cancela com Esc. A seleção de janela destaca bounds DWM e captura a identidade HWND/PID com WGC. Alteração de geometria/disponibilidade ou janela protegida resulta em falha explícita.

Anotações são objetos editáveis; o bitmap original permanece separado. O editor escala a apresentação e exporta na resolução original, escondendo alças de seleção. Save valida a pasta/nome/formato e confirma substituição; só então o manager autoriza e o arquivo é escrito por substituição atômica. Copy usa a área de transferência de imagens real, com retries limitados. Fechar a captura cancela trabalho e libera pixels/recursos. Limites: 32 milhões de pixels, 32 MiB codificados, 1.000 anotações e 100 passos de histórico.

As preferências registram os atalhos escolhidos e informam conflitos. Os comandos de voz/texto usam `screenshot_control` no catálogo existente; não foi criado outro interpretador. Não existe captura contínua escondida.

## 7. Integração com IA

Pergunta e PNG seguem a fila normal como um turno visual com pixels congelados. A imagem entra no contexto remoto da interação; a memória textual não grava binários temporários. Modelos desconhecidos são recusados como entrada visual; não existe um provedor local implementado nesta arquitetura.

O teste encontrou respostas baseadas na imagem anterior quando bytes eram enviados dentro de continuação de ferramenta. A correção conclui o resultado da ferramenta e usa o fluxo de turno visual direto comprovado, com autorização ao redor da transmissão. O teste final exigiu três imagens diferentes, incluindo texto/número jamais fornecidos ao modelo anteriormente. Falhas/negação não são anunciadas como análise bem-sucedida. O fluxo de voz reutiliza esse mesmo adaptador; sua conversa completa por microfone permanece uma verificação manual.

## 8. Integração com o cérebro

O usuário escolhe o assunto. Associação é por ID, título ou alias exatos; ambiguidades pedem decisão. Um novo assunto cria nó normal em `References`; imagens ficam em `Assets/Screenshots/<sha256>.png`. Referências Markdown são relativas e os dados/conexões existentes são preservados.

Repetir o mesmo anexo não duplica bytes ou referência. Exportações limitadas são reutilizadas por estado imutável do documento; isso corrige a deriva de rasterização/PNG encontrada pelo teste completo do coordenador e evita que ações repetidas sem edição criem outra referência. Edições invalidam esse cache, e o encerramento limpa seus buffers. Leitor e ferramenta recuperam a imagem real, com verificação de hash quando content-addressed. Mover/copiar/duplicar/arquivar notas recalcula referências; imagens ainda referenciadas não podem ser removidas. Não há coleta automática de assets. Uma falha entre gravação da imagem e do nó pode deixar um asset sem referência, reutilizável em retry; não existe transação atômica de dois arquivos.

## 9. Testes e evidências

| Verificação executada | Resultado/evidência |
|---|---|
| Build Release da solução, analisadores ativos | passou, 0 warnings / 0 erros |
| Suíte Core final | 243 passaram; 0 falhas / 0 ignorados |
| Gate Permissions anterior a Screenshot | [registro](laura-permissions.md#gate-executed-on-2026-10-13) |
| Permissions integrado após Screenshot | [PermissionsAcceptance/result.txt](../../_Output/PermissionsAcceptance/result.txt): aberta/oculta/minimizada, decisões nativas, gravação real, negação, cancelamento, expiração, store protegido e configurações |
| Voz final real | [PermissionsLiveFinal/result.txt](../../_Output/PermissionsLiveFinal/result.txt): PCM/transcrições de dois anúncios serializados com gate de conversa ocupado; volume zero, sem microfone/contexto pessoal |
| Regressão MAUI EN/PT | [LauraUiAcceptance/result.txt](../../_Output/LauraUiAcceptance/result.txt): navegação, cinco abas, Brain, HUD e 720×600 |
| Screenshot nativo + cloud real | [LauraScreenshotFinal/result.txt](../../_Output/ScreenshotStableFinal/result.txt): editor/pixels, PNG/JPEG, save buttons/cancelamento, clipboard, WGC, desktop local 1920×1080, hotkey registro/conflito e Brain recuperável |
| Visão/cérebro real | mesmo relatório: LAURA VISUAL TEST, segunda imagem distinta e **VECTOR 813** recuperado por `read_brain_image`, ausente da pergunta/imagens anteriores; autorização individual |
| Fechar captura com envio pendente | mesmo relatório: cancelou envio e rejeitou aprovação tardia |
| Revisão visual | [editor](../../_Output/ScreenshotStableFinal/editor-ui.png), [save form](../../_Output/ScreenshotStableFinal/save-form-ui.png): layout inspecionado |
| Privacidade/Git | `Build/Verify-BrainPrivacy.ps1` e `git diff --check` passaram; avisos de normalização CRLF não são erros de build |

Core inclui negação/contadores de despacho, timeout, cancelamento, limites/duplicatas, reinício, revogação, escopos/origens/efeitos, alteração de recursos e junction Windows, independência de Markdown, persistência, geometria negativa, histórico/anotações, formatos, atalhos, rede, fila visual congelada e troca de vault. Os testes existentes também passaram.

Os testes ao vivo referenciaram a credencial já protegida somente dentro do processo e enviaram apenas textos/imagens sintéticos. Captura do desktop foi exercitada localmente sem salvar/transmitir seus pixels. As instâncias de teste foram isoladas; processos bloqueados durante diagnóstico foram identificados por PID/argumentos e encerrados, e as versões corrigidas foram repetidas. A área de transferência foi substituída pela imagem sintética do teste.

## 10. Limitações e verificações pendentes

- Mouse real na seleção de região/janela, ajustes, ferramentas, teclado/acessibilidade e navegação de pastas.
- Disparo real dos atalhos com a janela principal oculta/minimizada. Registro/conflito foi testado; isso não prova keystrokes.
- Múltiplos monitores físicos, escalas DPI distintas, HDR, desconexão/mudança de resolução e restrições de aplicativos terceiros.
- Narrator/leitor de tela, Windows Não Perturbe/notificações desativadas e audição nos dispositivos escolhidos. PCM gerado não comprova audibilidade.
- Disco cheio, conversa completa de captura/análise/armazenamento pelo microfone e modelo fallback específico.
- Hit testing usa bounds; resize de seleção/anotação usa a alça inferior direita. Não há edição tipográfica avançada, monitoramento contínuo, voz autenticada ou sandbox adicional de processos.

A tentativa de Computer Use para os gestos retornou `Computer Use app approval timed out`; não houve cobertura interativa bem-sucedida. Os testes de software/nativos disponíveis passaram, mas essas pendências impedem declarar todos os critérios de aceitação integralmente verificados.

## 11. Uso e reprodução

Abra **Configurações → Segurança** para modos, voz, notificações e Centro de Aprovações. Abra **Preferências → LAURA Screenshot** para atalhos e monitores. O scan do HUD inicia região; no editor, use Copiar, Salvar, Analisar ou Cérebro. O escudo e a bandeja continuam acessíveis em segundo plano.

Comandos de teste e detalhes de configuração estão nos documentos de [Permissions](laura-permissions.md) e [Screenshot](laura-screenshot.md). Artefatos `_Output` são diagnósticos locais ignorados pelo Git. A evolução cognitiva subsequente solicita commits incrementais; a integração de permissões e captura é registrada como sua base testada. Não foi criado PR, instalador ou publicação.
