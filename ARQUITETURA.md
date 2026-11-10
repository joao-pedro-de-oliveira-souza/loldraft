# LolDraft: arquitetura e guia

Bot de Discord + painel web para campeonatos internos de League of Legends com **draft de capitães**.
Este documento explica como o projeto está organizado, como as peças conversam e onde mexer para cada tipo de mudança.
Para instalar e rodar, veja o [README](README.md).

## Visão geral

```
            Discord (mensagens !comando, botões, menus)        Navegador (Angular)
                         │                                          │  REST + SignalR
                         ▼                                          ▼
   ┌──────────────────────────────── LolDraft.Host (um processo .NET 8) ───────────────────────────────┐
   │  Bot/Text      comandos !...           Web/Api        endpoints /api/...                        │
   │  Bot/Modules   botões e menus          Web/Realtime   hub SignalR /hubs/tournaments             │
   │  Bot/*         embeds, cargos/calls, timer do draft, DMs        Web/Auth  login com Discord      │
   │                         │                                          │                             │
   │                         └──────────────┬───────────────────────────┘                             │
   │                                        ▼                                                         │
   │                          LolDraft.Application (serviços + EF Core)                                │
   │                                        │                                                         │
   │                                        ▼                                                         │
   │                          LolDraft.Domain (regras puras, sem dependências)                         │
   └──────────────────────────────────────────────────────────────────────────────────────────────────┘
                         │                                          │
                     SQLite (loldraft.db)                      Riot API (opcional)
```

- **Um processo só**: o bot (Discord.Net) e a API (ASP.NET Core) rodam juntos no `LolDraft.Host`. Assim os dois usam os
  mesmos serviços e o mesmo banco, e um pick feito no Discord aparece no painel na hora (e vice-versa).
- **Roda no PC de quem hospeda**: o bot se conecta "de saída" ao Discord, então não precisa abrir porta. O banco é um
  arquivo SQLite criado sozinho.
- **Todas as regras ficam no domínio** (`Tournament`, `Match`...). Discord, painel e timer só chamam os serviços, então a
  regra é a mesma venha a ação de onde vier.

## Stack

| Parte | Tecnologia |
|---|---|
| Backend | .NET 8, ASP.NET Core (minimal APIs), SignalR, autenticação OAuth2 nativa (cookie + Discord) |
| Bot | Discord.Net 3.17 (`Discord.Commands` para os comandos `!`, `Discord.Interactions` para botões/menus) |
| Banco | EF Core 8 + SQLite |
| Front | Angular 22 (standalone, signals), `@microsoft/signalr`, fonte Geist (self-hosted) |
| Testes | xUnit (domínio, serviços com SQLite em memória, comandos de texto, Riot simulada) |

## Estrutura de pastas

