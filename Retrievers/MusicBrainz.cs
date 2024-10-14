using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Threading;
using System.ComponentModel;
using MusicBeePlugin.Models;

namespace MusicBeePlugin.Retrievers
{
    public class MusicBrainzRetriever : IDiscographyRetriever, IAlbumRetriever
    {
        MusicBrainz _api;

        public MusicBrainzRetriever(Config config)
        {
            _api = new MusicBrainz();
        }

        public async Task<(string entityName, List<Release> releases)> GetReleasesAsync(string query, Action<string> statusChange, CancellationToken ct)
        {
            var releases = new List<Release>();
            var entity = MusicBrainz.Entity.Artist;
            bool exact = false;
            bool retrieveAll = false;

            if (query.ToLower().StartsWith(">>"))
            {
                retrieveAll = true;
                query = query.Substring(2);
            }

            if (query.ToLower().StartsWith("l:"))
            {
                entity = MusicBrainz.Entity.Label;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("a:"))
            {
                entity = MusicBrainz.Entity.Artist;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("ar:"))
            {
                entity = MusicBrainz.Entity.Artist;
                query = query.Substring(3);
            }

            if (query.StartsWith("\"") && query.EndsWith("\""))
            {
                exact = true;
                query = query.Substring(1, query.Length - 2);
            }

            statusChange($"Querying {entity}: {query}");

            var entities = await _api.QueryEntities(entity, query, ct, exact ? 20 : 0);

            if (entities.Count == 0)
            {
                statusChange($"No results found for {entity}: {query}");
                return (null, new List<Release>());
            }

            string entityName;
            string entityId;

            if (!exact)
            {
                entityName = entities[0].name;
                entityId = entities[0].id;
            }
            else
            {
                var exactMatch = entities.FirstOrDefault(e => e.name.Equals(query, StringComparison.OrdinalIgnoreCase));
                if (exactMatch == default)
                {
                    statusChange($"No exact match found for {entity}: {query}");
                    return (null, new List<Models.Release>());
                }

                entityName = exactMatch.name;
                entityId = exactMatch.id;
            }

            statusChange($"Getting releases for {entity}: {entityName}");

            if (entity == MusicBrainz.Entity.Artist)
            {
                var res = await _api.GetReleaseGroupsByArtist(entityId, entityName, ct);
                releases = res.Select(r => new Release
                {
                    Id = r.Id,
                    Title = r.Name,
                    Date = r.Date,
                    Thumb = r.CoverArtUrl,
                    Artist = r.Artist,
                    Source = Retriever.MusicBrainz,
                    AdditionalData = new Dictionary<string, string>
                    {
                        { "isGroup", "true" }
                    }
                }).ToList();

                if (retrieveAll)
                {
                    var appearsOnReleases = await _api.GetAppearsOnReleasesByArtist(entityId, ct);
                    releases.AddRange(appearsOnReleases.Select(r => new Release
                    {
                        Id = r.Id,
                        Title = r.Name,
                        Date = r.Date,
                        Thumb = r.CoverArtUrl,
                        Artist = r.Artist,
                        Source = Retriever.MusicBrainz,
                        AppearanceOnly = true,
                        AdditionalData = new Dictionary<string, string>
                        {
                            { "isGroup", "false" }
                        }
                    }));
                }
            }
            else if (entity == MusicBrainz.Entity.Label)
            {
                var res = await _api.GetReleasesByLabel(entityId, ct, uniqueNamesOnly: true);
                releases = res.Select(r => new Release
                {
                    Id = r.Id,
                    Title = r.Name,
                    Date = r.Date,
                    Thumb = r.CoverArtUrl,
                    Artist = r.Artist,
                    Source = Retriever.MusicBrainz,
                    AdditionalData = new Dictionary<string, string>
                    {
                        { "isGroup", "false" }
                    }
                }).ToList();
            }

            return (entityName, releases);
        }

