using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public interface IDiscographyRetriever
    {
        //Task<()> GetArtist(string query, CancellationToken ct);
        Task<(string entityName, List<Release> releases)> GetReleasesAsync(string query, Action<string> statusChange, CancellationToken ct);
    }
}