```
LolDraft.sln
README.md                 instalação, configuração e lista de comandos
ARQUITETURA.md            este arquivo
src/
  LolDraft.Domain/        regras puras, sem pacotes externos
    Result.cs               Result / Result<T>: sucesso ou erro com mensagem pronta para o usuário
    Players/
      LolRole.cs            enum das 5 roles + Label() e Emoji()
      Player.cs             jogador (Discord id, roles, fill) + RiotAccount (owned); IsFake = id < 1.000.000
      EloScore.cs           tier/divisão/PL → número comparável; Label() → "Ouro II"
    Draft/
      DraftOrder.cs         ordem em cobrinha (1-2-3-4, 4-3-2-1...)
      RoleAssignment.cs     distribui as 5 roles no time (principal > secundária > fill), com capitão travado
      PoolFeasibility.cs    emparelhamento bipartido: os jogadores que sobram ainda fecham todos os times?
    Tournaments/
      Enums.cs              TournamentStatus, SeriesFormat (MD1/3/5), BracketType
      Entities.cs           TournamentEntry (inscrição), Team, DraftPick
      Tournament.cs         AGREGADO PRINCIPAL: inscrição, check-in, capitães, draft, auto-pick, balanceamento, chave
      Match.cs              série da chave: placar, formato, avanço do vencedor
    Bracket/
      SingleElimination.cs  gera a chave para qualquer nº de times (byes para os melhores seeds)

  LolDraft.Application/   casos de uso + persistência
    Data/AppDbContext.cs    mapeamento EF (DateTimeOffset como binário, propriedades calculadas ignoradas)
    Players/PlayerService.cs        cadastro, Riot ID manual (sem chave), parse de "Nome#TAG"
    Tournaments/TournamentService.cs  carrega o agregado, aplica a operação com lock, salva e dispara eventos
                                     + ITournamentEvents, NoopTournamentEvents, TournamentLocks
    Riot/RiotApiClient.cs   cliente HTTP: account-v1, summoner-v4, league-v4 (por PUUID) + RiotOptions
    Riot/RiotLinkService.cs vincula conta e sincroniza elo (singleton; usa escopos para o banco)
    Dev/DevService.cs       jogadores de teste (🤖) para simular campeonatos

  LolDraft.Host/          bot + API + arquivos do painel
    Program.cs              composição: DI, intents do Discord, auth, SignalR, banco (EnsureCreated/Migrate)
    appsettings.json        configuração pública (segredos ficam em appsettings.Local.json, fora do git)
    Bot/
      DiscordBotService.cs  conecta o bot, lê mensagens "!", despacha botões/menus, trata erros; DiscordOptions
      Text/                 COMANDOS DE TEXTO (!)
        TextInfra.cs          [Help], seções do !ajuda, Parse (apelidos de role/formato), TypeReaders,
                              TextModule (base), HelpText (monta o !ajuda), TextCommandSetup (carga)
        PlayerCommands.cs     !ajuda, !registrar, !perfil, !jogadores, !painel-cadastro, !vincular, !elo...
        CampCommands.cs       grupo !camp: criar, config, capitães, check-in, draft, chave, calls, encerrar
        DevCommands.cs        grupo !dev (só com Dev:Enabled = true)
      Modules/              BOTÕES E MENUS (InteractionService)
        RegistrationComponents.cs  painel de cadastro (reg:*)
        TournamentComponents.cs    inscrição/check-in (t:*), menu do draft (d:*), resultado das partidas (m:*)
      Ui.cs                 embeds de perfil e do painel de cadastro, cores, Tag() (menção ou 🤖 nome)
      TournamentUi.cs       embeds/componentes da inscrição e do quadro do draft
      BracketUi.cs          embeds da chave, cartão da partida, campeão
      DiscordTournamentEvents.cs  ouve os eventos e atualiza mensagens no canal
      DraftNotifier.cs      DMs: "você foi escolhido" e elenco final do time
      DiscordResources.cs   cria/apaga cargos, categorias, chats e calls; move jogadores
      DraftTimerService.cs  a cada 3s: auto-pick quando o tempo acaba (ou capitão 🤖)
    Web/
      Api.cs                endpoints REST /api/... (jogador e admin) + login de desenvolvimento
      Auth.cs               OAuth2 com Discord (cookie), AdminChecker (Gerenciar servidor)
      Dtos.cs               DTOs do painel + DtoMapper (ids do Discord como string)
      Realtime.cs           TournamentHub (SignalR), SignalRTournamentEvents, CompositeTournamentEvents
    wwwroot/                build do Angular (gerado por `npm run build` em web/; não editar à mão)

tests/LolDraft.Tests/
  TestData.cs               fábricas de jogadores/campeonatos em memória
  DraftTests.cs             cobrinha, roles, travas, auto-pick, timer
  BracketTests.cs           chave, byes, formatos, placar, renomear time
  ServiceFlowTests.cs       fluxos completos com EF + SQLite em memória (um DbContext por passo)
  RiotTests.cs              vínculo/elo com Riot falsa, cliente HTTP com handler falso, balanceamento
  TextCommandTests.cs       apelidos, roteamento e validação dos comandos !, limites do !ajuda
  NotificationTests.cs      conteúdo das DMs

web/                      painel Angular
  src/app/models.ts         tipos espelhando os DTOs + cores/rótulos de role
  src/app/store.ts          estado global (signals), chamadas da API, conexão SignalR, relógio do pick
  src/app/ui.ts             componentes pequenos: role-tag, player-name
  src/app/app.ts            casca: barra de navegação, login, toasts
  src/app/pages/            home (campeonato), draft, bracket (chave), players (jogadores/perfil)
  src/styles.scss           tokens de design (tema claro + escuro automático)
  proxy.conf.json           `npm start` redireciona /api, /auth, /hubs para http://localhost:5080
```