        public async Task<List<Track>> GetReleaseTracksAsync(CommentData data)
        {
            string id = bool.Parse(data.AdditionalData["isGroup"]) ? await _api.GetBestRelease(data.Id) : data.Id;

            var songs = await _api.GetReleaseSongs(id);

            var res = songs.Select(s => new Track
            {
                Id = s.Id,
                Title = s.Title,
                Artist = s.Artist,
                Length = s.Length,
                TrackPosition = $"{s.TrackNumber}/{s.TotalTracks}",
                DiscPosition = $"{s.DiscNumber}/{s.TotalDiscs}",
                Source = Retriever.MusicBrainz
            }).ToList();

            return res;
        }
    }

    public class MusicBrainz
    {
        public enum Entity
        {
            Area,
            Artist,
            Event,
            Genre,
            Instrument,
            Label,
            Place,
            Recording,
            Release,
            ReleaseGroup,
            Series,
            Work,
            Url
        }

        private readonly HttpClient _httpClient;

        public MusicBrainz()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TestApp", "1.0"));
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(mailto:test@mail.com)"));
        }

        private string ToKebab(string input)
        {
            return string.Concat(input.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x : x.ToString())).ToLower();
        }

        public async Task<List<(string name, string id)>> QueryEntities(Entity entityType, string query, CancellationToken cancellationToken, int limit)
        {
            var entityName = ToKebab(entityType.ToString());
            var searchUrl = $"https://musicbrainz.org/ws/2/{entityName}?query={Uri.EscapeDataString(query)}&limit={limit}&fmt=json";

            var response = await _httpClient.GetAsync(searchUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var data = JObject.Parse(await response.Content.ReadAsStringAsync());

            var entities = data[entityName + "s"]
                .Select(entity => (
                    name: entity["name"]?.ToString() ?? entity["title"].ToString(),
                    id: entity["id"].ToString()
                ))
                .ToList();

            return entities;
        }

        public async Task<List<Release>> GetReleasesByLabel(string labelId, CancellationToken cancellationToken, bool uniqueNamesOnly = true)
        {
            var releasesData = new List<JObject>();
            var offset = 0;
            const int limit = 100;
            bool hasMoreResults;

            // Step 1: Collect all releases data
            do
            {
                var releaseUrl = $"https://musicbrainz.org/ws/2/release?label={labelId}&inc=artist-credits&limit={limit}&offset={offset}&fmt=json";
                var releaseResponse = await _httpClient.GetAsync(releaseUrl, cancellationToken);
                releaseResponse.EnsureSuccessStatusCode();
                var releaseData = JObject.Parse(await releaseResponse.Content.ReadAsStringAsync());

                releasesData.AddRange(releaseData["releases"].Select(release => (JObject)release));

                hasMoreResults = releaseData["releases"].Count() == limit;
                offset += limit;

            } while (hasMoreResults);

            // Step 2: Filter unique releases if necessary, selecting best
            if (uniqueNamesOnly)
            {
                releasesData = releasesData
                    .GroupBy(release => new
                    {
                        Title = release["title"]?.ToString(),
                        Artist = release["artist-credit"]?.FirstOrDefault()?["name"]?.ToString()
                    })
                    .Select(g => g
                        .OrderByDescending(r => r["cover-art-archive"]?["front"]?.Value<bool>() == true)
                        .ThenByDescending(r => r["cover-art-archive"]?["count"]?.Value<int>() > 0)
                        .ThenByDescending(r => r["release-events"]?.FirstOrDefault()?["date"]?.ToString())
                        .FirstOrDefault())
                    .ToList();
            }

            // Step 3: Get cover art urls and construct release objects
            var releases = new List<Release>();
            var semaphore = new SemaphoreSlim(15);

            var tasks = releasesData.Select(async release =>
            {
                var releaseId = release["id"]?.ToString();
                var releaseName = release["title"]?.ToString();
                var releaseDate = release["release-events"]?.FirstOrDefault()?["date"]?.ToString();
                var artistName = release["artist-credit"]?.FirstOrDefault()?["name"]?.ToString();

                string coverArtUrl = "";
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    coverArtUrl = await GetCoverArtUrl(releaseId, Entity.Release, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }

                return new Release
                {
                    Id = releaseId,
                    Name = releaseName,
                    Date = releaseDate,
                    Artist = artistName,
                    IsReleaseGroup = false,
                    CoverArtUrl = coverArtUrl,
                };
            });

            releases.AddRange(await Task.WhenAll(tasks));

            return releases;
        }

        public async Task<List<Release>> GetAppearsOnReleasesByArtist(string artistId, CancellationToken cancellationToken)
        {
            var releases = new List<Release>();
            var offset = 0;
            const int limit = 100;
            bool hasMoreResults;

            do
            {
                var releaseUrl = $"https://musicbrainz.org/ws/2/release?track_artist={artistId}&limit={limit}&offset={offset}&inc=artist-credits&fmt=json";
                var releaseResponse = await _httpClient.GetAsync(releaseUrl, cancellationToken);
                releaseResponse.EnsureSuccessStatusCode();
                var releaseData = JObject.Parse(await releaseResponse.Content.ReadAsStringAsync());

                var semaphore = new SemaphoreSlim(15);

                var tasks = releaseData["releases"].Select(async release =>
                {
                    var releaseId = release["id"]?.ToString();
                    var releaseName = release["title"]?.ToString();
                    var releaseDate = release["date"]?.ToString();
                    var releaseArtist = release["artist-credit"]?.First?["name"]?.ToString();

                    string coverArtUrl = "";
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        coverArtUrl = await GetCoverArtUrl(releaseId, Entity.Release, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                    return new Release
                    {
                        Id = releaseId,
                        Name = releaseName,
                        Date = releaseDate,
                        CoverArtUrl = coverArtUrl,
                        Artist = releaseArtist,
                    };
                });

                releases.AddRange(await Task.WhenAll(tasks));

                hasMoreResults = releaseData["releases"].Count() == limit;
                offset += limit;

            } while (hasMoreResults);

            return releases;
        }

        public async Task<List<Release>> GetReleaseGroupsByArtist(string artistId, string artistName, CancellationToken cancellationToken)
        {
            var releases = new List<Release>();
            var offset = 0;
            const int limit = 100;
            bool hasMoreResults;

            do
            {
                var releaseUrl = $"https://musicbrainz.org/ws/2/release-group?artist={artistId}&limit={limit}&release-group-status=website-default&offset={offset}&fmt=json";
                var releaseResponse = await _httpClient.GetAsync(releaseUrl, cancellationToken);
                releaseResponse.EnsureSuccessStatusCode();
                var releaseData = JObject.Parse(await releaseResponse.Content.ReadAsStringAsync());

                var semaphore = new SemaphoreSlim(15);

                var tasks = releaseData["release-groups"].Select(async release =>
                {
                    var releaseId = release["id"]?.ToString();
                    var releaseName = release["title"]?.ToString();
                    var releaseDate = release["first-release-date"]?.ToString();
                    var primaryType = release["primary-type"]?.ToString();

                    string coverArtUrl = "";
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        coverArtUrl = await GetCoverArtUrl(releaseId, Entity.ReleaseGroup, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                    return new Release
                    {
                        Id = releaseId,
                        Name = releaseName,
                        Date = releaseDate,
                        CoverArtUrl = coverArtUrl,
                        Artist = artistName,
                        IsReleaseGroup = true,
                    };
                });

                releases.AddRange(await Task.WhenAll(tasks));

                hasMoreResults = releaseData["release-groups"].Count() == limit;
                offset += limit;

            } while (hasMoreResults);

            return releases;
        }

        private async Task<string> GetCoverArtUrl(string albumId, Entity releaseOrGroup, CancellationToken cancellationToken)
        {
            if (releaseOrGroup != Entity.Release && releaseOrGroup != Entity.ReleaseGroup)
            {
                throw new ArgumentException("Invalid entity type. Must be either Release or ReleaseGroup.");
            }

            try
            {
                var entityName = ToKebab(releaseOrGroup.ToString());
                var coverArtUrl = $"https://coverartarchive.org/{entityName}/{albumId}";

                var coverArtResponse = await _httpClient.GetAsync(coverArtUrl, cancellationToken);
                coverArtResponse.EnsureSuccessStatusCode();
                var coverArtData = JObject.Parse(await coverArtResponse.Content.ReadAsStringAsync());

                var coverArtImageUrl = coverArtData["images"]?
                    .OrderByDescending(image => image["front"]?.Value<bool>() == true)
                    .FirstOrDefault()?["thumbnails"]?["small"]?.ToString();
                return coverArtImageUrl;
            }
            catch
            {
                return "";
            }
        }

        public async Task<string> GetBestRelease(string releaseGroupId)
        {
            var songs = new List<Song>();
            var releaseGroupUrl = $"https://musicbrainz.org/ws/2/release-group/{releaseGroupId}?inc=releases+media&limit=100&fmt=json";

            var releaseGroupResponse = await _httpClient.GetStringAsync(releaseGroupUrl);
            var releaseGroupData = JObject.Parse(releaseGroupResponse);

            // among all official releases, get the earliest one which has the maximum number of songs
            var releases = releaseGroupData["releases"]
                .Where(r => r["status"]?.ToString() == "Official")
                .OrderByDescending(r => r["media"]?.Sum(m => (int)m["track-count"]))
                .ThenBy(r => r["date"]?.ToString());

            string releaseId;

            if (releases.Any())
            {
                releaseId = releases.FirstOrDefault()?["id"]?.ToString();
            }
            else
            {
                releaseId = releaseGroupData["releases"].FirstOrDefault()?["id"].ToString();
            }

            return releaseId;
        }

        public async Task<List<Song>> GetReleaseSongs(string releaseId)
        {
            var songs = new List<Song>();

            var releaseUrl = $"https://musicbrainz.org/ws/2/release/{releaseId}?inc=recordings+artist-credits&fmt=json";
            var releaseResponse = await _httpClient.GetStringAsync(releaseUrl);
            var releaseData = JObject.Parse(releaseResponse);

            var media = releaseData["media"];
            int totalDiscs = media?.Count() ?? 0;

            if (media != null)
            {
                for (int discNumber = 0; discNumber < media.Count(); discNumber++)
                {
                    var medium = media[discNumber];
                    var tracks = medium["tracks"];
                    int totalTracks = tracks?.Count() ?? 0;

                    foreach (var track in tracks)
                    {
                        var song = new Song
                        {
                            Id = track["recording"]?["id"]?.ToString(),
                            Title = track["recording"]?["title"]?.ToString(),
                            Artist = string.Join("; ", track["artist-credit"].Select(artist => artist["name"].ToString())),
                            Length = Convert.ToInt32(track["length"] ?? "0"),
                            TrackNumber = Convert.ToInt32(track["position"]?.ToString() ?? "0"),
                            TotalTracks = totalTracks,
                            DiscNumber = discNumber + 1,
                            TotalDiscs = totalDiscs
                        };
                        songs.Add(song);
                    }
                }
            }
            
            return songs;
        }

        public class Release
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Date { get; set; }
            public string CoverArtUrl { get; set; }
            public string Artist { get; set; }
            public bool IsReleaseGroup { get; set; }
        }

        public class Song
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Artist { get; set; }
            public int Length { get; set; }
            public int TrackNumber { get; set; }
            public int TotalTracks { get; set; }
            public int DiscNumber { get; set; }
            public int TotalDiscs { get; set; }
        }
    }
}
