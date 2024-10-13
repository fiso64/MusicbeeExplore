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
        public static IRetriever GetRetriever(Retriever retriever, Config config)
        {
            switch (retriever)
            {
                case Retriever.Discogs:
                    return new DiscogsRetriever(config);
                case Retriever.MusicBrainz:
                    return new MusicBrainzRetriever(config);
                default:
                    throw new ArgumentException("Invalid source.");
            }
        }
    }
}
