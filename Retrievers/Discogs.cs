using MusicBeePlugin.Retrievers.DiscogsData;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public class DiscogsRetriever : IDiscographyRetriever, IAlbumRetriever
    {
        Discogs _api;

        public DiscogsRetriever(Config config)
        {
            if (string.IsNullOrWhiteSpace(config.DiscogsToken))
                throw new ArgumentException("Discogs token cannot be null or empty.");
            _api = new Discogs(config.DiscogsToken);
        }

        public async Task<(string entityName, List<Models.Release> releases)> GetReleasesAsync(string query, Action<string> statusChange, CancellationToken ct)
        {
            var releases = new List<Models.Release>();
            var entityType = SearchEntityType.Artist;
            bool exact = false;
            int retrieveLevel = 0;

            if (query.StartsWith(">"))
            {
                retrieveLevel = 1;
                if (query.StartsWith(">>"))
                    retrieveLevel = 2;
                query = query.Substring(retrieveLevel);
            }

            if (query.ToLower().StartsWith("l:"))
            {
                entityType = SearchEntityType.Label;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("a:"))
            {
                entityType = SearchEntityType.Artist;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("ar:"))
            {
                entityType = SearchEntityType.Artist;
                query = query.Substring(3);
            }

            if (query.StartsWith("\"") && query.EndsWith("\""))
            {
                exact = true;
                query = query.Substring(1, query.Length - 2);
            }

            statusChange($"Querying {entityType}: {query}");

            var entities = await _api.SearchEntityAsync(query, entityType, exact ? 20 : 1, ct);

            if (entities.Count == 0)
            {
                statusChange($"No results found for {entityType}: {query}");
                return (null, new List<Models.Release>());
            }

            string entityName;
            int entityId;

            if (!exact)
            {
                entityName = entities[0].Title;
                entityId = entities[0].Id;
            }
            else
            {
                var exactMatch = entities.FirstOrDefault(e => e.Title.Equals(query, StringComparison.OrdinalIgnoreCase));
                if (exactMatch == null)
                {
                    statusChange($"No exact match found for {entityType}: {query}");
                    return (null, new List<Models.Release>());
                }

                entityName = exactMatch.Title;
                entityId = exactMatch.Id;
            }

            statusChange($"Getting releases for {entityType}: {entityName}");

            var res = await _api.GetReleasesAsync(entityId, entityType, ct);

            if (entityType == SearchEntityType.Artist && retrieveLevel != 2)
            {
                res = res.Where(release => release.Role == "Main" && (retrieveLevel >= 1 || release.Type == "master")).ToList();
            }

            releases = res.Select(r => new Models.Release
            {
                Id = r.MainRelease?.ToString() ?? r.Id.ToString(),
                Title = r.Title,
                Date = r.Year.ToString(),
                Thumb = r.Thumb,
                Artist = Regex.Replace(r.Artist.Trim(), @"\s\(\d+\)$", ""),
                Source = Models.Retriever.Discogs,
                AppearanceOnly = r.Role != "Main",
            }).ToList();

            return (Regex.Replace(entityName, @"\s\(\d+\)$", ""), releases);
        }

        public async Task<List<Models.Track>> GetReleaseTracksAsync(Models.CommentData data)
        {
            var releaseDetails = await _api.GetReleaseTracksAsync(int.Parse(data.Id));

            var res = releaseDetails.Tracklist.Select(s => {
                var artistNames = s.Artists?.Select(a => Regex.Replace(a.Name, @"\s\(\d+\)$", ""))
                     ?? releaseDetails.Artists.Select(a => Regex.Replace(a.Name, @"\s\(\d+\)$", ""));
                var track = new Models.Track
                {
                    Title = s.Title,
                    Artist = string.Join("; ", artistNames),
                    Length = int.TryParse(s.Duration, out int l) ? l : 0,
                    TrackPosition = s.Position + "/" + releaseDetails.Tracklist.Count,
                    Source = Models.Retriever.Discogs
                };
                return track;
            }).ToList();

            return res;
        }
    }

    public class Discogs
    {
        private readonly HttpClient _httpClient;

        public Discogs(string personalAccessToken)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Discogs", $"token={personalAccessToken}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DiscogsApiClient/1.0");
        }

        public async Task<List<SearchEntity>> SearchEntityAsync(string query, SearchEntityType entityType, int limit, CancellationToken cancellationToken = default)
        {
            var type = entityType.ToString().ToLower();
            var requestUrl = $"https://api.discogs.com/database/search?q={Uri.EscapeDataString(query)}&type={type}&per_page={limit}";
            var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var jsonResponse = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<SearchResult>(jsonResponse).Results;
        }

        public async Task<List<Release>> GetReleasesAsync(int entityId, SearchEntityType artistOrLabel, CancellationToken cancellationToken = default)
        {
            if (artistOrLabel != SearchEntityType.Artist && artistOrLabel != SearchEntityType.Label)
            {
                throw new ArgumentException("Entity type must be either Artist or Label.");
            }

            const int LOOKUP_LIMIT = 150;
            var allReleases = new List<Release>();
            int page = 1;
            bool hasMore = true;

            while (hasMore && allReleases.Count < LOOKUP_LIMIT)
            {
                var type = artistOrLabel.ToString().ToLower();
                var requestUrl = $"https://api.discogs.com/{type}s/{entityId}/releases?page={page}&per_page=100&sort=year&sort_order=desc";
                var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var entityReleases = JsonConvert.DeserializeObject<ReleasesResult>(jsonResponse);

                if (entityReleases.Releases != null)
                {
                    allReleases.AddRange(entityReleases.Releases);
                }

                hasMore = entityReleases.Pagination.Page < entityReleases.Pagination.Pages;
                page++;
            }

            return allReleases;
        }

        public async Task<ReleaseDetail> GetReleaseTracksAsync(int releaseId, CancellationToken cancellationToken = default)
        {
            var requestUrl = $"https://api.discogs.com/releases/{releaseId}";
            var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var releaseDetails = JsonConvert.DeserializeObject<ReleaseDetail>(jsonResponse);
            releaseDetails.Tracklist = releaseDetails.Tracklist.Where(t => t.Type == "track").ToList();
            return releaseDetails;
        }
    }
}
