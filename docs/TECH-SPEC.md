# RadioClassic macOS - Technical Specification
Implement the Mac version in Swift/SwiftUI against this spec. Everything below is
distilled from the working Windows implementation (see `main` branch).

## 1. Station directory (radio-browser.info)
- Base: pick a server from `https://all.api.radio-browser.info/json/servers`, use it for all calls
- Full sync: `GET /json/stations?hidebroken=true&order=votes&reverse=true` (paged via limit/offset)
  Fields used: stationuuid, name, url_resolved, country, genre=tags, language, codec, bitrate, votes, clickcount, favicon
- Cache everything in SQLite locally; sync deltas on launch; offline browsing must work
- Search/filter is LOCAL over cached rows: substring match on name+tags+country+language, plus dropdown filters
- Register plays: `GET /json/url/{stationuuid}` when playback starts (directory ranking courtesy)

## 2. Playback
- AVPlayer handles MP3/AAC/HLS natively. For OGG/Opus/FLAC use BASS for macOS (libbass.dylib +
  bassopus/bassflac dylibs from un4seen.com) or VLCKit. Decide after a codec survey of top stations.
- Buffer target ~4-8s; show codec+bitrate and buffer health in the status bar
- WATCHDOG (important, proven needed on Windows): if audio level stays 0 or the stream stalls
  ~8s while playing, reconnect once; if it persists, show honest error - never fake "Playing".
  Stations really do broadcast digital silence or stall without disconnecting.

## 3. ICY/stream metadata + track splitting (recording)
- MP3 streams: `Icy-MetaData` header, metadata interleaved every `metaint` bytes;
  block = length byte * 16, contains `StreamTitle='Artist - Title';`
- AAC/ADTS: same ICY scheme when served over HTTP shoutcast-style
- Parse StreamTitle changes -> window title "Artist - Title" AND recording split points
- Recording: capture compressed stream bytes as-is (no re-encode). Buffer 64KB writes,
  flush periodically so file growth is visible (Windows lesson: explorers confuse users otherwise)
- On title change: close current file, start new file named `Artist - Title.mp3/.aac` (sanitized)
- Folder: `Recordings/<StationName>/`; filenames deduplicated with (2), (3) suffixes
- Scheduled recordings: start/end times, weekday recurrence, auto-stop

## 4. Data layer (SQLite schema essentials)
- stations(stationuuid PK, name, url, country, tags, language, codec, bitrate, votes, clickcount, favicon, is_custom, favorite, last_played, play_count)
- recordings(id, station, path, started, duration, bytes, title)
- settings as key-value JSON file, not DB

## 5. UI requirements
- Main window ~560x500: search box + filter dropdowns (view/country/genre/language/codec/bitrate),
  sortable multi-column list, transport bar (play/pause/stop/prev/next, red record button),
  spectrum visualizer, status bar (codec+bitrate right, state+elapsed center, buffer left)
- Column headers must be clickable sort toggles. Search filters live as you type (debounced ~200ms)
- Combo boxes: editable search + genre boxes MUST accept typing even with empty item lists
  (Windows bug lesson: never disable input when Items is empty)
- Compact mini-player mode; menu bar (tray equivalent) quick controls
- 6 themes using Shared/themes.json palettes; EN + FA localization from Shared/strings.*.json
- Persian = full RTL layout mirroring
- Keyboard/media keys: play/pause/stop/next/prev, volume
- Global hotkeys on macOS need accessibility permission - use NSEvent global monitor or HotKey pod

## 6. Windows-proven behaviors worth copying exactly
- Single instance; close-to-menu-bar keeps music playing
- Favorites/Recently played/My stations quick views; custom stations by URL
- Import/export station lists (JSON)
- Window title shows live "Artist - Title" while playing
- Honest errors in EN+FA: station silent / stream stalled / unreachable

## 7. Distribution
- .app bundle signed+notarized for direct download, plus optional App Store later
- Keep the repo rules: no machine metadata, author Farhadzadeai only, releases via GitHub assets
