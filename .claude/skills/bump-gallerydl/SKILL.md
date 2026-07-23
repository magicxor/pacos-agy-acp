---
name: bump-gallerydl
description: >-
  Bump/update the GalleryDl version in the Pacos project. Use whenever asked to
  upgrade, downgrade, or pin GalleryDl, the GalleryDl MCP server
  (GALLERYDL_MCP_VERSION / GalleryDl.McpServer), or the gallerydl-webapi
  container image. Lists every place the version must change so the two halves
  stay in sync.
---

# Bump GalleryDl

GalleryDl is one mechanism made of **two halves that MUST run the same version**:

1. **MCP server binary** — `GalleryDl.McpServer`, baked into the `pacos` image at
   build time from the [`magicxor/GalleryDlManager`](https://github.com/magicxor/GalleryDlManager)
   releases. This is the local half agy spawns.
2. **`gallerydl-webapi` backend** — the remote half, a separate container
   (`ghcr.io/magicxor/gallerydl-webapi`) the MCP server talks to over the compose
   network.

If these drift apart the MCP server and its backend can become incompatible, so
**always bump all three locations below to the same version in one change.**

## Places to edit (set all to the new version `X.Y.Z`)

| # | File | What to change |
|---|------|----------------|
| 1 | `Pacos/Dockerfile` | `ENV GALLERYDL_MCP_VERSION=...` — the MCP server binary. This single value feeds the release tag **and** the tarball filename in the `curl` download URL, so one edit covers both. |
| 2 | `docker-compose.deploy.yml` | `image: ghcr.io/magicxor/gallerydl-webapi:...` — the **production** backend (service `gallerydl-webapi`). Keep it pinned to an explicit version, never `latest`. |
| 3 | `docker-compose.yml` | `image: ghcr.io/magicxor/gallerydl-webapi:...` — the **local dev** backend. Pin it too; a floating `latest` here can drift ahead of the binary pinned in the Dockerfile. |

Search anchors if line numbers have moved: `GALLERYDL_MCP_VERSION=` and
`ghcr.io/magicxor/gallerydl-webapi:`.

## NOT version locations (do not touch when bumping)

These mention GalleryDl but carry no version — leave them alone:

- `Pacos/Models/Options/PacosOptions.cs` — the `gallerydl` MCP server definition
  (DLL path `/opt/gallerydl-mcp/GalleryDl.McpServer.dll`, `BaseUrl`, etc.).
- `Pacos.Tests.Unit/AgyMcpConfigTests.cs` — asserts the config shape/paths.
- `Pacos/Constants/Const.cs` — the system-prompt text describing the MCP tools.
- `.github/workflows/deploy_latest.yml` — the `GALLERYDL_CONFIG` credentials
  heredoc (gallery-dl auth, unrelated to versions).

## Verify

1. Confirm both upstream tags exist for the new version — they live in **different
   repos**, so parity is a project convention, not guaranteed. If a tag is
   missing, `docker compose pull` will fail on deploy.
   - MCP server: a release `X.Y.Z` at `magicxor/GalleryDlManager` with asset
     `GalleryDl.McpServer-X.Y.Z.tar.gz`.
   - Backend: `docker manifest inspect ghcr.io/magicxor/gallerydl-webapi:X.Y.Z`
2. Commit all three files together in a single commit, e.g.
   `Bump GalleryDl to X.Y.Z`, so the bump can be pushed/reverted as one unit.
