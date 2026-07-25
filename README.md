# Laura — Assistente Virtual

Laura é uma assistente pessoal para Windows inspirada no Jarvis, em uma versão
feminina. Ela mora na bandeja do sistema, atende por voz ao comando **"Ok, Laura"**
ou **"Hey Laura"** e cuida de tarefas simples do dia a dia — dizer as horas e a data,
pesquisar na web, abrir aplicativos, ajustar o volume e bloquear o computador.

A interface fica sempre escondida e aparece com **Alt + L**, servindo para
trocar a voz, o idioma, o tom e as demais preferências. Toda a escuta e fala acontece
fora da thread da interface, então a janela nunca trava enquanto Laura ouve ou responde.

## Requisitos

- Windows 10 ou 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) para compilar
- Um microfone e o pacote de fala do idioma desejado
  (Configurações do Windows › Hora e idioma › Fala) para os comandos de voz

Sem microfone ou sem reconhecedor instalado, Laura continua utilizável apenas pela
janela de configurações.

## Como executar

```powershell
dotnet run --project Laura.App
```

Ou use os scripts em `Build/`:

- `Build/Run.bat` — compila e executa em modo de depuração
- `Build/Publish.bat` — gera um `Laura.exe` autocontido em `_Output/Publish`

## Arquitetura

A solução separa domínio, plataforma e apresentação para manter as regras da
assistente independentes de Windows e de interface gráfica.

| Projeto | Responsabilidade |
| --- | --- |
| `Laura.Core` | Domínio puro: configurações, localização, habilidades e o motor da assistente. Sem dependências de plataforma. |
| `Laura.Platform.Windows` | Adaptadores Windows: síntese e reconhecimento de fala (SAPI), controle de volume, inicialização automática. |
| `Laura.App` | Aplicação de bandeja e janela de configurações em Windows Forms. |
| `Laura.Core.Tests` | Testes de unidade do domínio. |

O motor conversa com o mundo apenas por interfaces (`ISpeechSynthesizer`,
`ISpeechRecognizer`, `ISkill`, `ISettingsService`, …), e a raiz de composição em
[`ServiceConfiguration`](Laura.App/Composition/ServiceConfiguration.cs) liga cada
abstração à sua implementação.

### Habilidades

Cada capacidade é uma implementação de `ISkill` que decide sozinha se um comando lhe
pertence. Acrescentar uma capacidade nova é registrar mais uma habilidade, sem tocar
no motor nem nas existentes. As frases que ativam cada habilidade vivem nos arquivos
de idioma, não no código.

### Idiomas

Os textos e gatilhos ficam em `Laura.Core/Localization/Locales/<cultura>.json`.
Traduzir Laura ou adicionar uma forma de pedir algo é editar um arquivo de idioma.
Já vêm português (Brasil) e inglês (EUA).

### Modo generativo (planejado)

O motor já prevê um complemento opcional de IA generativa: comandos que nenhuma
habilidade reconhece podem, no futuro, ser encaminhados a um modelo local como o
[Ollama](https://ollama.com/) através da interface `IConversationEngine`. Hoje o
registro padrão é inerte (`NullConversationEngine`) — **Laura funciona inteiramente
offline**, e a IA generativa será apenas um modo extra a ligar nas configurações.

## Testes

```powershell
dotnet test
```
