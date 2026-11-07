# WhosYourKiller

Client-only SPT 4.1.6 plugin. When a bot kills you, the death screen's "watch profile" button shows the killer's gear, as it does for player killers in live Tarkov. The server is not involved: the profile is mocked from the bot's in-raid equipment.

PMC bots also get an invented career: stats and achievements. These are generated from the bot's level and prestige, skewed by its SAIN personality when SAIN is installed. The numbers are flavor only.

## How it works

- `Player.OnBeenKilledByAggressor`: snapshot of the killer's equipment (and, for PMC bots, the generated career).
- `SessionResultExitStatus.Show`: shows the watch-profile button, which the game hides for AI killers.
- `ClientBackendSession.GetOtherPlayerProfile`: answers the profile request locally for those killers.
- `KillListVictim.Show`: makes the post-raid kill list rows clickable, opening the victim's profile.

## Install

Copy `WhosYourKiller.dll` to `BepInEx/plugins/WhosYourKiller/`. SAIN is optional.

## Build

Set `SptDir` in `WhosYourKiller.csproj.user` (not committed) to your SPT folder, then `dotnet build -c Release`. The build copies the DLL into the game.

## Notes

Written with AI assistance (Claude). Untested beyond the author's own game. Not affiliated with Battlestate Games or SPT.

MIT license.
