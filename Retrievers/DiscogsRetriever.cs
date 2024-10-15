using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public class DiscogsRetriever : IDiscographyRetriever, IAlbumRetriever
    {
        public class DiscogsRetrieverData : RetrieverData
        {
            public string Id;
            public DiscogsRetrieverData() { Source = Models.Retriever.Discogs; }
        }

        Api.Discogs.Discogs _api;

        public DiscogsRetriever(Config config)
        {
            if (string.IsNullOrWhiteSpace(config.DiscogsToken))
                throw new ArgumentException("Discogs token cannot be null or empty.");
            _api = new Api.Discogs.Discogs(config.DiscogsToken);
        }

        public async Task<(string entityName, List<Models.Release> releases)> GetReleasesAsync(string query, Action<string> statusChange, CancellationToken ct)
        {
            var releases = new List<Models.Release>();
            var entityType = Api.Discogs.SearchEntityType.Artist;
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
                entityType = Api.Discogs.SearchEntityType.Label;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("a:"))
            {
                entityType = Api.Discogs.SearchEntityType.Artist;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("ar:"))
            {
                entityType = Api.Discogs.SearchEntityType.Artist;
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

            if (entityType == Api.Discogs.SearchEntityType.Artist && retrieveLevel != 2)
            {
                res = res.Where(release => release.Role == "Main" && (retrieveLevel >= 1 || release.Type == "master")).ToList();
            }

            releases = res.Select(r => new Models.Release
            {
                Title = r.Title,
                Date = r.Year.ToString(),
                Thumb = r.Thumb,
                Artist = Regex.Replace(r.Artist.Trim(), @"\s\(\d+\)$", ""),
                AppearanceOnly = r.Role != "Main",
                RetrieverData = new DiscogsRetrieverData { Id = r.MainRelease?.ToString() ?? r.Id.ToString() },
            }).ToList();

            return (Regex.Replace(entityName, @"\s\(\d+\)$", ""), releases);
        }

        public async Task<List<Models.Track>> GetReleaseTracksAsync(RetrieverData retrieverData)
        {
            if (!(retrieverData is DiscogsRetrieverData data))
                throw new ArgumentException("Data must be of type DiscogsRetrieverData.");

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
                    RetrieverData = new RetrieverData { Source = Models.Retriever.Discogs },
                };
                return track;
            }).ToList();

            return res;
        }
    }
}
