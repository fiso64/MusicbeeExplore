using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public interface ISimilarAlbumRetriever
    {
        Task<List<Release>> GetSimilarAlbumsAsync(string artist, string album, Action<string> statusChange, CancellationToken ct);
    }
}
