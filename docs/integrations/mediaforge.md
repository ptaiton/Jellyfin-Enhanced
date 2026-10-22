# MediaForge integration (fork)

This fork adds a separate **MediaForge** button to the Jellyfin web header. It works independently of Seerr, which can remain configured alongside it.

## Setup

1. Build this fork for your Jellyfin version, or download the matching build from the **MediaForge builds** GitHub Actions workflow. Use `jf10` for Jellyfin 10.11, `jf12` for Jellyfin 12.
2. Replace the existing Enhanced DLL in Jellyfin's plugin directory with this build while Jellyfin is stopped, then restart Jellyfin and refresh the browser. Keep a copy of the original DLL to revert.
3. In **Dashboard → Plugins → Jellyfin Enhanced → Seerr**, enable **MediaForge** and enter its URL as reachable by the Jellyfin server, for example `http://mediaforge:8080`. Include a reverse proxy base path if applicable. Save.
4. Open **MediaForge** in the header and sign in using your own MediaForge credentials. Existing Jellyfin credentials work if that MediaForge instance is configured to authenticate against Jellyfin.
5. Search for a film or series, select the result and, for series, choose the seasons to request. Quality and audio/subtitle requirements use MediaForge's configured defaults.

The tracked requests list shows MediaForge library statuses, supports pagination and has a Refresh button. Film status is also shown on the request button. Available or already-requested seasons are marked and cannot be selected again.

## Accounts and transport

Each person signs in separately. MediaForge validates the account and its active state through its normal API. No shared administrator credential or automatic account mapping is introduced. The password is only used for the login request and is never saved in plugin settings or browser storage. The MediaForge session stays in browser memory and is discarded on Jellyfin account changes or page reload. Close the panel to keep the current session; use Sign out to discard it.

Requests go through Jellyfin to MediaForge, so the browser does not need cross-origin cookie access or a public MediaForge address. The proxy forwards only a fixed set of search, detail, defaults, library, request and logout endpoints. It does not expose administration or accept an arbitrary upstream URL, and it does not follow redirects or forward Jellyfin credentials to MediaForge. Use HTTPS when the connection between the two servers crosses an untrusted network.

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
