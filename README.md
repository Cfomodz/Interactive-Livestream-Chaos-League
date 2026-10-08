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

### Music

Put mp3, ogg or wav files in `Assets/StreamingAssets/Music` (or set `MusicFolder` in your config).
The game shuffles them in the background, and the king can pick a song with `!song`. Only use
music you're licensed to play on stream; see the README in that folder.

## License

This project is available under the [GNU General Public License v3.0](./LICENSE).

[DoodleChaos]: https://www.youtube.com/@DoodleChaos
