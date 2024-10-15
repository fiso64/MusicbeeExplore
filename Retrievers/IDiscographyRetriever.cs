using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public interface IDiscographyRetriever
    {
        Task<EntityRetrieverData> GetArtistAsync(string query, Action<string> statusChange, CancellationToken ct);
        Task<List<Release>> GetReleasesAsync(EntityRetrieverData artistData, Action<string> statusChange, CancellationToken ct);
    }
}