**Regra de dependência:** `Domain` ← `Application` ← `Host`. O domínio não conhece EF, Discord nem HTTP; a aplicação
não conhece Discord. Tudo que é Discord ou web fica no Host.

## Modelo de dados

```
Player 1───* TournamentEntry *───1 Tournament
                 │ (TeamId)            │
                 └──────* Team *───────┤  (Team.CaptainEntryId → entry do capitão)
                                       ├──* DraftPick (TeamId, EntryId)
                                       └──* Match (TeamA, TeamB, Winner, NextMatch → Match)
```

- **Tournament** guarda a configuração (times, formato padrão/semi/final, timer, travas de role, check-in), o status e os
  ids das mensagens/canais do Discord (painel, quadro do draft, lobby).
- **Team** guarda os ids de cargo/categoria/chat/call criados no Discord, para renomear e apagar depois.
- **Match.Number** é o número visível ("partida #3"); é ele que comandos e API usam, não o `Id`.
- Ids do Discord são `ulong`. No SQLite, **igualdade funciona em consultas, mas `<`/`>` não** (o EF não traduz):
  filtre em memória quando precisar (veja `DevService`).

### Ciclo de vida do campeonato

```
Registration ──!camp checkin──► CheckIn ──!camp iniciar-draft / balancear──► Drafting ──último pick──► Drafted
     │                                                                                              │
     └────────────────────────────── !camp cancelar ─► Cancelled          !camp chave ──► Running ──► Finished
```

## Fluxos principais

### Uma ação qualquer (ex.: pick)

1. Entrada: menu do draft no Discord (`TournamentComponents`), botão no painel (`POST /api/tournaments/{id}/pick`) ou o
   timer (`DraftTimerService`).
2. `TournamentService.PickAsync` pega o **lock do campeonato** (`TournamentLocks`), carrega o agregado completo, chama
   `Tournament.Pick(...)` e salva.
3. Dispara `ITournamentEvents.PickMadeAsync`. O `CompositeTournamentEvents` repassa para:
   - `DiscordTournamentEvents`: edita o quadro do draft, avisa no canal e pede a DM ao `DraftNotifier`;
   - `SignalRTournamentEvents`: manda o estado completo para os navegadores que estão assistindo.

Toda operação que muda o campeonato segue esse caminho (`MutateAsync`/`MatchOpAsync` no serviço). **Não salve o agregado
por fora do serviço**, senão Discord e painel ficam desatualizados.

### Draft

- A ordem é cobrinha (`DraftOrder.Snake`). O capitão já ocupa a role principal dele.
- `Tournament.CanTeamPick` aplica duas travas (desligáveis com `!camp config roles nao`):
  1. o time continua conseguindo fechar as 5 roles (`RoleAssignment`);
  2. os jogadores que sobram ainda completam todos os outros times (`PoolFeasibility`).
- `AutoPick` escolhe quem fecha uma role que falta e, depois, o maior elo.
- Capitão 🤖 (jogador de teste) escolhe sozinho depois de 3s (`IsBotTurnReady`).
- `!camp balancear` faz o draft inteiro automático em **dois passos salvos separadamente**: primeiro cria os times (para
  terem `Id` no banco), depois faz os picks. Se juntar os dois, os `DraftPick.TeamId` ficam 0.

