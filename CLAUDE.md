# CLAUDE.md

Orientações para trabalhar neste repositório. Leia antes de alterar código.

## O que é a Laura

Assistente pessoal para Windows inspirada no Jarvis (versão feminina). Mora na
bandeja do sistema, atende por voz ("Ok, Laura" / "Hey Laura") e mostra a janela de
configurações com **Alt + L**. Migrada de Python para **C# / .NET 9**.

O idioma padrão é **inglês (en-US) com voz feminina em inglês**: as vozes e o
reconhecimento em inglês do Windows soam bem mais naturais que os equivalentes em
português. É só o padrão — o usuário troca para pt-BR na janela.

## Comandos

```powershell
dotnet build Laura.sln          # compilar tudo
dotnet test                     # rodar os testes
dotnet run --project Laura.App  # executar (Build/Run.bat faz o mesmo)
```

`Build/Publish.bat` gera um `Laura.exe` autocontido em `_Output/Publish`.

## Estrutura de pastas

Cada projeto fica em sua **própria pasta na raiz** — sem pasta `src/` agrupadora e
sem `Directory.Build.props` compartilhado (cada `.csproj` declara as próprias
propriedades). Não reintroduza nenhum dos dois.

```
Laura.sln
Laura.Core/            Laura.Platform.Windows/
Laura.App/             Laura.Core.Tests/
Build/  Data/
```

## Arquitetura — respeite as camadas

A dependência flui numa direção só: `App → Platform.Windows → Core`. **O `Core`
nunca depende de plataforma nem de UI.**

| Projeto | Papel | Pode depender de |
| --- | --- | --- |
| `Laura.Core` | Domínio puro (net9.0): motor, habilidades, configurações, localização | nada de plataforma |
| `Laura.Platform.Windows` | Adaptadores SAPI e Win32 | `Core` |
| `Laura.App` | Bandeja + UI em Windows Forms | `Core`, `Platform.Windows` |
| `Laura.Core.Tests` | Testes xUnit | `Core` |

Regras:
- Toda dependência externa (fala, sistema, rede, UI) entra no `Core` por **interface**
  em `Laura.Core/Abstractions`. Implementações concretas ficam em `Platform.Windows`
  ou `App`.
- A ligação abstração → implementação acontece **só** em
  [`ServiceConfiguration`](src/Laura.App/Composition/ServiceConfiguration.cs). Não
  instancie serviços com `new` fora da raiz de composição e dos testes.
- Para o domínio agir sobre a UI (abrir janela, encerrar), use `IShellController` —
  nunca referencie Windows Forms a partir do `Core`.

## UI: Windows Forms, não WPF nem MAUI

Decisão do dono do projeto. **Não migre para WPF nem MAUI.** Para um app de bandeja
Windows-only com hotkey global, WinForms é o encaixe certo (`NotifyIcon` nativo,
janela oculta trivial). A UI é montada **em código** (sem o designer). Toda a lógica
mora no `Core`, então a apresentação é substituível sem tocar no domínio.

## Modelo de concorrência — não bloqueie a UI

O `AssistantEngine` processa tudo num **único laço de consumo** alimentado por uma
fila (`System.Threading.Channels`). Regras ao mexer nele:
- Eventos do reconhecedor e chamadas da UI só **enfileiram** mensagens; nunca
  executam trabalho no chamador. A UI não pode travar enquanto Laura ouve ou fala.
- Só o laço altera o estado do motor — não introduza travas para proteger estado
  compartilhado; enfileire uma mensagem.
- Ao falar, a escuta é suspensa (senão Laura responde à própria voz). Preserve isso.
- Eventos vindos de threads de segundo plano que tocam a UI passam por
  `IUiDispatcher`.

## Como adicionar uma habilidade (skill)

1. Crie a classe em `Laura.Core/Skills/Builtin/` implementando `ISkill` (ou herdando
   de `PhraseSkillBase` quando ativada por lista de frases).
2. Defina `Priority`: valores **menores** são avaliados primeiro. Gatilhos
   específicos precisam preceder os genéricos (ex.: "abrir configurações" antes de
   "abrir").
3. As frases de ativação e as respostas vão nos **arquivos de idioma**, não no código.
   Adicione a chave em `LocalizationKeys` e o texto em cada `Locales/<cultura>.json`.
4. Registre em `AddSkills` na `ServiceConfiguration`.
5. Habilidade não deve lançar para fluxo normal (ex.: app inexistente → resposta
   falada, não exceção). Falhas inesperadas são isoladas pelo despachante.

