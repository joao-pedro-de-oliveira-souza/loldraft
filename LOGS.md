// Emillyn Alteração 10/11

# Classe
## Ui.cs
-> Novo método IEmote
-> Substituição 
    de: 
    emote: new Emoji(r.Emoji())
    por:
    emote: Ui.RoleEmote(r)

## TournamentUi
-> Substituição no método DraftComponents
    de:
    emote: new Emoji(p.PrimaryRole.Emoji())
    por:
    emote: Ui.RoleEmote(p.PrimaryRole)