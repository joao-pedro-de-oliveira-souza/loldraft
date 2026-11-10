# LolDraft: campeonatos com draft de capitães para Discord

> Vai mexer no código? Comece pelo [ARQUITETURA.md](ARQUITETURA.md): estrutura, fluxos e como adicionar comandos.

Bot + API em .NET 8 (o painel Angular entra na fase 4). Roda no seu PC, com banco SQLite em arquivo.

```
src/
  LolDraft.Domain/        regras puras: jogadores, roles, draft, chave (sem dependências)
  LolDraft.Application/   EF Core (SQLite) + serviços usados pelo bot e pela API
  LolDraft.Host/          ASP.NET Core + bot Discord.Net no mesmo processo
tests/
  LolDraft.Tests/         testes xUnit do draft e da chave
```

## 1. Criar o bot no Discord

1. Acesse https://discord.com/developers/applications → **New Application**.
2. Aba **Bot** → **Reset Token** e copie o token (não compartilhe).
3. Ainda em **Bot**, ative **Server Members Intent** (mover jogadores entre calls) e **Message Content Intent** (ler os comandos `!`).
4. Aba **OAuth2 → URL Generator**:
   - Scopes: `bot` e `applications.commands`
   - Permissões: `Manage Roles`, `Manage Channels`, `Move Members`, `View Channels`,
     `Send Messages`, `Embed Links`, `Connect`
5. Abra a URL gerada e adicione o bot ao seu servidor.
6. Em **Configurações do servidor → Cargos**, arraste o cargo do bot **para cima** dos cargos que ele vai criar
   (o Discord só deixa um bot gerenciar cargos abaixo do dele).

## 2. Configurar

Opção A (mais simples): crie `src/LolDraft.Host/appsettings.Local.json` (já está no `.gitignore`):

```json
{ "Discord": { "Token": "SEU_TOKEN", "GuildId": "ID_DO_SERVIDOR" } }
```

Opção B: user-secrets. Pegue o ID do servidor (Discord → Configurações → Avançado → Modo desenvolvedor; depois clique com o
botão direito no servidor → Copiar ID).

```bash
cd src/LolDraft.Host
dotnet user-secrets set "Discord:Token" "SEU_TOKEN"
dotnet user-secrets set "Discord:GuildId" "ID_DO_SERVIDOR"
```


## 3. Rodar

```bash
dotnet run --project src/LolDraft.Host
```

- O banco `loldraft.db` é criado automaticamente na pasta do Host.
- API: http://localhost:5080/health e http://localhost:5080/api/players
- O bot conecta "de saída" no Discord: não precisa abrir porta no roteador.

Testes:

```bash
dotnet test
```

## Comandos

Todos os comandos são de texto com `!`. Mande `!ajuda` no Discord para ver a lista completa com o que cada um faz,
ou `!ajuda camp` para filtrar. Se o comando estiver errado, o bot responde com o jeito certo e um exemplo.
O prefixo pode ser trocado em `"Discord": { "Prefix": "!" }`.

### Jogador

| Comando | Quem | O que faz |
|---|---|---|
| `!registrar <principal> [secundária] [fill]` | todos | cria/atualiza o cadastro |
| `!perfil [@jogador]` | todos | mostra o perfil |
| `!vincular Nome#TAG` | todos | vincula a conta Riot e puxa o elo (veja a seção Riot API) |
| `!desvincular` | todos | remove o Riot ID |
| `!jogadores` | todos | lista por role |
| `!painel-cadastro` | admin | posta um painel com botão; cada pessoa edita suas roles por menus |

## Avisos por DM

Quem é escolhido no draft recebe uma DM com o time, o capitão e a role. Quando os times fecham (draft ou
`!camp balancear`), todo mundo recebe o elenco completo do próprio time. Para desligar:
`"Discord": { "DmNotifications": false }`. Quem bloqueia DMs do servidor simplesmente não recebe.

## Comandos de teste

`!dev popular` e `!dev limpar` criam e apagam jogadores falsos (🤖) para simular um campeonato sozinho.
Eles só aparecem com `"Dev": { "Enabled": true }` no `appsettings.json` (ou no `appsettings.Local.json`).
Coloque `false` e reinicie o bot para tirá-los do Discord.

## Comandos de campeonato (fase 2)

| Comando | Quem | O que faz |
|---|---|---|
| `!camp criar <times> <formato> <nome>` | admin | cria e posta o painel de inscrição (botões Inscrever/Sair) |
| `!camp config [opção] [valor]` | admin | muda times, formatos (MD1/MD3/MD5), timer, travas de role, check-in |
| `!camp capitao @jogador` / `!camp tirar-capitao @jogador` | admin | define capitão (inscreve automaticamente se precisar) |
| `!camp inscrever @jogador` / `!camp remover @jogador` | admin | gerencia inscritos manualmente |
| `!camp checkin` | admin | fecha inscrições e abre a confirmação de presença |
| `!camp iniciar-draft [fixo]` | admin | começa o draft neste canal |
| `!camp autopick` | admin | força o pick do time da vez |
| `!camp status` | todos | mostra o painel atual |
| `!camp cancelar` | admin | cancela o campeonato |

No draft, o capitão da vez escolhe pelo menu do quadro (só aparecem jogadores que fecham uma role
que falta). O quadro mostra o tempo restante e se atualiza sozinho; quando o tempo acaba, o bot faz
auto-pick. Admins também podem escolher por qualquer time.

## Depois do draft (fase 3)

