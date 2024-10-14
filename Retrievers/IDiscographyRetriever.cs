using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public interface IDiscographyRetriever
    {
        Task<(string entityName, List<Release> releases)> GetReleasesAsync(string query, Action<string> statusChange, CancellationToken ct);
    }
}
