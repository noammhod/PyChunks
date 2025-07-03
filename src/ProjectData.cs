using System.Collections.Generic;

namespace PyChunks
{
    public class ChunkData
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
    }

    public class ProjectData
    {
        public List<ChunkData> Chunks { get; set; }

        public ProjectData()
        {
            Chunks = new List<ChunkData>();
        }
    }
}

