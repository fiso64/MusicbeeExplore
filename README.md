# MusicbeeExplore

MusicbeeExplore is a plugin for MusicBee that allows users to browse MusicBrainz or Discogs in the player. It is intended to be a replacement for the "More Albums" section in the music explorer view by fetching albums from more comprehensive sources and allowing you to play the songs in full, rather than just a preview. Implemented in a very hacky way since the MusicBee API does not support modifying the music explorer view (please see the known issues section).

## Features

- Query Discogs or MusicBrainz for albums by an artist
- Play songs in MusicBee by downloading them from YouTube
- Fetch popular tracks for an artist from Last.fm
- Get similar album recommendations
- Seamless-ish integration with MusicBee's interface

## How It Works

The plugin integrates with MusicBee's interface and allows users to query external music databases (MusicBrainz and Discogs) for artist discographies and album information.

1. When a user searches for an artist, the plugin queries the selected database (MusicBrainz or Discogs) and retrieves the discography information.

2. The plugin creates dummy files for every album in the artist's discography. These dummy files contain metadata but no actual audio content, and simply serve as "links" to load the album in MusicBee.

3. When a user attempts to play a dummy file, the plugin intercepts the playback:
   - For unloaded albums, it fetches the track list and creates dummy files for all tracks in the album.
   - For unloaded tracks, it uses yt-dlp to search for and download the audio from YouTube.

4. The downloaded audio is saved in the opus format and replaces the dummy file. The file is then re-queued for playback.

5. If configured, the plugin can stream the audio directly without downloading (although it takes about the same amount of time until playback starts, at least for minute-long songs).

## Requirements

- **yt-dlp**: This tool is required for searching and downloading audio from YouTube. Make sure it's installed and available in your system PATH.
- **ffmpeg**: yt-dlp requires ffmpeg for audio conversion. Ensure it's installed and accessible to yt-dlp.

## Setup

1. Download the zip file from the releases page.
2. Extract `mb_MusicBeeExplore.dll` to the MusicBee plugins folder.
3. Add exclusions to all filters in MusicBee to avoid displaying the plugin's cached items:
   - Add a filter condition: comment does not contain `巽 ` (note the space). This is the identifier of all files generated by this plugin.
4. Configure the plugin settings in MusicBee:
   - Go to Edit > Preferences > Plugins > MusicBeeExplore > Configure.

### Additional Configuration

- **Discogs**: If you plan to use Discogs, you need to obtain a personal access token:
  1. Create an account on [Discogs](https://www.discogs.com/) if you don't have one.
  2. Go to [https://www.discogs.com/settings/developers](https://www.discogs.com/settings/developers)
  3. Click on "Generate new token"
  4. Copy the generated token and paste it into the "Discogs Token" field in the plugin settings.

- **Last.fm**: To use Last.fm features (like fetching popular tracks or similar albums), you need an API key:
  1. Create a Last.fm account if you don't have one.
  2. Go to [https://www.last.fm/api/account/create](https://www.last.fm/api/account/create) to create an API account.
  3. Fill out the form (you can use "MusicBeeExplore" as the application name).
  4. After submitting, you'll receive an API key. Copy this key and paste it into the "Last.fm API Key" field in the plugin settings.

### Optimizing the Results View

To enhance your browsing experience, it's recommended to create a custom view for the plugin results:

1. Create a virtual tag:
   - Go to Edit > Preferences > Tags (1)
   - In the "Virtual Tags" section, click "Add"
   - Name the tag "MbeHeader", and set its value to: 
     ```
     $If($Contains(<Comment>,Appearance),"More Albums: Appears On","More Albums")
     ```

2. Create a custom view for the plugin results:
   - Go to the header menu > Custom Views > Copy Current View Settings To...
   - In the dialog that appears, enter a name for your new view, such as "MusicBeeExplore Results"

3. Configure the new view:
   - Set it to be grouped by the "MbeHeader" tag
   - Sort by year in descending order
   - Set the view to "Album Covers"

## Usage

The plugin adds several commands:

1. Discogs Query: Search for albums by the selected or search box artist on Discogs
2. MusicBrainz Query: Search for albums by the selected or search box artist on MusicBrainz
3. Load Selected Albums: Load the tracks of the selected albums. Alternatively, you can just double-click on an album in the results.
4. Toggle Cached Albums: Show or hide cached albums
5. Get Popular Tracks: Fetch popular tracks for the selected artist from Last.fm
6. Get Similar Albums: Find similar albums for the selected album

These commands can be found in the menu under "Tools > MusicBeeExplore" or as a hotkey.

Note that you must either select a track or album, or enter a search query in the search box, in order to query an artist. Unfortunately, MusicBee does not provide a way to get the currently visible artist in the music explorer.

### Search Syntax

The plugin supports additional search syntax for both MusicBrainz and Discogs queries:

#### MusicBrainz:

- `>query`: Retrieve more releases, including "appears on" releases
- `l:query`: Search for a label instead of an artist
- `"query"`: Perform an exact match search

#### Discogs:

- `>query`: Retrieve more releases, including "appears on" releases
- `>>query`: Retrieve even more releases (may contain junk)
- `l:query`: Search for a label instead of an artist
- `"query"`: Perform an exact match search

## Known Issues

- MusicBee doesn't handle streams all that well, so when the on-play action is set to "Stream with MusicBee", there are a few issues. The most notable is that seeking basically does not work at all.
- Sometimes the view is not updated when album tracks are loaded. To fix this, re-query the artist.
- Scrobbling issues: Artist names are prefixed with an identifier, affecting Last.fm scrobbling. Add the cache folder to excluded locations in the Last.fm plugin settings.
- Wavebar: The wavebar is not updated after dummy tracks are downloaded. The ordinary progress bar works fine.
