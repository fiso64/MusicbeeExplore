using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Api.Discogs
{
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

        public async Task<List<SearchEntity>> SearchEntityExactAsync(string query, SearchEntityType entityType, int limit, CancellationToken cancellationToken = default)
        {
            var type = entityType.ToString().ToLower();
            var exactMatches = new List<SearchEntity>();
            int page = 1;
            bool hasMore = true;

            while (hasMore && exactMatches.Count < limit)
            {
                var requestUrl = $"https://api.discogs.com/database/search?q={Uri.EscapeDataString(query)}&type={type}&per_page=100&page={page}";
                var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var searchResult = JsonConvert.DeserializeObject<SearchResult>(jsonResponse);

                foreach (var result in searchResult.Results)
                {
                    if (string.Equals(result.Title, query, StringComparison.OrdinalIgnoreCase))
                    {
                        exactMatches.Add(result);
                        if (exactMatches.Count >= limit)
                        {
                            break;
                        }
                    }
                }

                hasMore = searchResult.Pagination.Page < searchResult.Pagination.Pages;
                page++;
            }

            return exactMatches;
        }

        public async Task<List<Release>> GetReleasesAsync(int entityId, SearchEntityType artistOrLabel, CancellationToken cancellationToken = default, int lookupLimit = 150)
        {
            if (artistOrLabel != SearchEntityType.Artist && artistOrLabel != SearchEntityType.Label)
            {
                throw new ArgumentException("Entity type must be either Artist or Label.");
            }

            var allReleases = new List<Release>();
            int page = 1;
            bool hasMore = true;

            while (hasMore && allReleases.Count < lookupLimit)
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
