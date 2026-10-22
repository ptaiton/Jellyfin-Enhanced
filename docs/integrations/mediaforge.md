# MediaForge integration (fork)

This fork adds a separate **MediaForge** button to the Jellyfin web header. It works independently of Seerr, which can remain configured alongside it.

## Setup

1. Build this fork for your Jellyfin version, or download the matching build from the **MediaForge builds** GitHub Actions workflow. Use `jf10` for Jellyfin 10.11, `jf12` for Jellyfin 12.
2. Replace the existing Enhanced DLL in Jellyfin's plugin directory with this build while Jellyfin is stopped, then restart Jellyfin and refresh the browser. Keep a copy of the original DLL to revert.
3. In **Dashboard → Plugins → Jellyfin Enhanced → Seerr**, enable **MediaForge** and enter its URL as reachable by the Jellyfin server, for example `http://mediaforge:8080`. Include a reverse proxy base path if applicable. Save.
4. Ensure your Jellyfin accounts are imported and enabled in MediaForge and that MediaForge points to this Jellyfin server. Open **MediaForge** in the header: the current Jellyfin session is reused automatically, with no additional login.
5. Search for a film or series, select the result and, for series, choose the seasons to request. Quality and audio/subtitle requirements use MediaForge's configured defaults.

The tracked requests list shows MediaForge library statuses, supports pagination and has a Refresh button. Film status is also shown on the request button. Available or already-requested seasons are marked and cannot be selected again.

## Accounts and transport

The panel uses the current Jellyfin user's access token and never asks for a MediaForge password or creates a separate MediaForge session. Every proxied request sends this token to the administrator-configured MediaForge URL in `X-MediaForge-Jellyfin-Token`. MediaForge validates it against `/Users/Me` on its own configured Jellyfin server on every request. A revoked/expired Jellyfin token stops working immediately on the next request; there is no authentication cache or independent cookie that outlives it. Changing Jellyfin accounts closes the panel and discards its contents.

MediaForge maps the verified Jellyfin username to an existing, active imported account, matching its existing import/login model. Unknown or locally disabled accounts are rejected. Local MediaForge roles are preserved: a Jellyfin administrator is not automatically promoted by this integration. Username changes require importing/updating the corresponding account. This currently uses usernames, not a persistent Jellyfin ID mapping.

The proxy accepts only fixed search, detail, defaults, library and request endpoints. It does not expose administration, accept a caller-supplied upstream URL, follow redirects or forward browser cookies. The browser does not need cross-origin cookies or a public MediaForge address. Use HTTPS when either server-to-server connection crosses an untrusted network. Update MediaForge alongside this plugin: its authentication extractor must support `X-MediaForge-Jellyfin-Token`.

## Scope

The panel supports French and English and requests through MediaForge's existing API, including verified season choices. It is not wired into Seerr's discovery rows, automatic requests, quotas, parental filtering or download monitor. MediaForge's library visibility rules apply. Status updates are refreshed manually; no download percentage is shown. This targets the current MediaForge API (`/api/discover/details/{format}/{id}`, `/api/discover/config`, `/api/discover/request` and `/api/library`). Older MediaForge builds may need updating.

## Build and test

```sh
npm ci
npm run test:frontend
dotnet build Jellyfin.Plugin.JellyfinEnhanced -p:JellyfinTarget=jf12 -o artifacts/jf12
dotnet build Jellyfin.Plugin.JellyfinEnhanced -p:JellyfinTarget=jf10 -o artifacts/jf10
dotnet test tests/backend/JE.Tests.csproj -p:JellyfinTarget=jf12 --filter FullyQualifiedName~ApiMediaForgeTests
```

Use Node 22.12 or newer and .NET SDK 10. The two DLLs are built for distinct Jellyfin versions and are not interchangeable.
