using SpacetimeDB;

public static partial class PlayerModule
{
    [Table(Name = "online_player", Public = true)]
    public partial struct OnlinePlayer
    {
        [PrimaryKey]
        public Identity identity;

        [Unique, AutoInc]
        public uint player_id;

        public string name;
        public string color;

        [SpacetimeDB.Index.BTree]
        public uint room_id;
        public ulong last_room_join_time; // Time when the player joined the room

        public ulong last_connect_time; // Time when the player joined the room
        public ulong total_play_time; // Total time spent in the room

        public DbVector3 last_position;
        public float last_rotation;
    }

    [Table(Name = "player_count", Public = true)]
    public partial struct PlayerCount
    {
        [PrimaryKey]
        public uint id; // always 0
        public uint count;
    }

    [Table(Name = "logged_out_player")]
    public partial struct LoggedOutPlayer
    {
        [PrimaryKey]
        public Identity identity;

        public uint player_id;
        public string name;
        public string color;
        public ulong last_disconnect_time; // Time when the player left the room
        public ulong total_play_time; // Total time spent in the room
    }

    // Player-related reducers
    [Reducer]
    public static void SetPlayerProfile(ReducerContext ctx, string name, string color)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        player.name = name;
        player.color = color;
        ctx.Db.online_player.identity.Update(player);
    }

    [Reducer]
    public static void UpdateLastPosition(ReducerContext ctx, DbVector3 position, float rotation)
    {
        var player = ctx.Db.online_player.identity.Find(ctx.Sender) ?? throw new Exception("Player not found");
        if (player.room_id == uint.MaxValue)
            throw new Exception("Player must join a room first");
        player.last_position = position;
        player.last_rotation = rotation;
        ctx.Db.online_player.identity.Update(player);
    }

    public static string MakeKey(Identity identity, uint roomId) => $"{identity.ToString()}|{roomId}";
}