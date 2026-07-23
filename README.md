# Pacos Telegram Bot

[![master branch - test, build, push, deploy](https://github.com/magicxor/pacos-agy-acp/actions/workflows/on_master_push.yml/badge.svg)](https://github.com/magicxor/pacos-agy-acp/actions/workflows/on_master_push.yml)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/magicxor/pacos-agy-acp)

Pacos is a .NET-based Telegram bot designed to interact in group chats. It drives the agy (Google Antigravity) CLI agent through the `agy-acp` ACP adapter to produce chat responses, run tools, and deliver generated files.

*The alpaca was scientifically described by Carl Linnaeus in his System of Nature (1758) under the Latin name Camelus pacos.*

![scr1](https://user-images.githubusercontent.com/8275793/231939658-5b52f5c3-2dba-4313-9756-4d8b16d14627.jpg)

## Features

- **AI-Powered Chat**: Responds to mentions (e.g., "pacos", "пакос") or direct messages by driving the agy agent over ACP. Each chat gets its own working directory and persona steering file (`GEMINI.md`).
- **Image Generation**: Generate or modify images via the `!drawx <prompt>` command. An optional source image can be supplied with the command or by replying to a message that contains one.
- **File Delivery**: The agent can return generated files (images, documents, etc.) by moving them into a per-turn output directory via the filemcp MCP server, which the bot then forwards back to the user.
- **Chat Management**:
    - **Reset History**: Users can clear the agent's session for a specific chat with the `!resetx` command.
- **Language Identification**: Detects the language of incoming messages to tailor responses (using `NTextCat` with `Core14.profile.xml`).
- **Sandboxed Execution**: An enforced agy permission policy (written at startup) denies all shell commands and restricts the agent's file access to its per-chat workspace and the brain staging dir; file delivery is handled by the filemcp MCP server, not the shell. The real isolation boundary is the container (non-root user, restricted volume).
- **Asynchronous Processing**: Handles incoming Telegram updates and agent interactions asynchronously using a background task queue to ensure responsiveness.

## Core Technologies

- **Framework**: .NET (Worker Service)
- **Telegram API**: [Telegram.Bot](https://github.com/TelegramBots/telegram.bot) library
- **AI Agent**: agy ([Google Antigravity CLI](https://antigravity.google/docs/cli-using)), driven via the `agy-acp` [ACP adapter](https://github.com/openabdev/openab)
- **Logging**: NLog (configured via `nlog.config`)
- **Configuration**: Standard .NET configuration (e.g., `appsettings.json`, environment variables)
- **Language Detection**: `NTextCat`

## Configuration

The bot reads its settings from environment variables or an `appsettings.json` file under the `Pacos` section.

**Required:**

- `TelegramBotApiKey`: Your Telegram Bot API token.
- `AllowedChatIds`: An array of Telegram chat IDs where the bot is permitted to operate.
- `ChatModel`: The model name written into the agy permission policy (e.g. `Gemini 3.5 Flash (High)`).

**Optional:**

- `AgyAcpCommand`: Executable used to spawn the agy-acp adapter (default: `agy-acp`).
- `AgyAcpArgs`: Extra command-line arguments passed to the agy-acp process.
- `WorkingDirectoryRoot`: Root directory under which per-chat working directories are created (default: a folder under the system temp directory).
- `AgyExtraArgs`: Extra arguments forwarded to every underlying `agy` invocation (via `AGY_EXTRA_ARGS`).
- `GeminiApiKey`: Optional Gemini API key passed to the agy subprocess for non-interactive auth. When empty, agy relies on its own persisted OAuth credentials (e.g. `~/.gemini`).
- `PromptTimeoutSeconds`: Hard timeout for a single prompt round-trip to agy-acp (default: `300`).
- `AgyCommandRuleMode`: Which set of agy command-permission rules to write (`denyall` (default) or `off`).

## Setup and Running

1.  Ensure you have the .NET SDK and the `agy` / `agy-acp` executables available on `PATH`.
2.  Configure the required settings (see **Configuration** section).
3.  Create `Core14.profile.xml` (for NTextCat language identification) in the application's root directory.
4.  Run the application:
    ```bash
    dotnet run
    ```

When running in Docker, the agy state directory (`/home/agent/.gemini`) should be backed by a named volume so the agent's OAuth credentials and state persist across deployments (see the deploy workflow).

## Bot Commands

- `pacos, <message>`: Engage in a conversation with the agent.
- `!drawx <prompt>`: Generate an image based on the provided text prompt (optionally using an attached or replied-to image as the source).
- `!resetx`: Reset the agent's session for the current chat.
