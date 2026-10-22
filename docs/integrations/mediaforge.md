# Native MediaForge provider

MediaForge replaces Seerr as the request/discovery provider inside Enhanced. There is no separate MediaForge panel. Enhanced's existing Jellyfin search results, cards, media detail modals, movie and season request buttons, discovery pages, Requests/download tracking, calendar and issue reporting use MediaForge through a server-side adapter.

## Setup

1. Update MediaForge to the native Enhanced adapter build and install this fork's DLL for your Jellyfin version (`jf10` for 10.11; `jf12` for 12).
2. In Enhanced's MediaForge settings tab, enable **MediaForge** and enter its server URL reachable from Jellyfin, including any base path. Selecting MediaForge routes the existing Enhanced components to MediaForge; Seerr is no longer contacted for those views.
3. Import and enable the matching Jellyfin accounts in MediaForge. Its configured Jellyfin URL must point to this server. Users reuse their current Jellyfin session, with no second login.
4. Enable the normal Enhanced features you want: search results, More Info modals, Recommendations, Requests and Calendar. Their existing per-user preferences continue to apply.
5. For automatic next-season/movie requests and watchlist synchronization, generate a dedicated random secret, set `JELLYFIN_ENHANCED_KEY` on MediaForge, and paste the same value into **MediaForge background integration key** in the plugin settings. Browser requests do not use this key. Keep existing automatic-request feature switches as desired.

## Behavior

TMDB discovery data is translated into the shapes Enhanced already renders. MediaForge library IDs, statuses, Jellyfin item IDs and verified season choices are attached to those cards. Requests go through MediaForge's normal server-side sorting, language requirements, download selection and import pipeline. Season selections are explicit; requesting a complete series expands the verified non-special season catalogue.

The existing Requests page shows tracked MediaForge requests, their real statuses and transfers. Issues are stored in MediaForge and visible to their author and administrators. Discovery watchlists are stored per account. Automatic playback requests and the library watchlist monitor use a dedicated service credential restricted to the integration route.

MediaForge processes accepted requests automatically, so there is no fake pending approval queue. Its quotas are unlimited. Advanced quality choices map to Default, 1080p or 2160p, and root-folder choices select configured MediaForge categories. Seerr/Sonarr/Radarr external links are not shown for the MediaForge provider. Calendar data comes from tracked MediaForge episodes. These are MediaForge's backend semantics; the display and interactions remain Enhanced's native components.

## Authentication

Browser calls forward the current Jellyfin token to the configured MediaForge server. MediaForge verifies `/Users/Me` on its own configured Jellyfin server on every request. Revoked/expired sessions and inactive local accounts are rejected immediately, with local MediaForge roles preserved. Account resolution follows the current username-based import model; renaming a Jellyfin account requires updating its MediaForge account.

Background jobs use the optional shared integration key. That key does not authenticate against ordinary MediaForge API routes. User-scoped background mutations require an active imported user ID; jobs cannot mint sessions or promote roles. User-directory lookups use MediaForge's configured Jellyfin API credential. Neither passwords nor browser session material is read from a database.

Use trusted server-to-server networking or HTTPS. The adapter does not follow redirects with credentials. The old custom MediaForge panel and its routes have been removed.

## Build

```sh
npm ci
npm run test:frontend
dotnet test tests/backend/JE.Tests.csproj -p:JellyfinTarget=jf12
dotnet build Jellyfin.Plugin.JellyfinEnhanced -c Release -p:JellyfinTarget=jf10 -o artifacts/jf10
dotnet build Jellyfin.Plugin.JellyfinEnhanced -c Release -p:JellyfinTarget=jf12 -o artifacts/jf12
```
