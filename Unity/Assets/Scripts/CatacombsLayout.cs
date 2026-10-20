namespace GuerraDasNacoes
{
    /// <summary>Shared map coordinates only. Gameplay uses the exported physics grid, never this
    /// authoring footprint, so tombs, pillars and the character capsule remain authoritative.</summary>
    public static class CatacombsLayout
    {
        public const string MapId = "catacombs";
        public const string RootName = "Catacumbas";
        public const float OriginX = -24f, OriginZ = -10f, CellSize = .25f;
        public const int GridWidth = 192, GridHeight = 288;

        public readonly struct Point
        {
            public readonly float X, Z;
            public Point(float x, float z) { X = x; Z = z; }
        }

        public readonly struct Room
        {
            public readonly string Name;
            public readonly float MinX, MaxX, MinZ, MaxZ;
            public Room(string name, float minX, float maxX, float minZ, float maxZ)
            { Name = name; MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; }
            public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
        }

        public static readonly Point Entry = new Point(0, 0);
        public static readonly Point ReturnPortal = new Point(0, -3);
        public static readonly Point ValePortal = new Point(0, 19);
        public static readonly Point ValeReturn = new Point(0, 17);
        public static readonly Point[] SpawnPoints = {
            new Point(-12,14), new Point(-14,20), new Point(0,27),
            new Point(12,30), new Point(14,36), new Point(-5,49), new Point(5,52)
        };
        public static readonly Room[] Rooms = {
            new Room("Vestíbulo", -7,7,-7,7),
            new Room("Galeria central", -3,3,4,50),
            new Room("Ossuário", -19,-5,10,24),
            new Room("Passagem do ossuário", -7,0,14,20),
            new Room("Câmara do ritual", 5,19,26,40),
            new Room("Passagem do ritual", 0,7,30,36),
            new Room("Cripta dos guardiões", -10,10,44,58)
        };

        public static bool IsFloor(float x, float z)
        {
            foreach (Room room in Rooms) if (room.Contains(x, z)) return true;
            return false;
        }
    }
}
