namespace MusicBeePlugin.Api.MusicBrainz
{
    public enum Entity
    {
        Area,
        Artist,
        Event,
        Genre,
        Instrument,
        Label,
        Place,
        Recording,
        Release,
        ReleaseGroup,
        Series,
        Work,
        Url
    }

    public class Release
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Date { get; set; }
        public string CoverArtUrl { get; set; }
        public string Artist { get; set; }
        public bool IsReleaseGroup { get; set; }
    }

    public class Song
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public int Length { get; set; }
        public int TrackNumber { get; set; }
        public int TotalTracks { get; set; }
        public int DiscNumber { get; set; }
        public int TotalDiscs { get; set; }
    }
}
