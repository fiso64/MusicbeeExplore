using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public interface IAlbumRetriever
    {
        Task<List<Track>> GetReleaseTracksAsync(CommentData data);
    }
}
