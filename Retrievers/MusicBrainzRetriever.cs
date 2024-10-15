using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using MusicBeePlugin.Models;

namespace MusicBeePlugin.Retrievers
{
    public class MusicBrainzRetriever : IDiscographyRetriever, IAlbumRetriever
    {
        public class MusicBrainzRetrieverData : RetrieverData
        {
            public string Id;
            public bool isGroup;
            public MusicBrainzRetrieverData() { Source = Retriever.MusicBrainz; }
        }

        public class MusicBrainzEntityRetrieverData : EntityRetrieverData
        {
            public string Id;
            public Api.MusicBrainz.Entity Entity;
            public MusicBrainzEntityRetrieverData() { Source = Retriever.MusicBrainz; }
        }

        Api.MusicBrainz.MusicBrainz _api;

        public MusicBrainzRetriever(Config config)
        {
            _api = new Api.MusicBrainz.MusicBrainz();
        }

        public async Task<EntityRetrieverData> GetArtistAsync(string query, Action<string> statusChange, CancellationToken ct)
        {
            var entity = Api.MusicBrainz.Entity.Artist;
            bool exact = false;
            bool retrieveAll = false;

            if (query.ToLower().StartsWith(">"))
            {
                retrieveAll = true;
                query = query.TrimStart('>');
            }

            if (query.ToLower().StartsWith("l:"))
            {
                entity = Api.MusicBrainz.Entity.Label;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("a:"))
            {
                entity = Api.MusicBrainz.Entity.Artist;
                query = query.Substring(2);
            }
            else if (query.ToLower().StartsWith("ar:"))
            {
                entity = Api.MusicBrainz.Entity.Artist;
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
                return null;
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
                    return null;
                }

                entityName = exactMatch.name;
                entityId = exactMatch.id;
            }

            return new MusicBrainzEntityRetrieverData 
            { 
                Id = entityId, 
                Name = entityName, 
                CacheId = $"{(retrieveAll ? ">" : "")}{entityName}",
                Entity = entity, 
                RetrieveLevel = Convert.ToInt32(retrieveAll),
            };
        }

        public async Task<List<Release>> GetReleasesAsync(EntityRetrieverData retrieverData, Action<string> statusChange, CancellationToken ct)
        {
            if (!(retrieverData is MusicBrainzEntityRetrieverData data))
            {
                throw new ArgumentException("Data must be of type MusicBrainzEntityRetrieverData.");
            }

            var releases = new List<Release>();

            statusChange($"Getting releases for {data.Entity}: {data.Name}");

            if (data.Entity == Api.MusicBrainz.Entity.Artist)
            {
                var res = await _api.GetReleaseGroupsByArtist(data.Id, data.Name, ct);
                releases = res.Select(r => new Release
                {
                    Title = r.Name,
                    Date = r.Date,
                    Thumb = r.CoverArtUrl,
                    Artist = r.Artist,
                    RetrieverData = new MusicBrainzRetrieverData { Id = r.Id, isGroup = r.IsReleaseGroup },
                }).ToList();

                if (data.RetrieveLevel > 0)
                {
                    var appearsOnReleases = await _api.GetAppearsOnReleasesByArtist(data.Id, ct);
                    releases.AddRange(appearsOnReleases.Select(r => new Release
                    {
                        Title = r.Name,
                        Date = r.Date,
                        Thumb = r.CoverArtUrl,
                        Artist = r.Artist,
                        AppearanceOnly = true,
                        RetrieverData = new MusicBrainzRetrieverData { Id = r.Id, isGroup = false },
                    }));
                }
            }
            else if (data.Entity == Api.MusicBrainz.Entity.Label)
            {
                var res = await _api.GetReleasesByLabel(data.Id, ct, uniqueNamesOnly: true);
                releases = res.Select(r => new Release
                {
                    Title = r.Name,
                    Date = r.Date,
                    Thumb = r.CoverArtUrl,
                    Artist = r.Artist,
                    RetrieverData = new MusicBrainzRetrieverData { Id = r.Id, isGroup = false },
                }).ToList();
            }

            return releases;
        }

        public async Task<List<Track>> GetReleaseTracksAsync(RetrieverData retrieverData)
        {
            if (!(retrieverData is MusicBrainzRetrieverData data))
            {
                throw new ArgumentException("Data must be of type MusicBrainzRetrieverData.");
            }
            string id = data.isGroup ? await _api.GetBestRelease(data.Id) : data.Id;

            var songs = await _api.GetReleaseSongs(id);

            var res = songs.Select(s => new Track
            {
                Title = s.Title,
                Artist = s.Artist,
                Length = s.Length,
                TrackPosition = $"{s.TrackNumber}/{s.TotalTracks}",
                DiscPosition = $"{s.DiscNumber}/{s.TotalDiscs}",
                RetrieverData = new RetrieverData { Source = Retriever.MusicBrainz },
            }).ToList();

            return res;
        }
    }
}
