# Cardex Mobile — Pokémon TCG Collection Companion

> An Android companion app for [Cardex](https://github.com/Darumacho/Cardex-Companion), the Windows
> desktop Pokémon TCG collection manager. Browse your catalogue, track your collection, and keep it
> in sync with the desktop app.

---

## Features

### Catalogue & Navigation
- Full Pokémon TCG catalogue loaded from [pokemontcg.io](https://pokemontcg.io), with a TCGdex
  fallback for sets and cards missing from the primary source
- Three main tabs: **Sets**, **Search**, **Collection**
- Sets grouped by series, with collapsible groups that remember their expanded/collapsed state
  between launches
- Set logo shown in the title bar of the set detail page
- Colored border highlight on sets that contain at least one owned card

### Collection Management
- Mark cards as owned and track quantity per card
- Mark cards as wanted or excluded from completion tracking
- Favorite sets, shown in their own section on the Collection page
- Collection page sections — Favorites, My Collection, Wanted, Duplicates — each collapsible and
  persisted between launches
- List view and binder view, toggled per set:
  - List view shows full card details (name, number, rarity, prices); tapping the card image opens
    a full-screen HD view
  - Binder view shows a compact grid of card thumbnails; tapping a card toggles owned, and a long
    press opens the HD view
- Clickable Cardmarket and TCGPlayer price links on each card

### Custom Tags
- Create colored tags (Tags page) and assign one per card from the set detail list view
- Tagged cards are grouped into their own sections on the Collection page, below Duplicates

### Search
- Global search by card name or number across the entire catalogue

### Achievements
- 35 unlockable achievements (set completions, collection milestones, tagging, want-listing, and a
  few hidden ones), shown with progress and unlock dates on the Achievements page
- Toast notification when an achievement unlocks

### Collection Sync
- Links to the desktop app through a free API key from [cardex-api.dev](https://cardex-api.dev)
- Manual sync pushes your collection, wants, exclusions, favorites, tags, and achievements to the
  server, tagged with the device it came from
- If the other device synced more recently, syncing warns before overwriting those changes
- On launch, if the desktop app has synced more recently, the newer collection is pulled in
  automatically, no action needed
- Reachable from Settings or directly from the navigation menu

### Settings
Reached through the navigation menu:
- Show/hide the My Collection section
- Collection border color picker
- Collection sync: API key entry, manual sync button, last-synced status
- App update: available version and install button, shown when a newer release exists

### Automatic Updates
- Checks for new releases on GitHub at startup, with a toast notification if one is found
- Settings shows an install button that downloads the update and hands it to the system installer

---

## Screenshots

> *(coming soon)*

---

## Installation

Cardex Mobile is not distributed through the Play Store — it is sideloaded from a GitHub release.

1. Download `CardexMobile.apk` from the
   [Releases](https://github.com/Darumacho/CardexMobile/releases) page on your Android device
2. Open the downloaded file; the first time, Android will ask you to allow installs from this
   source — accept, then continue the install
3. On first launch, the catalogue is downloaded and indexed automatically

> **Requirements:** Android 5.0 (API 21) or later

---

## Configuration

A `settings.json` file is created automatically in the app's private data directory on first
launch.

```json
{
  "ApiKey": "your-pokemontcg-io-api-key",
  "CollectionSyncApiKey": "your-cardex-api-key"
}
```

- `ApiKey` — a free key from [pokemontcg.io](https://pokemontcg.io) raises catalogue rate limits;
  the app works without one in limited mode
- `CollectionSyncApiKey` — a free key from [cardex-api.dev](https://cardex-api.dev), needed only to
  sync with the desktop app

Both can be entered directly from the Settings page instead of editing the file by hand.

---

## Technical Overview

| Layer | Technology |
|---|---|
| UI | .NET MAUI (net9.0-android), XAML, Shell navigation |
| Pattern | MVVM — CommunityToolkit.Mvvm |
| Database | SQLite via Entity Framework Core |
| Catalogue API | [pokemontcg.io](https://pokemontcg.io), with a TCGdex fallback |
| Sync API | [cardex-api.dev](https://cardex-api.dev) |
| Distribution | Sideloaded signed APK, self-updating via GitHub releases |

### Project Structure

```
CardexMobile/
├── CardexCore/
│   ├── Models/              # Database entities and API models (mirrors the desktop schema)
│   └── Services/
│       ├── AppSettings            # Configuration loader (API keys, persisted UI state, ...)
│       ├── PokemonTcgService      # pokemontcg.io API client
│       ├── AchievementService     # Achievement definitions and unlock checks
│       └── CollectionSyncService  # Builds/applies the sync JSON blob, talks to cardex-api.dev
├── Data/
│   ├── MobileAppDbContext   # EF Core DbContext (Android-specific database path)
│   ├── DbInitializer        # Background startup: schema migration, catalogue seed, sync pull, update check
│   ├── TcgdexSeeder         # TCGdex fallback catalogue seeding
│   ├── AchievementChecks    # Achievement condition checks (mobile-only)
│   └── ExpandedStateStore   # Persisted collapsed/expanded state for list sections
├── Services/
│   └── MobileUpdateService  # Update check and APK download/install
├── ViewModels/
│   ├── SetsPageViewModel, SearchPageViewModel, SetDetailPageViewModel
│   ├── CollectionPageViewModel, TagsPageViewModel, AchievementsPageViewModel
│   └── SettingsPageViewModel
├── Pages/
│   ├── SetsPage, SearchPage, SetDetailPage, CardZoomPage
│   ├── CollectionPage, TagsPage, AchievementsPage
│   └── SettingsPage
├── AppShell.xaml             # Navigation shell (bottom tabs + flyout menu)
└── Platforms/Android/        # Manifest, FileProvider configuration for update installs
```

---

## Local Data

The SQLite database lives in the app's private storage and contains:

| Table | Content |
|---|---|
| `CachedSets` | Metadata for all sets |
| `CachedCards` | Cards with market prices and URLs |
| `OwnedCards` | Personal collection with quantities |
| `WantedCards` | Want list |
| `FavoriteSets` | Starred sets |
| `ExcludedCards` | Cards excluded from completion tracking |
| `Tags` / `CardTags` | Custom colored tags and their card assignments |
| `UnlockedAchievements` | Achievement unlock records |

---

## Development

```bash
# Build and install onto a connected device or running emulator
dotnet build CardexMobile.csproj -f net9.0-android -t:Install

# Publish a release APK
dotnet publish CardexMobile.csproj -f net9.0-android -c Release
```

### Releasing an update

Releases are signed with a dedicated keystore kept outside the repository. Every release must be
signed with the **same** keystore — Android refuses to install an update signed with a different
one, which would force users to uninstall and lose any unsynced local data.

```bash
dotnet publish CardexMobile.csproj -f net9.0-android -c Release \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=<path-to-keystore> \
  -p:AndroidSigningKeyAlias=<alias> \
  -p:AndroidSigningKeyPass=<password> \
  -p:AndroidSigningStorePass=<password>
```

Bump `ApplicationDisplayVersion` and `ApplicationVersion` in `CardexMobile.csproj`, then publish a
GitHub release tagged with the new version (a leading `v` is optional) and attach the signed APK
as `CardexMobile.apk` — the exact name the in-app updater looks for.