### Depois do draft

`!camp montar` → `DiscordResources.SetupAsync`: cargo colorido por time, categoria privada com chat e call, call de Lobby.
O bot só pode liberar num canal privado **permissões que ele mesmo tem**; por isso as permissões da call são só ver,
conectar e conversar (falar já vem liberado para todo mundo).
`!camp chave` → `SingleElimination.Generate`. Os resultados vêm dos botões do cartão da partida, do painel (+1) ou de
`!camp placar`. Quando uma série fecha, o evento posta o cartão da próxima partida; na final, anuncia o campeão.

### Riot

`RiotLinkService` resolve `Nome#TAG` na account-v1 (rota regional `americas`), lê o perfil na summoner-v4 e o elo na
league-v4 (solo/duo; se não tiver, flex), tudo por PUUID na plataforma `br1`. Sem `Riot:ApiKey`, `!vincular` só guarda o
Riot ID. O elo vira um número (`EloScore`) usado no auto-pick, no balanceamento e nas médias de time.

## Comandos de texto (como funcionam)

- `DiscordBotService.HandleMessageAsync` só trata mensagens de servidor que começam com o prefixo (`Discord:Prefix`, padrão
  `!`) seguido de letra. Cada comando roda em **segundo plano e no seu próprio escopo de DI** (o banco é scoped).
- `TextCommandSetup.LoadAsync` registra os leitores de tipo e os módulos **dentro de um escopo**: o Discord.Net instancia
  cada módulo uma vez ao registrar, e resolver serviço scoped no provedor raiz quebra em Development.
- Erros viram respostas úteis em `OnCommandExecutedAsync`: comando desconhecido → sugere `!ajuda`; argumento errado →
  mostra o uso e o exemplo do atributo `[Help]`; sem permissão → avisa que é para admins.

### Como adicionar um comando

```csharp
// em Bot/Text/CampCommands.cs (grupo !camp) ou num novo TextModule
[Command("pausar"), RequireUserPermission(GuildPermission.ManageGuild)]
[Summary("Pausa o timer do draft.")]                                   // aparece no !ajuda
[Help(HelpSections.Draft, "!camp pausar [minutos]", "!camp pausar 5")] // uso + exemplo (obrigatório)
public async Task PauseAsync(int minutos = 5)
{
    var t = await Active();
    if (t == null) return;
    var r = await tournaments.MutateAsync(t.Id, x => Task.FromResult(x.Pausar(minutos)), "paused");
    await (r.Ok ? Say("⏸️ Draft pausado.") : Fail(r.Error!));
}
```

- Regra nova vai no **domínio** (método no `Tournament` que devolve `Result`), com teste em `tests/`.
- Use `Say`/`Fail` do `TextModule` (respondem à mensagem do usuário, sem marcar ninguém).
- O teste `Every_command_has_help_and_help_fits_discord_limits` falha se faltar `[Help]` ou `[Summary]`.
- Para botões/menus, crie um método `[ComponentInteraction("prefixo:*")]` em `Bot/Modules` e responda em até 3s
  (`DeferAsync` se for demorar).

## Painel web

- `Program.cs` serve o `wwwroot` (Angular compilado) e cai no `index.html` para as rotas do front.
- O front carrega `/api/me` e `/api/tournaments/current`, conecta no hub e chama `Watch(id)`. A cada mudança chega
  `tournamentUpdated(dto, tipo, mensagem)` com o **estado completo** (o DTO é pequeno), e o `Store` só substitui o signal.
- Permissões são checadas **no servidor** (`Api.cs`): capitão da vez ou admin para picks; capitães da partida ou admin
  para resultado; endpoints `/admin/*` só para quem tem Gerenciar servidor.