Nunca escreva texto falado ou gatilho fixo (hard-coded) numa habilidade.

## Localização

- Textos e gatilhos: `src/Laura.Core/Localization/Locales/<cultura>.json`.
- Uma chave pode ter string única ou lista (variações sorteadas / múltiplos gatilhos).
- Ao adicionar uma chave, adicione em **todos** os idiomas (hoje `pt-BR` e `en-US`).
- Chaves acessadas pelo domínio devem existir em `LocalizationKeys` (erro de
  digitação vira erro de compilação).

## IA generativa é opcional

Comando não reconhecido por nenhuma habilidade **pode** ir a um modelo via
`IConversationEngine`, mas o padrão é `NullConversationEngine` (inerte). **Laura tem
de funcionar 100% offline.** Nunca torne a IA generativa um caminho obrigatório;
ela é um modo extra a ligar nas configurações.

## Estilo de código

- **Documente todo método com `<summary>` no estilo docstring do Google** — descrição,
  depois `Args:` / `Returns:` / `Raises:` quando aplicável. `<inheritdoc />` em
  implementações de interface.
- Comentários explicam o **porquê**, não o quê. Siga a densidade de comentários do
  código ao redor.
- Aplique SOLID, DRY, KISS e Clean Code. Nomes descritivos; um método faz uma coisa.
- `Nullable` e `ImplicitUsings` ligados; `TreatWarningsAsErrors` ligado — o build tem
  de passar **sem warnings**.
- Configurações são `record` imutáveis com um método `Sanitized()` que corrige
  valores fora de faixa; dados lidos do disco sempre passam por ele.
- **Idioma de TODO o código — identificadores, comentários, `<summary>`, logs,
  mensagens de exceção e mensagens de commit — é INGLÊS.** (Decisão do dono do
  projeto, revertendo o português anterior.) Apenas os arquivos de tradução
  `Locales/pt-BR.json` contêm português, porque são a tradução para o usuário final.
  Este próprio arquivo (CLAUDE.md) e a documentação para o dono podem seguir em
  português.

### Analisadores

Os projetos de domínio e plataforma desligam `CA1848` e `CA1716` (ruído para este
app); a UI desliga também `WFO1000`/`CA1859` (regras de designer/micro-perf). Cada
`.csproj` declara isso no próprio `NoWarn`. Só amplie o `NoWarn` com uma justificativa
em comentário; prefira corrigir o aviso.

## Configurações em disco

Persistidas em `Documentos/Laura Virtual Assistant/settings.ini` (via
`Environment.SpecialFolder.MyDocuments`) — formato **INI** de propósito, para poder
ser editado à mão. A gravação é UTF-8 **com BOM**, senão editores locais corrompem os
acentos. `IniDocument` faz o parse tolerante (linha malformada é ignorada, não quebra
a leitura); `IniSettingsStore` mapeia INI ↔ `LauraSettings`. Não volte para JSON nem
para AppData. Idioma padrão é **en-US** (voz feminina em inglês soa mais natural).

## UI Windows Forms — armadilhas já resolvidas (não regrida)

- Controles próprios (`Slider`, `ToggleSwitch`) pintam sobre **fundo opaco**
  (`Palette.Surface`/`Field`), nunca `Color.Transparent` — transparente no WinForms
  repinta o pai a cada quadro e **cintila** ao arrastar.
- Todo `Label` de conteúdo usa `UseMnemonic = false`; senão o `&` de textos como
  "Time & language" vira tecla de acesso e some da tela.
- Campos de entrada passam por `InputFactory`/`ThemedComboBox` para não exibir a
  borda e a seta claras do sistema sobre o tema escuro.
- O rótulo de valor de um slider é inicializado lendo `slider.Value`, não o mínimo
  da faixa.
- `Application.SetUnhandledExceptionMode` tem de ser chamado **antes** de qualquer
  controle existir (no início de `Main`), ou lança em runtime.
- Layout dos cartões: `TableLayoutPanel` com `Dock = Fill` numa coluna de 100%, para
  que todas as linhas tenham a mesma largura.

## Testes

- Lógica nova de domínio (normalização, casamento de frases, saneamento, despacho)
  precisa de teste em `Laura.Core.Tests`.
- Use os dublês em `TestDoubles/` em vez de mocar frameworks.
- A camada de plataforma (SAPI, Win32) e a UI não são cobertas por testes de unidade —
  mantenha a lógica testável no `Core`.

## Git

- Não faça commit sem o usuário pedir.
- Mensagens de commit em português.
