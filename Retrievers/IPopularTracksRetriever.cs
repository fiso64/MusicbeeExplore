using MusicBeePlugin.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    internal interface IPopularTracksRetriever
    {
        Task<List<Track>> GetPopularTracksByArtistAsync(string artist);
    }
}
