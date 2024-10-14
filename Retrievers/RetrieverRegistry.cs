using MusicBeePlugin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBeePlugin.Retrievers
{
    public static class RetrieverRegistry
    {
        public static IDiscographyRetriever GetDiscographyRetriever(Retriever retriever, Config config)
        {
            switch (retriever)
            {
                case Retriever.Discogs:
                    return new DiscogsRetriever(config);
                case Retriever.MusicBrainz:
                    return new MusicBrainzRetriever(config);
                default:
                    throw new ArgumentException($"Retriever {retriever} is not a valid discography retriever.");
            }
        }

        public static IAlbumRetriever GetAlbumRetriever(Retriever retriever, Config config)
        {
            switch (retriever)
            {
                case Retriever.Discogs:
                    return new DiscogsRetriever(config);
                case Retriever.MusicBrainz:
                    return new MusicBrainzRetriever(config);
                case Retriever.Lastfm:
                    return new LastfmRetriever(config);
                default:
                    throw new ArgumentException($"Retriever {retriever} is not a valid album retriever.");
            }
        }
    }
}