- Login: OAuth2 do Discord com cookie. Precisa de `Discord:ClientSecret` e do redirect `http://localhost:5080/signin-discord`
  no Developer Portal. Em Development dá para simular com `Web:DevLogin = true` e `/auth/dev-login?discordId=...`.
- Depois de mexer no front: `cd web && npm run build` (gera o `wwwroot`).

## Configuração

`appsettings.json` (versionado) + `appsettings.Local.json` (segredos, no `.gitignore`):

| Chave | Para quê |
|---|---|
| `Discord:Token` | token do bot (**segredo**) |
| `Discord:GuildId` | servidor do campeonato (painel, admin, limpeza de comandos antigos) |
| `Discord:Prefix` | prefixo dos comandos (padrão `!`) |
| `Discord:DmNotifications` | DMs de "você foi escolhido" e elenco final (padrão `true`) |
| `Discord:ClientId` / `Discord:ClientSecret` | login do painel (secret é **segredo**) |
| `Riot:ApiKey` / `Riot:Platform` / `Riot:Region` | Riot API (`br1` / `americas`) |
| `Dev:Enabled` | liga os comandos `!dev` (jogadores 🤖) |
| `Web:DevLogin` / `Web:DevAdminId` | login falso para testar o painel em Development |
| `ConnectionStrings:Default` | caminho do SQLite (padrão `loldraft.db` na pasta do Host) |

No Developer Portal, aba **Bot**, precisam estar ligadas: **Server Members Intent** e **Message Content Intent**.

## Testes

```bash
dotnet test
```

- Domínio: testes puros, rápidos. Para simular Ids do banco use `TestData.AssignTeamIds`/`AssignMatchIds`.
- Serviços: `ServiceFlowTests` usa SQLite em memória com **um DbContext novo por passo**, igual ao bot (pega erros de
  mapeamento e de tradução de consulta que testes puros não pegam).
- Comandos: `TextCommandTests` carrega os módulos com `ValidateScopes = true`, como o ASP.NET em Development.
- Riot: `FakeRiot` (em `RiotTests.cs`) implementa `IRiotApi`; nada chama a Riot de verdade.

## Convenções

- Mensagens para o usuário em **português**, curtas, e já prontas no `Result` do domínio (o Discord e o painel só exibem).
- Erro de regra → `Result.Fail("...")`; exceção só para falha inesperada.
- Ids do Discord trafegam como **string** no JSON (JavaScript perde precisão acima de 2^53).
- Jogadores 🤖 têm `DiscordUserId < 1.000.000`: não recebem cargo, não são movidos, não recebem DM e não são marcados.
- Comandos de admin usam `RequireUserPermission(GuildPermission.ManageGuild)`; mover jogadores usa `MoveMembers`.

## Banco e mudanças de schema

Hoje o banco é criado com `EnsureCreated` (não existe pasta de migrations). Ao **adicionar coluna/tabela**:

1. Crie as migrations: `dotnet ef migrations add Inicial -p src/LolDraft.Application -s src/LolDraft.Host`
   (a primeira representa o schema atual; depois `dotnet ef migrations add <Nome>` para cada mudança).
2. O `Program.cs` passa a usar `Migrate()` sozinho quando encontra migrations.
3. Um `loldraft.db` criado antes das migrations não tem a tabela de histórico: apague o arquivo (ou registre a migração
   inicial manualmente) antes de subir.

## Limitações conhecidas / próximos passos

- Chave só em eliminação simples (dupla eliminação e pontos corridos ainda não existem).
- O painel roda no PC do host; para outras pessoas acessarem é preciso um túnel (ex.: Cloudflare Tunnel) e registrar a
  URL extra nos Redirects do OAuth2.
- Redes com proxy autenticado (rede corporativa) bloqueiam a Riot API com erro 407; não há configuração de proxy no app.
- Mensagens dos comandos de texto aparecem para todos no canal (comandos de texto não têm resposta "só você vê").
