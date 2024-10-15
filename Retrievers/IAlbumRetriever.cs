using MusicBeePlugin.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public interface IAlbumRetriever
    {
        Task<List<Track>> GetReleaseTracksAsync(RetrieverData data);
    }
}
