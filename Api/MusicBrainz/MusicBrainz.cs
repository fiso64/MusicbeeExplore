using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Api.MusicBrainz // todo: use json deserialization like the other APIs
{
    public class MusicBrainz
    {
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
    }
}
