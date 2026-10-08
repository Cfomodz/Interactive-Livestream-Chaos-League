<img src="./.github/assets/logo.png" align="right" width="200">

# Chaos League

> [!NOTE]
> This README.md is a **Work in Progress**.  
> You will find places containing `TODO`s. This means that this part is not yet completed and may get updated in a future commit.

This is the source of the Chaos League Game.  
Chaos League is a Streaming-based Multiplayer Game created by the YouTuber [DoodleChaos].

## Structure

The project is split up into 3 folders:

- `Assets` which contains assets such as plugins, extensions and more.
- `Packages` containing the `manifest.json` and `packages-lock.json` files.
- `ProjectSettings` containing configuration (JSON) files and asset files.

## Live chat

Twitch chat, stream events (bits, subs, channel points), predictions and polls come from the
[unity-livechat](https://github.com/Cfomodz/unity-livechat) package (`com.cfomodz.livechat`),
installed through `Packages/manifest.json`. It talks to Twitch through EventSub and Helix, so
nothing needs to reach your PC from outside. See its README for the API.

The chat-controlled mini-games that used to sit on branches here live in
[chat-minigames](https://github.com/Cfomodz/chat-minigames).

## Contribute

Please read the CONTRIBUTING.md file (TODO) for how you can contribute to this project and what is important.

## Streaming Chaos League

To Stream Chaos League yourself, you need to meet certain prerequisites and also have to build the game yourself.

### Prerequisites

- Windows operating system. Linux Distros and Mac are currently **not** supported.
- Twitch affiliate status. The game requires channel points which are only available for affiliates on Twitch.
- Unity 6 (`6000.6.4f1`, see `ProjectSettings/ProjectVersion.txt`).

### Building the game

Open the project in Unity and use **Tools → Project Builder → Build**, or build from the command
line with `-executeMethod ProjectBuilder.BuildDefault`. Builds go to the same place every time,
`%USERPROFILE%\ChaosLeague\ChaosLeague.exe`, so Windows keeps per-app settings for it, such as
its output device in the Windows volume mixer (useful for sending game audio to a virtual cable).

Builds contain no personal data, so they're safe to share.

### Setting up

1. **Register a Twitch app** at [dev.twitch.tv/console](https://dev.twitch.tv/console/apps):
   OAuth redirect URL `http://localhost`, category "Chat Bot", **Client Type: Public**. Copy its
   Client ID.
2. **Create a bot account** on Twitch and make it a moderator of your channel (`/mod yourbot`).
3. **Run the game once.** It creates your config in the user data folder,
   `%USERPROFILE%\AppData\LocalLow\ChaosLeague\Chaos League\config.json`, with your details
   blank. Fill in `TwitchChannel` and `TwitchClientId` (and optionally `TwitchBotLogin`, your
   `!help` text and the `!wiki` / `!discord` / `!patreon` links), then restart the game.
4. **Log in.** The game opens Twitch's activation page twice: once for the bot account, once for
   your own (broadcaster) account. Logins are saved and refreshed automatically.

Your config only holds your details and the settings you change; everything else comes from the
defaults in `Assets/StreamingAssets/config.sample.json`. The same folder holds the player
database, its backups, the emote cache and the Twitch logins. It's outside the repo and the build
folder, so commits can't include it and rebuilds can't overwrite it.

### OBS control

The game can switch OBS scenes and start or stop the stream from your own chat, through
obs-websocket (built into OBS 28 and later). In OBS, open **Tools → WebSocket Server Settings**,
enable the server and set a password. Then fill in the OBS settings on the settings menu's
Networking page (or your config): `ObsEnabled`, `ObsUrl` (empty means `ws://127.0.0.1:4455`),
`ObsPassword` (masked, never logged), and optionally `ObsStartingScene` and `ObsGameScene`.

| Command (broadcaster only) | Does |
|---|---|
| `!clscene <name>` | Switch the scene. `starting` and `game` use the scenes above; part of a name works if it's unique |
| `!clgolive` | Switch to the game scene (if set), then start streaming |
| `!clend` | Asks to confirm: `!clend` again within 15 s (or `!clend confirm`) stops the stream; `!clend cancel` doesn't |
| `!clobs` | OBS status: connected, live, current scene |

The game only starts or stops the stream on these commands, never on its own. The settings menu
shows an OBS status line next to the Twitch one.

### Playing without Twitch (debug chat)

Set `UseDebugChat` to `true` in your config, or start the game with `-debugchat`, to play without
connecting to Twitch. A chat box appears in the bottom-left corner (`` ` `` shows or hides it): chat
there as the streamer, or use test commands to be other viewers and fake stream events.
`/help` lists them:

- `/as alice !invitedby @bob`: chat as another viewer
- `/bits alice 300 !lava`: cheer
- `/redeem alice 100` (or `lava` / `water`): redeem channel points
- `/sub`, `/gift`, `/giftbomb`, `/raid`, `/follow`

Predictions and polls are skipped, and players get the default profile picture.

For repeatable sessions, `-debugchatscript <file>` types a file's lines into the chat box, with
`/wait <seconds>` pauses. Add `-datadir <empty folder>` so test players don't land in your real
database. `Tools/debug-chat-smoke.txt` goes through most features:

```
ChaosLeague.exe -debugchat -datadir C:\Temp\chaos-test -debugchatscript Tools\debug-chat-smoke.txt
```

### Text to speech

Announcements and the king's chat are read aloud, fully offline. Run
`powershell -ExecutionPolicy Bypass -File Tools/get-piper.ps1` once to download
[Piper](https://github.com/rhasspy/piper) and the default voices (about 155 MB) into
`Assets/StreamingAssets/Piper`. Without it the game falls back to the voices built into Windows.

`TtsAnnouncerVoice` and `TtsPlayerVoice` in your config pick the voices (a Piper voice name or an
installed Windows voice), `TtsRate` the speed, and `enableTTS` / `enableKingTTS` turn it off. The
default player voice has 904 speakers, and each player always gets the same one. Before adding
other Piper voices, check their MODEL_CARD: some are licensed for non-commercial use only.

### Music

Put mp3, ogg or wav files in `Assets/StreamingAssets/Music` (or set `MusicFolder` in your config).
The game shuffles them in the background, and the king can pick a song with `!song`. Only use
music you're licensed to play on stream; see the README in that folder.

## License

This project is available under the [GNU General Public License v3.0](./LICENSE).

### Credits

- Text to speech: [Piper](https://github.com/rhasspy/piper) (MIT), which includes
  [espeak-ng](https://github.com/espeak-ng/espeak-ng) (GPL-3.0).
- Announcer voice: Piper `en_US-joe-medium`, from
  [OHF-Voice/voice-datasets](https://github.com/OHF-Voice/voice-datasets) (CC0).
- Player voices: Piper `en_US-libritts_r-medium`, trained on
  [LibriTTS-R](http://www.openslr.org/141/) by Koizumi et al. (CC BY 4.0).

[DoodleChaos]: https://www.youtube.com/@DoodleChaos
