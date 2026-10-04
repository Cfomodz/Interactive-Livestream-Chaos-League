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

## LiveChat Module

Generic live chat integration lives under `Assets/Scripts/LiveChat`. It exposes
platform-neutral message/event models and Twitch implementations:

- `LiveChat.Twitch.TwitchLiveChatClient` for chat messages
- `LiveChat.Twitch.TwitchPubSubClient` for bits, redemptions, and subs
- `LiveChat.YouTube.YouTubeLiveChatClient` is a stub you can wire to YouTube Data API

## Chat Command Foundation

For building chat-driven game ideas, there is a command routing layer in
`Assets/Scripts/LiveChat` under the `LiveChat.Commands` namespace:

- `ChatCommandRouter` listens to `LiveChatClientBase` messages and dispatches commands
- `ChatCommandHandler` is the base class for new commands
- `ChatCommandHelp`, `ChatCommandPing`, and `ChatCommandEcho` are example handlers

Quick setup:

1. Add `TwitchLiveChatClient` (or another `LiveChatClientBase`) to a GameObject.
2. Add `ChatCommandRouter` to the same object and set the command prefix (default `!`).
3. Add one or more `ChatCommandHandler` components as children of the router.
4. Create new commands by deriving from `ChatCommandHandler` and overriding
   `Execute(ChatCommandContext context)`.

The parser supports quoted arguments, for example: `!spawn "big boss" 3`.

## Contribute

Please read the CONTRIBUTING.md file (TODO) for how you can contribute to this project and what is important.

## Streaming Chaos League

To Stream Chaos League yourself, you need to meet certain prerequisites and also have to build the game yourself.

### Prerequisites

- Windows operating system. Linux Distros and Mac are currently **not** supported.
- Twitch affiliate status. The game requires channel points which are only available for affiliates on Twitch.

### Building the game

TODO: Add instructions for building the game.

## License

This project is available under the [GNU General Public License v3.0](./LICENSE).

[DoodleChaos]: https://www.youtube.com/@DoodleChaos