| Comando | Quem | O que faz |
|---|---|---|
| `!camp renomear-time <número> <nome>` | admin | renomeia o time (e o cargo/canais, se já existirem) |
| `!camp montar` | admin | cria por time: cargo colorido (separado na lista de membros), categoria com chat e call privados; e uma call de **Lobby** |
| `!camp chave [fixo]` | admin | gera a chave e posta os cartões das partidas prontas |
| `!camp partida <número>` | todos | cartão da partida com botões "X venceu o jogo" e "Chamar para as calls" |
| `!camp ver-chave` | todos | mostra a chave atual |
| `!camp placar <partida> <a> <b>` | admin | corrige o placar de uma série |
| `!camp formato <partida> <md1|md3|md5>` | admin | troca MD1/MD3/MD5 de uma partida |
| `!camp chamar [partida]` | quem move membros | move os jogadores para as calls dos times |
| `!camp lobby` | quem move membros | junta todo mundo na call de Lobby |
| `!camp encerrar` | admin | apaga cargos, chats e calls do campeonato |

Os capitães (ou admins) registram cada jogo pelos botões do cartão. Quando uma série fecha, o bot anuncia
o vencedor, posta o cartão da próxima partida marcando os dois times e, na final, anuncia o campeão.

> O Discord só deixa mover quem já está conectado em alguma call. O cargo do bot precisa ficar
> **acima** dos cargos dos times.

## Painel web (fase 4)

Com o bot rodando, abra **http://localhost:5080**. O painel já vem compilado em `src/LolDraft.Host/wwwroot`.

- **Campeonato**: inscritos por role, botões de inscrever/confirmar presença; admins marcam capitães clicando no jogador e começam o draft.
- **Draft**: relógio do pick, quadro roles × times e lista de disponíveis com filtro. O capitão da vez (ou admin) escolhe ali mesmo. Tudo atualiza ao vivo (SignalR), inclusive picks feitos pelo Discord.
- **Chave**: partidas por rodada; capitães e admins registram cada jogo com **+1**. Campeão em destaque no fim.
- **Jogadores**: lista geral e edição do seu perfil (mesmo cadastro do `!registrar`).

### Login com Discord

1. Developer Portal → seu app → **OAuth2** → **Redirects** → adicione `http://localhost:5080/signin-discord` e salve.
2. Na mesma página, **Reset Secret** e copie o Client Secret.
3. Em `src/LolDraft.Host/appsettings.Local.json`, adicione `"ClientSecret": "..."` dentro de `"Discord"`.

Sem o secret o painel funciona só para visualizar. Admin no painel = quem tem **Gerenciar servidor** no Discord.

### Mexer no front

```bash
cd web
npm install
npm start            # http://localhost:4200, com proxy para a API em :5080
npm run build        # gera de novo o wwwroot
```

### Abrir para os amigos (opcional)

O painel roda no seu PC. Para outras pessoas acessarem, use um túnel, por exemplo
`cloudflared tunnel --url http://localhost:5080`, e adicione a URL https gerada + `/signin-discord`
nos Redirects do OAuth2.

## Riot API (fase 5)

1. Entre em https://developer.riotgames.com com sua conta Riot. A **Development API Key** do painel já funciona,
   mas expira a cada 24h. Para não precisar trocar todo dia, use **Register Product → Personal API Key**
   (bot de servidor privado; a análise da Riot pode levar alguns dias).
2. Em `src/LolDraft.Host/appsettings.Local.json` adicione:

```json
"Riot": { "ApiKey": "RGAPI-...", "Platform": "br1", "Region": "americas" }
```

| Comando | Quem | O que faz |
|---|---|---|
| `!vincular Nome#TAG` | todos | acha a conta na Riot e já puxa o elo |
| `!elo` | todos | atualiza o seu elo |
| `!sincronizar-elos` | admin | atualiza o elo de todas as contas vinculadas |
| `!camp balancear` | admin | monta os times automaticamente por elo (sem draft manual), respeitando as roles |

O elo (solo/duo; se não tiver, flex) entra no auto-pick e no balanceamento, e a média de cada time aparece no
resumo do Discord e no painel. Sem a chave, `!vincular` só guarda o Riot ID.

## Regras já implementadas no domínio (com testes)

- **Draft em cobrinha** (1-2-3-4 / 4-3-2-1), o capitão já ocupa a role principal dele.
- **Travas de role**: o capitão só pode escolher alguém que feche uma role que falta, e o sistema
  impede picks que deixariam outro time sem jogador para alguma role (emparelhamento bipartido).
- **Timer por pick** com **auto-pick** (prioriza role que falta e, depois, o elo).
- **Distribuição automática de roles** no time (principal > secundária > fill).
- **Chave flexível**: eliminação simples para qualquer número de times (2 a 32), byes automáticos
  para os melhores seeds, formato padrão MD1/MD3/MD5 com override para semifinal e final, e troca
  de formato no meio da série quando o placar permite.

## Próximas fases

2. ~~Comandos de campeonato: criar, inscrição com botão, check-in, capitães, draft no Discord com timer~~ ✅
3. ~~Pós-draft: cargo por time, categoria/canais/calls privadas, mover jogadores, chave e resultados, limpeza~~ ✅
4. ~~Painel Angular com login Discord e draft ao vivo (SignalR)~~ ✅
5. ~~Riot API: vínculo de conta, elo automático, balanceamento~~ ✅

## Migrations (quando o schema mudar)

Na fase 1 o banco é criado com `EnsureCreated`. Quando quiser migrations:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Inicial -p src/LolDraft.Application -s src/LolDraft.Host
```

Ao detectar migrations, o Host passa a usar `Migrate()` sozinho. Se o banco antigo der conflito,
apague `loldraft.db` (por enquanto só tem cadastros de teste).
