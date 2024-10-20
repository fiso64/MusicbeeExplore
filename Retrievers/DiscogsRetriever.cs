﻿using MusicBeePlugin.Models;
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

        public class DiscogsEntityRetrieverData : EntityRetrieverData
        {
            public int Id;
            public Api.Discogs.SearchEntityType EntityType;
            public DiscogsEntityRetrieverData() { Source = Models.Retriever.Discogs; }
        }

        Api.Discogs.Discogs _api;

        public DiscogsRetriever(Config config)
        {
            if (string.IsNullOrWhiteSpace(config.DiscogsToken))
                throw new ArgumentException("Discogs token cannot be null or empty.");
            _api = new Api.Discogs.Discogs(config.DiscogsToken);
        }

        public async Task<EntityRetrieverData> GetArtistAsync(string query, Action<string> statusChange, CancellationToken ct)
        {
            query = query.Trim();

            var entityType = Api.Discogs.SearchEntityType.Artist;
            bool exact = false;
            int retrieveLevel = 0;

            if (query.StartsWith(">"))
            {
                retrieveLevel = 1;
                if (query.StartsWith(">>"))
                    retrieveLevel = 2;
                query = query.TrimStart('>');
            }

            if (query.ToLower().StartsWith("l:"))
            {
                entityType = Api.Discogs.SearchEntityType.Label;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("a:") || query.ToLower().StartsWith("ar:"))
            {
                entityType = Api.Discogs.SearchEntityType.Artist;
                query = query.Substring(query.ToLower().StartsWith("ar:") ? 3 : 2);
            }

            if (query.StartsWith("\"") && query.EndsWith("\""))
            {
                exact = true;
                query = query.Substring(1, query.Length - 2);
            }

            statusChange($"Querying {entityType}: {query}");

            List<Api.Discogs.SearchEntity> entities;

            if (!exact)
            {
                entities = await _api.SearchEntityAsync(query, entityType, 1, ct);
            }
            else
            {
                entities = await _api.SearchEntityExactAsync(query, entityType, 1, ct);
            }

            if (entities.Count == 0)
            {
                statusChange($"No{(exact ? " exact" : "")} results found for {entityType}: {query}");
                return null;
            }

            string entityName = entities[0].Title;
            int entityId = entities[0].Id;

            entityName = Regex.Replace(entityName, @"\s\(\d+\)$", "");

            return new DiscogsEntityRetrieverData
            {
                Id = entityId,
                Name = entityName,
                CacheId = $"{new string('>', retrieveLevel)}{entityName}",
                EntityType = entityType,
                RetrieveLevel = retrieveLevel
            };
        }

        public async Task<List<Release>> GetReleasesAsync(EntityRetrieverData retrieverData, Action<string> statusChange, CancellationToken ct)
        {
            if (!(retrieverData is DiscogsEntityRetrieverData data))
            {
                throw new ArgumentException("Data must be of type DiscogsEntityRetrieverData.");
            }

            statusChange($"Getting releases for {data.EntityType}: {data.Name}");

            var res = await _api.GetReleasesAsync(data.Id, data.EntityType, ct);

            if (data.EntityType == Api.Discogs.SearchEntityType.Artist && data.RetrieveLevel != 2)
            {
                res = res.Where(release => release.Role == "Main" && (data.RetrieveLevel >= 1 || release.Type == "master")).ToList();
            }

            var releases = res.Select(r => new Release
            {
                Title = r.Title,
                Date = r.Year.ToString(),
                Thumb = r.Thumb,
                Artist = Regex.Replace(r.Artist.Trim(), @"\s\(\d+\)$", ""),
                AppearanceOnly = r.Role != "Main",
                RetrieverData = new DiscogsRetrieverData { Id = r.MainRelease?.ToString() ?? r.Id.ToString() },
            }).ToList();

            return releases;
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
