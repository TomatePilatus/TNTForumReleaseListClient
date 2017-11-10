using System;

namespace TNTForumReleaseListClient
{
    public class Release
    {
        public Uri Torrent;
        public Uri Magnet;
        public Uri DescriptionPage;

        public byte Category;

        public uint Leechers;
        public uint Seeders;
        public uint Completed;

        public string Title;
        public string Notes;
    }
}